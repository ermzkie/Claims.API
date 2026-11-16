using Claims.API.Data;
using Claims.API.DTOs.Dashboard;
using Claims.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

// read-only figures for the home page, for one month (default: this month)
[Route("api/dashboard")]
[ApiController]
public class DashboardController(ApplicationDbContext context, TimeProvider time) : ControllerBase
{
  private const int TrendMonths = 12;
  private const int TopCreditors = 5;
  private const int RecentClaims = 8;

  [HttpGet]
  public async Task<ActionResult<DashboardDto>> Get([FromQuery] int? year = null, [FromQuery] int? month = null)
  {
    var now = time.GetLocalNow().DateTime;
    var y = year ?? now.Year;
    var m = month ?? now.Month;
    if (y < 1900 || y > 2100 || m < 1 || m > 12)
    {
      return BadRequest(new { message = "Give a valid year and month." });
    }

    var start = new DateTime(y, m, 1);
    var end = start.AddMonths(1);
    var previous = start.AddMonths(-1);

    // old-system claims came over with status false; only claims made here can be deleted
    var claims = context.Transactions.AsNoTracking().Where(t => t.Status || t.CreatedBy == null);
    var inMonth = claims.Where(t => t.Date >= start && t.Date < end);
    var inPrevious = claims.Where(t => t.Date >= previous && t.Date < start);
    var inYear = claims.Where(t => t.Date >= new DateTime(y, 1, 1) && t.Date < end);

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

    var recent = (await inMonth
        .OrderByDescending(t => t.Date).ThenByDescending(t => t.TransactionId)
        .Take(RecentClaims)
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
      y,
      m,
      await inMonth.CountAsync(),
      await inMonth.SumAsync(t => t.Amount) ?? 0,
      await inPrevious.CountAsync(),
      await inPrevious.SumAsync(t => t.Amount) ?? 0,
      await inYear.CountAsync(),
      await inYear.SumAsync(t => t.Amount) ?? 0,
      await context.People.CountAsync(p => p.Status && p.DateCreated >= start && p.DateCreated < end),
      byProgram,
      topCreditors.Select(c => new DashboardCreditorDto(
        c.CreditorId, creditorNames.GetValueOrDefault(c.CreditorId), c.Claims, c.Amount ?? 0)).ToList(),
      trend,
      recent);
  }

  // the Assistance table has one row per budget period; one name per code
  private async Task<Dictionary<string, string?>> ProgramNamesAsync() =>
    (await context.Assistances.AsNoTracking()
      .Where(a => a.AssistanceCode != null)
      .Select(a => new { a.AssistanceCode, a.AssistanceType })
      .ToListAsync())
    .GroupBy(a => a.AssistanceCode!.Trim().ToUpperInvariant())
    .ToDictionary(g => g.Key, g => g.First().AssistanceType);
}
