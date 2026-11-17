using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Dashboard;
using Claims.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/dashboard")]
[ApiController]
public class DashboardController(ApplicationDbContext context, TimeProvider time, ILogger<DashboardController> logger)
  : ControllerBase
{
  private const int TrendMonths = 12;
  private const int TopCreditors = 5;
  private const int WaitingShown = 8;
  private const string NotRecorded = "Not recorded";

  [HttpGet]
  public async Task<ActionResult<DashboardDto>> Get([FromQuery] int? year = null, [FromQuery] int? month = null)
  {
    if (!TryMonth(year, month, out var start))
    {
      return BadRequest(new { message = "Give a valid year and month." });
    }
    var end = start.AddMonths(1);
    var previous = start.AddMonths(-1);

    var claims = CountedClaims();
    var inMonth = claims.Where(t => t.Date >= start && t.Date < end);
    var inPrevious = claims.Where(t => t.Date >= previous && t.Date < start);
    var inYear = claims.Where(t => t.Date >= new DateTime(start.Year, 1, 1) && t.Date < end);
    // old-system claims have no release status, so only claims made here can be waiting
    var waiting = context.Transactions.AsNoTracking()
      .Where(t => t.Status && t.CreatedBy != null && t.IsReleased != true);

    var programs = await ProgramNamesAsync();

    var byProgram = (await inMonth
        .Where(t => t.AssistanceCode != null)
        .GroupBy(t => t.AssistanceCode!)
        .Select(g => new { Code = g.Key, Claims = g.Count(), Amount = g.Sum(t => t.Amount) })
        .ToListAsync())
      .GroupBy(p => p.Code.Trim().ToUpperInvariant())
      .Select(g => new DashboardProgramDto(
        g.Key, programs.GetValueOrDefault(g.Key), g.Sum(p => p.Claims), g.Sum(p => p.Amount ?? 0)))
      .OrderByDescending(p => p.Amount)
      .ToList();

    var topCreditors = (await inMonth
        .Where(t => t.CreditorId != null)
        .GroupBy(t => t.CreditorId!.Value)
        .Select(g => new { CreditorId = g.Key, Claims = g.Count(), Amount = g.Sum(t => t.Amount) })
        .ToListAsync())
      .OrderByDescending(c => c.Amount ?? 0)
      .Take(TopCreditors)
      .ToList();
    var creditorIds = topCreditors.Select(c => c.CreditorId).ToList();
    var creditorNames = await context.Creditors.AsNoTracking()
      .Where(c => creditorIds.Contains(c.CreditorId))
      .ToDictionaryAsync(c => c.CreditorId, c => c.CreditorName);

    var trendStart = start.AddMonths(1 - TrendMonths);
    var trendRows = await claims
      .Where(t => t.Date >= trendStart && t.Date < end)
      .GroupBy(t => new { t.Date!.Value.Year, t.Date.Value.Month })
      .Select(g => new { g.Key.Year, g.Key.Month, Claims = g.Count(), Amount = g.Sum(t => t.Amount) })
      .ToListAsync();
    var trend = Enumerable.Range(0, TrendMonths)
      .Select(i => trendStart.AddMonths(i))
      .Select(d =>
      {
        var row = trendRows.FirstOrDefault(r => r.Year == d.Year && r.Month == d.Month);
        return new DashboardMonthDto(d.Year, d.Month, row?.Claims ?? 0, row?.Amount ?? 0);
      })
      .ToList();

    var oldestWaiting = (await waiting
        .OrderBy(t => t.Date == null).ThenBy(t => t.Date).ThenBy(t => t.TransactionId)
        .Take(WaitingShown)
        .Select(t => new
        {
          t.TransactionId,
          t.RefNo,
          t.Date,
          t.PatientPersonId,
          Last = t.PatientPerson!.LastName,
          First = t.PatientPerson.FirstName,
          t.AssistanceCode,
          t.CreditorId,
          CreditorName = t.Creditor!.CreditorName,
          t.Amount,
        })
        .ToListAsync())
      .Select(t => new DashboardClaimDto(
        t.TransactionId,
        t.RefNo,
        t.Date,
        t.PatientPersonId,
        string.Join(", ", new[] { t.Last, t.First }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : null,
        t.AssistanceCode,
        t.AssistanceCode is null ? null : programs.GetValueOrDefault(t.AssistanceCode.Trim().ToUpperInvariant()),
        t.CreditorId,
        t.CreditorName,
        t.Amount))
      .ToList();

    return new DashboardDto(
      start.Year,
      start.Month,
      await inMonth.CountAsync(),
      await inMonth.SumAsync(t => t.Amount) ?? 0,
      await inPrevious.CountAsync(),
      await inPrevious.SumAsync(t => t.Amount) ?? 0,
      await inYear.CountAsync(),
      await inYear.SumAsync(t => t.Amount) ?? 0,
      await context.People.CountAsync(p => p.Status && p.DateCreated >= start && p.DateCreated < end),
      await waiting.CountAsync(),
      await waiting.SumAsync(t => t.Amount) ?? 0,
      byProgram,
      topCreditors.Select(c => new DashboardCreditorDto(
        c.CreditorId, creditorNames.GetValueOrDefault(c.CreditorId), c.Claims, c.Amount ?? 0)).ToList(),
      trend,
      oldestWaiting);
  }

  [HttpGet("beneficiaries")]
  public async Task<ActionResult<DashboardBeneficiariesDto>> Beneficiaries(
    [FromQuery] int? year = null, [FromQuery] int? month = null, [FromQuery] string? period = null)
  {
    period = string.IsNullOrWhiteSpace(period) ? "month" : period.Trim().ToLowerInvariant();
    if (!TryMonth(year, month, out var monthStart) || period is not ("month" or "year"))
    {
      return BadRequest(new { message = "Give a valid year, month and period (month or year)." });
    }
    var end = monthStart.AddMonths(1);
    var start = period == "year" ? new DateTime(monthStart.Year, 1, 1) : monthStart;

    var claims = CountedClaims();
    var inPeriod = claims.Where(t => t.Date >= start && t.Date < end);
    var patientIds = inPeriod.Where(t => t.PatientPersonId != null).Select(t => t.PatientPersonId!.Value);

    var perPerson = (await inPeriod
        .Where(t => t.PatientPersonId != null)
        .GroupBy(t => t.PatientPersonId!.Value)
        .Select(g => new { PersonId = g.Key, Claims = g.Count(), Amount = g.Sum(t => t.Amount) })
        .ToListAsync())
      .ToDictionary(r => r.PersonId);

    var people = await context.People.AsNoTracking()
      .Where(p => patientIds.Contains(p.PersonId))
      .Select(p => new
      {
        p.PersonId,
        Sex = p.Sex!.SexDesc,
        Town = p.BarangayNavigation!.CityMunicipality!.CityMunicipalityName,
        OldTown = p.Municipality,
        p.Birthdate,
        p.IsSeniorCitizen,
        p.IsPwd,
        p.IsSoloParent,
        p.Is4P,
      })
      .ToListAsync();

    // anyone with a counted claim before the period has been helped before
    var helpedBefore = (await claims
        .Where(t => t.Date < start && t.PatientPersonId != null && patientIds.Contains(t.PatientPersonId!.Value))
        .Select(t => t.PatientPersonId!.Value)
        .Distinct()
        .ToListAsync())
      .ToHashSet();

    var lastDay = DateOnly.FromDateTime(end.AddDays(-1));
    var rows = people
      .Select(p =>
      {
        var c = perPerson[p.PersonId];
        return new
        {
          p,
          c.Claims,
          Amount = c.Amount ?? 0,
          Town = Clean(p.Town) ?? Clean(p.OldTown) ?? NotRecorded,
          Sex = string.IsNullOrWhiteSpace(p.Sex) ? NotRecorded : p.Sex.Trim(),
          Age = AgeGroup(p.Birthdate, lastDay),
        };
      })
      .ToList();

    static List<DashboardGroupDto> Groups<T>(IEnumerable<T> items, Func<T, string> key, Func<T, int> claimsOf, Func<T, decimal> amountOf) =>
      items.GroupBy(key)
        .Select(g => new DashboardGroupDto(g.Key, g.Count(), g.Sum(claimsOf), g.Sum(amountOf)))
        .OrderBy(g => g.Name == NotRecorded)
        .ThenByDescending(g => g.Persons)
        .ThenBy(g => g.Name)
        .ToList();

    // a person can be in more than one sector, so these don't add up to the total
    var sectors = new[]
      {
        ("Senior citizen", rows.Where(r => r.p.IsSeniorCitizen == true)),
        ("PWD", rows.Where(r => r.p.IsPwd == true)),
        ("Solo parent", rows.Where(r => r.p.IsSoloParent == true)),
        ("4Ps", rows.Where(r => r.p.Is4P == true)),
      }
      .Select(s => new DashboardGroupDto(s.Item1, s.Item2.Count(), s.Item2.Sum(r => r.Claims), s.Item2.Sum(r => r.Amount)))
      .ToList();

    var ageOrder = AgeGroups.Append(NotRecorded).ToList();

    return new DashboardBeneficiariesDto(
      monthStart.Year,
      monthStart.Month,
      period,
      await inPeriod.CountAsync(),
      rows.Count,
      await inPeriod.CountAsync(t => t.PatientPersonId == null),
      rows.Count(r => !helpedBefore.Contains(r.p.PersonId)),
      rows.Count(r => helpedBefore.Contains(r.p.PersonId)),
      Groups(rows, r => r.Town, r => r.Claims, r => r.Amount),
      Groups(rows, r => r.Sex, r => r.Claims, r => r.Amount),
      Groups(rows, r => r.Age, r => r.Claims, r => r.Amount).OrderBy(a => ageOrder.IndexOf(a.Name)).ToList(),
      sectors);
  }

  [HttpGet("budget")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<ActionResult<IReadOnlyList<DashboardBudgetDto>>> Budget([FromQuery] int? year = null)
  {
    var y = year ?? time.GetLocalNow().Year;
    if (y < 1900 || y > 2100)
    {
      return BadRequest(new { message = "Give a valid year." });
    }

    try
    {
      // linked server, don't let it hang the page
      context.Database.SetCommandTimeout(TimeSpan.FromSeconds(20));
      var rows = await context.AnnualBalanceResults
        .FromSql($"EXEC getAnnualBalance @year = {y}")
        .AsNoTracking()
        .ToListAsync();
      return rows
        .Select(r => new DashboardBudgetDto(r.SourceDesc?.Trim(), r.Year, r.Release ?? 0, r.Obligated ?? 0))
        .OrderByDescending(r => r.Released)
        .ToList();
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
    {
      logger.LogWarning(ex, "Budget figures could not be read for {Year}", y);
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { message = "Budget figures from the finance system are not available right now." });
    }
  }

  private static readonly string[] AgeGroups = ["0-17", "18-29", "30-44", "45-59", "60 and over"];

  private static string AgeGroup(DateOnly? birthdate, DateOnly on)
  {
    if (birthdate is not { } b || b > on || b.Year < 1900)
    {
      return NotRecorded;
    }
    var age = on.Year - b.Year - (on < b.AddYears(on.Year - b.Year) ? 1 : 0);
    return age switch
    {
      < 18 => AgeGroups[0],
      < 30 => AgeGroups[1],
      < 45 => AgeGroups[2],
      < 60 => AgeGroups[3],
      _ => AgeGroups[4],
    };
  }

  private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

  private bool TryMonth(int? year, int? month, out DateTime start)
  {
    var now = time.GetLocalNow().DateTime;
    var y = year ?? now.Year;
    var m = month ?? now.Month;
    start = default;
    if (y < 1900 || y > 2100 || m < 1 || m > 12)
    {
      return false;
    }
    start = new DateTime(y, m, 1);
    return true;
  }

  // old-system claims came over with status false; only claims made here can be deleted
  private IQueryable<Transaction> CountedClaims() =>
    context.Transactions.AsNoTracking().Where(t => t.Status || t.CreatedBy == null);

  // Assistance has a row per budget release
  private async Task<Dictionary<string, string?>> ProgramNamesAsync() =>
    (await context.Assistances.AsNoTracking()
      .Where(a => a.AssistanceCode != null)
      .Select(a => new { a.AssistanceCode, a.AssistanceType })
      .ToListAsync())
    .GroupBy(a => a.AssistanceCode!.Trim().ToUpperInvariant())
    .ToDictionary(g => g.Key, g => g.First().AssistanceType);
}
