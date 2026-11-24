using Microsoft.AspNetCore.Authorization;
using Claims.API.Auth;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Claims.API.Data;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Authorize(Policy = AuthPolicies.ClaimsViewer)]
[Route("api/creditor")]
[ApiController]
public class CreditorSummaryController(ApplicationDbContext context) : ControllerBase
{
  private const int MaxPageSize = 100;

  [HttpGet("summary")]
  public async Task<ActionResult<List<CreditorTotalsDto>>> Totals()
  {
    var rows = await context.ServiceFundAllocations.AsNoTracking()
      .Where(allocation => allocation.Status && allocation.CreditorId != null
        && allocation.TranService.Transaction.ApprovalStatusId == 3
        && allocation.TranService.Transaction.IsReleased)
      .GroupBy(allocation => allocation.CreditorId!.Value)
      .Select(g => new
      {
        CreditorId = g.Key,
        Claims = g.Select(allocation => allocation.TranService.TransactionId)
          .Distinct()
          .Count(),
        Amount = g.Sum(allocation => allocation.Amount),
        LastClaim = g.Max(allocation => allocation.TranService.Transaction.Date),
        AllClaims = g.Select(allocation => allocation.TranService.TransactionId)
          .Distinct()
          .Count(),
      })
      .ToListAsync();

    return rows
      .Select(r => new CreditorTotalsDto(r.CreditorId, r.Claims, r.Amount ?? 0, r.LastClaim, r.AllClaims))
      .ToList();
  }

  [HttpGet("{id:int}/summary")]
  public async Task<ActionResult<CreditorSummaryDto>> Summary(int id)
  {
    var creditor = await context.Creditors.AsNoTracking()
      .Where(c => c.CreditorId == id)
      .Select(c => new { c.CreditorName, c.Status })
      .FirstOrDefaultAsync();
    if (creditor is null)
    {
      return NotFound(new { message = "Creditor not found." });
    }

    var claims = ClaimsForCreditor(id);

    var byYear = await claims
      .Where(claim => claim.Transaction.Date != null)
      .GroupBy(claim => claim.Transaction.Date!.Value.Year)
      .Select(g => new { Year = g.Key, Claims = g.Count(), Amount = g.Sum(claim => claim.Amount) })
      .ToListAsync();

    return new CreditorSummaryDto(
      id,
      creditor.CreditorName,
      creditor.Status,
      await claims.CountAsync(),
      await claims.SumAsync(claim => claim.Amount) ?? 0,
      await claims.MinAsync(claim => claim.Transaction.Date),
      await claims.MaxAsync(claim => claim.Transaction.Date),
      await claims.CountAsync(),
      await claims.CountAsync(claim => claim.Transaction.Date == null),
      byYear
        .OrderByDescending(y => y.Year)
        .Select(y => new CreditorYearDto(y.Year, y.Claims, y.Amount ?? 0))
        .ToList());
  }

  [HttpGet("{id:int}/programs")]
  public async Task<ActionResult<List<CreditorProgramDto>>> Programs(int id)
  {
    if (!await context.Creditors.AnyAsync(c => c.CreditorId == id))
    {
      return NotFound(new { message = "Creditor not found." });
    }

    var counts = await ClaimsForCreditor(id)
      .Where(claim =>
        claim.Transaction.AssistanceCode != null)
      .GroupBy(claim => claim.Transaction.AssistanceCode!)
      .Select(g => new { Code = g.Key, Claims = g.Count() })
      .ToListAsync();
    var programs = await ProgramNamesAsync();

    return counts
      .GroupBy(c => c.Code.Trim().ToUpperInvariant())
      .Select(g => new CreditorProgramDto(g.Key, programs.GetValueOrDefault(g.Key), g.Sum(c => c.Claims)))
      .OrderByDescending(p => p.Claims)
      .ToList();
  }

