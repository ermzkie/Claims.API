using Claims.API.Data;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

// Read-only claim figures for the Creditors pages. Adding, renaming and removing creditors stays in
// CreditorController.
//
// A claim counts unless it was deleted in this system (Status false with a CreatedBy). Claims imported from
// the old system have no CreatedBy, and all of 2022 plus the undated ones came over with Status false, so
// for them Status does not mean deleted (checked against sysCLAIMS on 2026-11-13).
[Route("api/creditor")]
[ApiController]
public class CreditorSummaryController(ApplicationDbContext context) : ControllerBase
{
  private const int MaxPageSize = 100;

  // Totals for every creditor that has claims; creditors without claims are left out.
  [HttpGet("summary")]
  public async Task<ActionResult<List<CreditorTotalsDto>>> Totals()
  {
    var rows = await context.Transactions.AsNoTracking()
      .Where(t => t.CreditorId != null)
      .GroupBy(t => t.CreditorId!.Value)
      .Select(g => new
      {
        CreditorId = g.Key,
        Claims = g.Count(t => t.Status || t.CreatedBy == null),
        Amount = g.Where(t => t.Status || t.CreatedBy == null).Sum(t => t.Amount),
        LastClaim = g.Where(t => t.Status || t.CreatedBy == null).Max(t => t.Date),
        AllClaims = g.Count(),
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

    var claims = context.Transactions.AsNoTracking().Where(t => t.CreditorId == id);
    var active = claims.Where(t => t.Status || t.CreatedBy == null);

    var byYear = await active
      .Where(t => t.Date != null)
      .GroupBy(t => t.Date!.Value.Year)
      .Select(g => new { Year = g.Key, Claims = g.Count(), Amount = g.Sum(t => t.Amount) })
      .ToListAsync();

    return new CreditorSummaryDto(
      id,
      creditor.CreditorName,
      creditor.Status,
      await active.CountAsync(),
      await active.SumAsync(t => t.Amount) ?? 0,
      await active.MinAsync(t => t.Date),
      await active.MaxAsync(t => t.Date),
      await claims.CountAsync(),
      await active.CountAsync(t => t.Date == null),
      byYear
        .OrderByDescending(y => y.Year)
        .Select(y => new CreditorYearDto(y.Year, y.Claims, y.Amount ?? 0))
        .ToList());
  }

  // Programs (assistance codes) on this creditor's active claims, for the claims filter.
  [HttpGet("{id:int}/programs")]
  public async Task<ActionResult<List<CreditorProgramDto>>> Programs(int id)
  {
    if (!await context.Creditors.AnyAsync(c => c.CreditorId == id))
    {
      return NotFound(new { message = "Creditor not found." });
    }

    var counts = await context.Transactions.AsNoTracking()
      .Where(t => t.CreditorId == id && (t.Status || t.CreatedBy == null) && t.AssistanceCode != null)
      .GroupBy(t => t.AssistanceCode!)
      .Select(g => new { Code = g.Key, Claims = g.Count() })
      .ToListAsync();
    var programs = await ProgramNamesAsync();

    return counts
      .GroupBy(c => c.Code.Trim().ToUpperInvariant())
      .Select(g => new CreditorProgramDto(g.Key, programs.GetValueOrDefault(g.Key), g.Sum(c => c.Claims)))
      .OrderByDescending(p => p.Claims)
      .ToList();
  }

  // Active claims paid to the creditor. from/to are inclusive dates; search matches the ref no or
  // every word of the patient's name; sort is "date" (default) or "amount", dir "desc" (default) or "asc".
  [HttpGet("{id:int}/claims")]
  public async Task<ActionResult<CreditorClaimsPageDto>> Claims(
    int id,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] DateTime? from = null,
    [FromQuery] DateTime? to = null,
    [FromQuery] string? search = null,
    [FromQuery] string? program = null,
    [FromQuery] string sort = "date",
    [FromQuery] string dir = "desc")
  {
    if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
    {
      return BadRequest(new { message = $"Page must be 1 or more and page size between 1 and {MaxPageSize}." });
    }
    if (from is not null && to is not null && from.Value.Date > to.Value.Date)
    {
      return BadRequest(new { message = "The start date cannot be later than the end date." });
    }
    if (sort is not ("date" or "amount") || dir is not ("asc" or "desc"))
    {
      return BadRequest(new { message = "Sort must be date or amount, and dir asc or desc." });
    }
    if (!await context.Creditors.AnyAsync(c => c.CreditorId == id))
    {
      return NotFound(new { message = "Creditor not found." });
    }

    var query = context.Transactions.AsNoTracking()
      .Where(t => t.CreditorId == id && (t.Status || t.CreatedBy == null));
    if (from is not null)
    {
      query = query.Where(t => t.Date >= from.Value.Date);
    }
    if (to is not null)
    {
      var endExclusive = to.Value.Date.AddDays(1);
      query = query.Where(t => t.Date < endExclusive);
    }
    if (!string.IsNullOrWhiteSpace(program))
    {
      var code = program.Trim();
      query = query.Where(t => t.AssistanceCode != null && t.AssistanceCode.Trim() == code);
    }
    if (!string.IsNullOrWhiteSpace(search))
    {
      // Names and ref nos are stored in capitals; matching in capitals works under any collation.
      var whole = search.Trim().ToUpperInvariant();
      // A ref no match satisfies every word; otherwise each word must be in the patient's name.
      foreach (var word in whole.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        query = query.Where(t =>
          (t.RefNo != null && t.RefNo.Contains(whole))
          || (t.PatientPerson != null
            && ((t.PatientPerson.LastName != null && t.PatientPerson.LastName.Contains(word))
              || (t.PatientPerson.FirstName != null && t.PatientPerson.FirstName.Contains(word))
              || (t.PatientPerson.MiddleName != null && t.PatientPerson.MiddleName.Contains(word)))));
      }
    }

    var total = await query.CountAsync();
    var totalAmount = await query.SumAsync(t => t.Amount) ?? 0;

    var ordered = (sort, dir) switch
    {
      ("amount", "asc") => query.OrderBy(t => t.Amount == null).ThenBy(t => t.Amount),
      ("amount", _) => query.OrderBy(t => t.Amount == null).ThenByDescending(t => t.Amount),
      (_, "asc") => query.OrderBy(t => t.Date == null).ThenBy(t => t.Date),
      _ => query.OrderBy(t => t.Date == null).ThenByDescending(t => t.Date),
    };

    var rows = await ordered
      .ThenByDescending(t => t.TransactionId)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .Select(t => new
      {
        t.TransactionId,
        t.RefNo,
        t.Date,
        t.PatientPersonId,
        Last = t.PatientPerson!.LastName,
        First = t.PatientPerson.FirstName,
        Middle = t.PatientPerson.MiddleName,
        t.AssistanceCode,
        t.Amount,
      })
      .ToListAsync();
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

  // One claim of this creditor, with names instead of IDs.
  [HttpGet("{id:int}/claims/{transactionId:int}")]
  public async Task<ActionResult<CreditorClaimDetailDto>> Claim(int id, int transactionId)
  {
    var t = await context.Transactions.AsNoTracking()
      .Where(x => x.TransactionId == transactionId && x.CreditorId == id)
      .Select(x => new
      {
        x.TransactionId,
        x.RefNo,
        x.Date,
        x.Amount,
        x.AssistanceCode,
        x.IsReleased,
        x.Status,
        x.PatientPersonId,
        Patient = x.PatientPerson == null ? null : new
        {
          x.PatientPerson.LastName,
          x.PatientPerson.FirstName,
          x.PatientPerson.MiddleName,
          Sex = x.PatientPerson.Sex != null ? x.PatientPerson.Sex.SexDesc : x.PatientPerson.Gender,
          x.PatientPerson.Barangay,
          x.PatientPerson.Municipality,
        },
        x.RepresentativePersonId,
        Representative = x.RepresentativePerson == null ? null : new
        {
          x.RepresentativePerson.LastName,
          x.RepresentativePerson.FirstName,
          x.RepresentativePerson.MiddleName,
        },
        RepresentativeText = x.Representative,
        Mode = x.Mode != null ? x.Mode.ModeDesc : null,
        FundSource = x.FundSource != null && x.FundSource.Source != null ? x.FundSource.Source.SourceDesc : null,
        FundYear = x.FundSource != null ? x.FundSource.Year : null,
        SocialWorkerUser = x.SocialWorkerUser == null ? null : new
        {
          x.SocialWorkerUser.Lastname,
          x.SocialWorkerUser.Firstname,
          x.SocialWorkerUser.Middlename,
        },
        x.SocialWorkerId,
        x.SocialWorkerAssessment,
        x.Remarks,
        x.CreatedBy,
        x.DateCreated,
        x.LastUpdatedBy,
        x.DateLastUpdated,
        Natures = x.TransactionServices
          .Where(s => s.Nature != null)
          .Select(s => s.Nature!.NatureDesc)
          .ToList(),
      })
      .FirstOrDefaultAsync();
    if (t is null)
    {
      return NotFound(new { message = "Claim not found for this creditor." });
    }

    string? socialWorker = null;
    string? socialWorkerPosition = null;
    if (t.SocialWorkerUser is not null)
    {
      socialWorker = PersonName(t.SocialWorkerUser.Lastname, t.SocialWorkerUser.Firstname, t.SocialWorkerUser.Middlename);
    }
    else if (int.TryParse(t.SocialWorkerId?.Trim(), out var officerId))
    {
      // Officers also stores passwords: read the name and position only.
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
      // The old system never recorded releases (always false), so for its claims it is unknown.
      t.CreatedBy is null ? null : t.IsReleased,
      t.Status || t.CreatedBy is null,
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
      t.Mode,
      t.FundSource is null ? null : JoinNonEmpty(" ", t.FundSource, t.FundYear?.ToString()),
      socialWorker,
      socialWorkerPosition,
      t.SocialWorkerAssessment,
      t.Remarks,
      UserName(t.CreatedBy),
      t.DateCreated,
      UserName(t.LastUpdatedBy),
      t.DateLastUpdated);
  }

  // The Assistance table has no key to join on, only the code; it is a handful of rows.
  private async Task<Dictionary<string, string?>> ProgramNamesAsync() =>
    (await context.Assistances.AsNoTracking()
      .Where(a => a.AssistanceCode != null)
      .Select(a => new { a.AssistanceCode, a.AssistanceType })
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