  [HttpGet("{id:int}/claims")]
  public async Task<ActionResult<CreditorClaimsPageDto>> Claims(
    int id,
    [FromQuery] ClaimFilter filter,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
  {
    if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
    {
      return BadRequest(new { message = $"Page must be 1 or more and page size between 1 and {MaxPageSize}." });
    }
    var (query, error) = await FilteredClaimsAsync(id, filter);
    if (error is not null)
    {
      return error;
    }

    var total = await query!.CountAsync();
    var totalAmount = await query!.SumAsync(claim => claim.Amount) ?? 0;
    var rows = await Rows(Sorted(query!, filter).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
    var programs = await ProgramNamesAsync();

    return new CreditorClaimsPageDto(
      page,
      pageSize,
      total,
      (int)Math.Ceiling(total / (double)pageSize),
      totalAmount,
      rows.Select(r => new CreditorClaimDto(
          r.TransactionId,
          r.RefNo,
          r.Date,
          r.PatientPersonId,
          PersonName(r.Last, r.First, r.Middle),
          r.AssistanceCode,
          ProgramName(programs, r.AssistanceCode),
          r.Amount))
        .ToList());
  }

  [HttpGet("{id:int}/claims/export")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Export(int id, [FromQuery] ClaimFilter filter)
  {
    var (query, error) = await FilteredClaimsAsync(id, filter);
    if (error is not null)
    {
      return error;
    }

    var rows = await Rows(Sorted(query!, filter)).ToListAsync();
    var programs = await ProgramNamesAsync();
    var name = await context.Creditors.Where(c => c.CreditorId == id).Select(c => c.CreditorName).FirstAsync();

    var csv = new StringBuilder();
    csv.AppendLine("Ref No,Date,Patient,Program Code,Program,Amount");
    foreach (var r in rows)
    {
      csv.AppendLine(string.Join(",",
        Csv(r.RefNo),
        Csv(r.Date?.ToString("yyyy-MM-dd")),
        Csv(PersonName(r.Last, r.First, r.Middle)),
        Csv(r.AssistanceCode?.Trim()),
        Csv(ProgramName(programs, r.AssistanceCode)),
        r.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""));
    }

    var period = filter.From is null && filter.To is null
      ? "all"
      : $"{filter.From?.ToString("yyyy-MM-dd") ?? "start"}_to_{filter.To?.ToString("yyyy-MM-dd") ?? "now"}";
    var fileName = $"{Regex.Replace(name ?? $"creditor-{id}", "[^A-Za-z0-9]+", "-").Trim('-')}_{period}.csv";
    // bom so excel reads ñ correctly
    return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
      "text/csv", fileName);
  }

  [HttpGet("{id:int}/months")]
  public async Task<ActionResult<List<CreditorMonthDto>>> Months(int id, [FromQuery] int year)
  {
    if (!await context.Creditors.AnyAsync(c => c.CreditorId == id))
    {
      return NotFound(new { message = "Creditor not found." });
    }

    var start = new DateTime(year, 1, 1);
    var end = start.AddYears(1);
    var months = await ClaimsForCreditor(id)
      .Where(claim =>
        claim.Transaction.Date >= start && claim.Transaction.Date < end)
      .GroupBy(claim => claim.Transaction.Date!.Value.Month)
      .Select(g => new { Month = g.Key, Claims = g.Count(), Amount = g.Sum(claim => claim.Amount) })
      .ToListAsync();

    return months
      .OrderBy(m => m.Month)
      .Select(m => new CreditorMonthDto(m.Month, m.Claims, m.Amount ?? 0))
      .ToList();
  }

  public class ClaimFilter
  {
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Search { get; set; }
    public string? Program { get; set; }
    public string Sort { get; set; } = "date";
    public string Dir { get; set; } = "desc";
  }

  private async Task<(IQueryable<CreditorClaimQuery>? Query, ActionResult? Error)> FilteredClaimsAsync(int id, ClaimFilter f)
  {
    if (f.From is not null && f.To is not null && f.From.Value.Date > f.To.Value.Date)
    {
      return (null, BadRequest(new { message = "The start date cannot be later than the end date." }));
    }
    if (f.Sort is not ("date" or "amount") || f.Dir is not ("asc" or "desc"))
    {
      return (null, BadRequest(new { message = "Sort must be date or amount, and dir asc or desc." }));
    }
    if (!await context.Creditors.AnyAsync(c => c.CreditorId == id))
    {
      return (null, NotFound(new { message = "Creditor not found." }));
    }

    var query = ClaimsForCreditor(id);
    if (f.From is not null)
    {
      var from = f.From.Value.Date;
      query = query.Where(claim => claim.Transaction.Date >= from);
    }
    if (f.To is not null)
    {
      var endExclusive = f.To.Value.Date.AddDays(1);
      query = query.Where(claim => claim.Transaction.Date < endExclusive);
    }
    if (!string.IsNullOrWhiteSpace(f.Program))
    {
      var code = f.Program.Trim();
      query = query.Where(claim =>
        claim.Transaction.AssistanceCode != null && claim.Transaction.AssistanceCode.Trim() == code);
    }
    if (!string.IsNullOrWhiteSpace(f.Search))
    {
      // stored in capitals, so this works under any collation
      var whole = f.Search.Trim().ToUpperInvariant();
      foreach (var word in whole.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        query = query.Where(claim =>
          (claim.Transaction.RefNo != null && claim.Transaction.RefNo.Contains(whole))
          || (claim.Transaction.PatientPerson != null
            && ((claim.Transaction.PatientPerson.LastName != null && claim.Transaction.PatientPerson.LastName.Contains(word))
              || (claim.Transaction.PatientPerson.FirstName != null && claim.Transaction.PatientPerson.FirstName.Contains(word))
              || (claim.Transaction.PatientPerson.MiddleName != null && claim.Transaction.PatientPerson.MiddleName.Contains(word)))));
      }
    }
    return (query, null);
  }

  private IQueryable<CreditorClaimQuery> ClaimsForCreditor(int creditorId)
  {
    var allocationTotals = context.ServiceFundAllocations.AsNoTracking()
      .Where(allocation => allocation.Status && allocation.CreditorId == creditorId
        && allocation.TranService.Transaction.ApprovalStatusId == 3
        && allocation.TranService.Transaction.IsReleased)
      .GroupBy(allocation => allocation.TranService.TransactionId)
      .Select(group => new
      {
        TransactionId = group.Key,
        Amount = group.Sum(allocation => allocation.Amount),
      });

    return from transaction in context.Transactions.AsNoTracking()
      join allocation in allocationTotals on transaction.TransactionId equals allocation.TransactionId
      select new CreditorClaimQuery { Transaction = transaction, Amount = allocation.Amount };
  }

  private static IQueryable<CreditorClaimQuery> Sorted(IQueryable<CreditorClaimQuery> query, ClaimFilter f) =>
    ((f.Sort, f.Dir) switch
    {
      ("amount", "asc") => query.OrderBy(claim => claim.Amount == null).ThenBy(claim => claim.Amount),
      ("amount", _) => query.OrderBy(claim => claim.Amount == null).ThenByDescending(claim => claim.Amount),
      (_, "asc") => query.OrderBy(claim => claim.Transaction.Date == null).ThenBy(claim => claim.Transaction.Date),
      _ => query.OrderBy(claim => claim.Transaction.Date == null).ThenByDescending(claim => claim.Transaction.Date),
    }).ThenByDescending(claim => claim.Transaction.TransactionId);

  private static IQueryable<ClaimRow> Rows(IQueryable<CreditorClaimQuery> query) =>
    query.Select(claim => new ClaimRow(
      claim.Transaction.TransactionId,
      claim.Transaction.RefNo,
      claim.Transaction.Date,
      claim.Transaction.PatientPersonId,
      claim.Transaction.PatientPerson!.LastName,
      claim.Transaction.PatientPerson.FirstName,
      claim.Transaction.PatientPerson.MiddleName,
      claim.Transaction.AssistanceCode,
      claim.Amount));

  private sealed record ClaimRow(
    int TransactionId, string? RefNo, DateTime? Date, int? PatientPersonId,
    string? Last, string? First, string? Middle, string? AssistanceCode, decimal? Amount);

  private sealed class CreditorClaimQuery
  {
    public Transaction Transaction { get; init; } = null!;
    public decimal? Amount { get; init; }
  }

  // quote text; a leading ' stops excel formulas (=, +, -, @)
  private static string Csv(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return "";
    }
    if ("=+-@".Contains(value[0]))
    {
      value = "'" + value;
    }
    return "\"" + value.Replace("\"", "\"\"") + "\"";
  }

  [HttpGet("{id:int}/claims/{transactionId:int}")]
  public async Task<ActionResult<CreditorClaimDetailDto>> Claim(int id, int transactionId)
  {
    var t = await ClaimsForCreditor(id)
      .Where(claim => claim.Transaction.TransactionId == transactionId)
      .Select(claim => new
      {
        claim.Transaction.TransactionId,
        claim.Transaction.RefNo,
        claim.Transaction.Date,
        claim.Amount,
        claim.Transaction.AssistanceCode,
        claim.Transaction.IsReleased,
        claim.Transaction.ApprovalStatusId,
        claim.Transaction.PatientPersonId,
        Patient = claim.Transaction.PatientPerson == null ? null : new
        {
          claim.Transaction.PatientPerson.LastName,
          claim.Transaction.PatientPerson.FirstName,
          claim.Transaction.PatientPerson.MiddleName,
          Sex = claim.Transaction.PatientPerson.Sex != null
            ? claim.Transaction.PatientPerson.Sex.SexDesc
            : claim.Transaction.PatientPerson.Gender,
          claim.Transaction.PatientPerson.Barangay,
          claim.Transaction.PatientPerson.Municipality,
        },
        claim.Transaction.RepresentativePersonId,
        Representative = claim.Transaction.RepresentativePerson == null ? null : new
        {
          claim.Transaction.RepresentativePerson.LastName,
          claim.Transaction.RepresentativePerson.FirstName,
          claim.Transaction.RepresentativePerson.MiddleName,
        },
        RepresentativeText = claim.Transaction.Representative,
        SocialWorkerUser = claim.Transaction.SocialWorkerUser == null ? null : new
        {
          claim.Transaction.SocialWorkerUser.Lastname,
          claim.Transaction.SocialWorkerUser.Firstname,
          claim.Transaction.SocialWorkerUser.Middlename,
        },
        claim.Transaction.SocialWorkerId,
        claim.Transaction.SocialWorkerAssessment,
        claim.Transaction.Remarks,
        claim.Transaction.CreatedBy,
        claim.Transaction.DateCreated,
        claim.Transaction.LastUpdatedBy,
        claim.Transaction.DateLastUpdated,
        Natures = claim.Transaction.TransactionServices
          .Where(s => s.Nature != null)
          .Select(s => s.Nature!.NatureDesc)
          .ToList(),
      })
      .FirstOrDefaultAsync();
    if (t is null)
    {
      return NotFound(new { message = "Claim not found for this creditor." });
    }

    var allocation = await context.ServiceFundAllocations.AsNoTracking()
      .Where(a => a.Status && a.CreditorId == id && a.TranService.TransactionId == transactionId)
      .OrderBy(a => a.ServiceFundAllocationId)
      .Select(a => new
      {
        Mode = a.Mode != null ? a.Mode.ModeDesc : null,
        FundSource = a.FundSource != null && a.FundSource.Source != null
          ? a.FundSource.Source.SourceDesc
          : null,
        FundYear = a.FundSource != null ? a.FundSource.Year : null,
      })
      .FirstOrDefaultAsync();

    string? socialWorker = null;
    string? socialWorkerPosition = null;
    if (t.SocialWorkerUser is not null)
    {
      socialWorker = PersonName(t.SocialWorkerUser.Lastname, t.SocialWorkerUser.Firstname, t.SocialWorkerUser.Middlename);
    }
    else if (int.TryParse(t.SocialWorkerId?.Trim(), out var officerId))
    {
      // Officers has passwords: name and position only
      var officer = await context.Officers.AsNoTracking()
        .Where(o => o.Idnumber == officerId)
        .Select(o => new { o.Name, o.Position })
        .FirstOrDefaultAsync();
      socialWorker = officer?.Name;
      socialWorkerPosition = officer?.Position;
    }

    var userIds = new[] { t.CreatedBy, t.LastUpdatedBy }.OfType<int>().ToList();
    var users = await context.Users.AsNoTracking()
      .Where(u => userIds.Contains(u.UserId))
      .Select(u => new { u.UserId, u.Lastname, u.Firstname, u.Middlename })
      .ToListAsync();
    string? UserName(int? userId) =>
      users.Where(u => u.UserId == userId).Select(u => PersonName(u.Lastname, u.Firstname, u.Middlename)).FirstOrDefault();

    var programs = await ProgramNamesAsync();

    return new CreditorClaimDetailDto(
      t.TransactionId,
      t.RefNo,
      t.Date,
      t.Amount,
      t.AssistanceCode,
      ProgramName(programs, t.AssistanceCode),
      t.Natures.OfType<string>().ToList(),
      t.IsReleased,
      t.ApprovalStatusId == 3 && t.IsReleased,
      t.Patient is null || t.PatientPersonId is null
        ? null
        : new ClaimPersonDto(
          t.PatientPersonId.Value,
          PersonName(t.Patient.LastName, t.Patient.FirstName, t.Patient.MiddleName),
          t.Patient.Sex,
          JoinNonEmpty(", ", t.Patient.Barangay, t.Patient.Municipality)),
      t.RepresentativePersonId,
      t.Representative is not null
        ? PersonName(t.Representative.LastName, t.Representative.FirstName, t.Representative.MiddleName)
        : string.IsNullOrWhiteSpace(t.RepresentativeText) ? null : t.RepresentativeText.Trim(),
      allocation?.Mode,
      JoinNonEmpty(" ", allocation?.FundSource, allocation?.FundYear?.ToString()),
      socialWorker,
      socialWorkerPosition,
      t.SocialWorkerAssessment,
      t.Remarks,
      UserName(t.CreatedBy),
      t.DateCreated,
      UserName(t.LastUpdatedBy),
      t.DateLastUpdated);
  }

  // Program names are keyed by source code
  private async Task<Dictionary<string, string?>> ProgramNamesAsync() =>
    (await context.SourceOfAssistances.AsNoTracking()
      .Where(source => source.SourceCode != null)
      .Select(source => new { AssistanceCode = source.SourceCode, AssistanceType = source.SourceDesc })
      .ToListAsync())
    .GroupBy(a => a.AssistanceCode!.Trim().ToUpperInvariant())
    .ToDictionary(g => g.Key, g => g.First().AssistanceType);

  private static string? ProgramName(Dictionary<string, string?> programs, string? code) =>
    code is null ? null : programs.GetValueOrDefault(code.Trim().ToUpperInvariant());

  private static string? PersonName(string? last, string? first, string? middle)
  {
    var name = string.Join(" ", new[] { last is null ? null : $"{last},", first, middle }
      .Where(s => !string.IsNullOrWhiteSpace(s)));
    return name.Length == 0 ? null : name;
  }

  private static string? JoinNonEmpty(string separator, params string?[] parts)
  {
    var text = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    return text.Length == 0 ? null : text;
  }
}
