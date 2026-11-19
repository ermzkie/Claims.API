using System.Text.Json;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.FundSources;
using Claims.API.Models;
using Claims.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

// getAnnualBalance reads these rows for the dashboard budget
[Route("api/fund-sources")]
[ApiController]
[Authorize(Policy = AuthPolicies.UserAdmin)]
public class FundSourcesController(
  ApplicationDbContext context, IFinanceAccounts finance, ICurrentUser currentUser, TimeProvider time,
  ILogger<FundSourcesController> logger) : ControllerBase
{
  private const string Entity = "FundSource";
  private const string Appropriation = "appropriation";
  private const string TrustFund = "trustFund";
  private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

  [HttpGet]
  public async Task<ActionResult<FundSourceListDto>> List([FromQuery] int? year = null)
  {
    var y = year ?? Now().Year;
    if (!ValidYear(y))
    {
      return BadRequest(new { message = YearMessage });
    }

    var years = (await context.FundSources.AsNoTracking().Where(f => f.Year != null).Select(f => (int)f.Year!.Value).Distinct().ToListAsync())
      .Append(Now().Year)
      .Append(y)
      .Distinct()
      .OrderDescending()
      .ToList();

    var (items, financeAvailable) = await BuildAsync(context.FundSources.Where(f => f.Year == y), y);
    return new FundSourceListDto(y, years, financeAvailable, items);
  }

  [HttpGet("{id:int}/claims")]
  public async Task<ActionResult<IReadOnlyList<FundSourceClaimDto>>> Claims(int id, [FromQuery] int take = 20)
  {
    if (take is < 1 or > 100)
    {
      return BadRequest(new { message = "Take 1 to 100 claims." });
    }
    if (!await context.FundSources.AnyAsync(f => f.FundSourceId == id))
    {
      return NotFound(new { message = "Fund source not found." });
    }

    var rows = await context.Transactions.AsNoTracking()
      .Where(t => t.FundSourceId == id && (t.Status || (t.CreatedBy == null && t.LastUpdatedBy == null)))
      .OrderByDescending(t => t.Date).ThenByDescending(t => t.TransactionId)
      .Take(take)
      .Select(t => new
      {
        t.TransactionId,
        t.RefNo,
        t.Date,
        t.PatientPersonId,
        t.PatientPerson!.LastName,
        t.PatientPerson.FirstName,
        t.PatientPerson.MiddleName,
        t.AssistanceCode,
        t.CreditorId,
        t.Creditor!.CreditorName,
        t.Amount,
        t.IsReleased,
      })
      .ToListAsync();

    var programs = (await context.Assistances.AsNoTracking()
        .Where(a => a.AssistanceCode != null)
        .Select(a => new { a.AssistanceCode, a.AssistanceType })
        .ToListAsync())
      .GroupBy(a => a.AssistanceCode!.Trim().ToUpperInvariant())
      .ToDictionary(g => g.Key, g => g.First().AssistanceType);

    return rows.Select(r => new FundSourceClaimDto(
        r.TransactionId,
        r.RefNo?.Trim(),
        r.Date,
        r.PatientPersonId,
        string.Join(" ", new[] { r.LastName is null ? null : $"{r.LastName.Trim()},", r.FirstName?.Trim(), r.MiddleName?.Trim() }
          .Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name.TrimEnd(',') : null,
        r.AssistanceCode?.Trim(),
        r.AssistanceCode is null ? null : programs.GetValueOrDefault(r.AssistanceCode.Trim().ToUpperInvariant()),
        r.CreditorId,
        r.CreditorName?.Trim(),
        r.Amount,
        r.IsReleased))
      .ToList();
  }

  // aid-paying accounts only, no way to list the rest on purpose
  [HttpGet("finance-accounts")]
  public async Task<ActionResult<IReadOnlyList<FinanceAccountDto>>> FinanceAccounts(
    [FromQuery] string? kind = null, [FromQuery] int? year = null)
  {
    var y = year ?? Now().Year;
    if (!ValidYear(y))
    {
      return BadRequest(new { message = YearMessage });
    }
    if (kind is not (Appropriation or TrustFund))
    {
      return BadRequest(new { message = $"Kind must be {Appropriation} or {TrustFund}." });
    }

    try
    {
      var accounts = kind == Appropriation ? await finance.AppropriationsAsync(y) : await finance.TrustFundsAsync();
      return Ok(accounts.Where(a => a.PaysAid).ToList());
    }
    catch (FinanceUnavailableException ex)
    {
      logger.LogWarning(ex, "Finance accounts could not be read");
      return FinanceDown();
    }
  }

  [HttpPost]
  public async Task<ActionResult<FundSourceDto>> Create(SaveFundSourceDto dto)
  {
    var fund = new FundSource { Status = true, CreatedBy = currentUser.Id, DateCreated = Now() };
    var (error, account) = await ApplyAsync(fund, dto);
    if (error is not null)
    {
      return error;
    }

    context.FundSources.Add(fund);
    await context.SaveChangesAsync();
    Log(fund.FundSourceId, "Added", null, Values(fund));
    await context.SaveChangesAsync();
    return CreatedAtAction(nameof(List), new { year = fund.Year }, await DtoAsync(fund));
  }

  [HttpPut("{id:int}")]
  public async Task<ActionResult<FundSourceDto>> Update(int id, SaveFundSourceDto dto)
  {
    var fund = await context.FundSources.FirstOrDefaultAsync(f => f.FundSourceId == id);
    if (fund is null)
    {
      return NotFound(new { message = "Fund source not found." });
    }

    var before = Values(fund);
    // claims already use it, so year and program stay
    if ((dto.Year != fund.Year || dto.SourceId != fund.SourceId) && await ClaimsAsync(id) is > 0 and var used)
    {
      return BadRequest(new { message = $"{used:N0} claims use this fund source, so its year and program can't change. Add a new one instead." });
    }

    var (error, account) = await ApplyAsync(fund, dto);
    if (error is not null)
    {
      return error;
    }

    var after = Values(fund);
    if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
    {
      fund.LastUpdatedBy = currentUser.Id;
      fund.DateLastUpdated = Now();
      Log(id, "Changed", before, after);
      await context.SaveChangesAsync();
    }
    return await DtoAsync(fund);
  }

  // getAnnualBalance ignores Status, so remove instead of deactivate
  [HttpDelete("{id:int}")]
  public async Task<IActionResult> Delete(int id)
  {
    var fund = await context.FundSources.FirstOrDefaultAsync(f => f.FundSourceId == id);
    if (fund is null)
    {
      return NotFound(new { message = "Fund source not found." });
    }
    if (await ClaimsAsync(id) is > 0 and var used)
    {
      return BadRequest(new { message = $"{used:N0} claims use this fund source, so it can't be removed." });
    }

    Log(id, "Removed", Values(fund), null);
    context.FundSources.Remove(fund);
    await context.SaveChangesAsync();
    return NoContent();
  }

  // deleted claims still point at the row
  private Task<int> ClaimsAsync(int id) => context.Transactions.CountAsync(t => t.FundSourceId == id);

  private async Task<(ActionResult? Error, FinanceAccountDto? Account)> ApplyAsync(FundSource fund, SaveFundSourceDto dto)
  {
    if (dto.Year is not { } year || !ValidYear(year))
    {
      return (BadRequest(new { message = YearMessage }), null);
    }
    if (dto.SourceId is not { } sourceId || !await context.SourceOfAssistances.AnyAsync(s => s.SourceId == sourceId))
    {
      return (BadRequest(new { message = "Pick the program." }), null);
    }
    if ((dto.AppropriationId is null) == (dto.TrustFundId is null))
    {
      return (BadRequest(new { message = "Pick either an appropriation or a trust fund." }), null);
    }

    var kind = dto.AppropriationId is not null ? Appropriation : TrustFund;
    var financeId = (dto.AppropriationId ?? dto.TrustFundId)!.Value;
    var taken = await context.FundSources.AsNoTracking()
      .Where(f => f.FundSourceId != fund.FundSourceId && f.Year == year
        && (kind == Appropriation ? f.AppropriationId == financeId : f.TrustFundId == financeId))
      .Select(f => f.Source!.SourceDesc)
      .FirstOrDefaultAsync();
    if (taken is not null)
    {
      return (BadRequest(new { message = $"That account is already set up for {taken?.Trim()} in {year}." }), null);
    }

    FinanceAccountDto? account;
    try
    {
      var accounts = kind == Appropriation ? await finance.AppropriationsAsync(year) : await finance.TrustFundsAsync();
      account = accounts.FirstOrDefault(a => a.Id == financeId);
    }
    catch (FinanceUnavailableException ex)
    {
      logger.LogWarning(ex, "Finance accounts could not be read");
      return (FinanceDown(), null);
    }
    if (account is null)
    {
      return (BadRequest(new
      {
        message = kind == Appropriation
          ? $"Appropriation {financeId} isn't one of the office's {year} appropriations in the finance system."
          : $"Trust fund {financeId} isn't one of the office's active trust funds in the finance system.",
      }), null);
    }
    // don't rely on the picker alone
    if (!account.PaysAid)
    {
      return (BadRequest(new
      {
        message = kind == Appropriation
          ? $"{account.Name ?? $"Appropriation {financeId}"} (#{financeId}) hasn't paid any medical, hospitalization, burial or financial assistance in {year}, or anything to a Claims creditor, so claims can't be charged to it. Check with finance which account pays the program."
          : $"{account.Name ?? $"Trust fund {financeId}"} (#{financeId}) has never paid medical, hospitalization, burial or financial assistance, or anything to a Claims creditor, so claims can't be charged to it.",
      }), null);
    }

    fund.Year = (short)year;
    fund.SourceId = sourceId;
    fund.AppropriationId = dto.AppropriationId;
    fund.TrustFundId = dto.TrustFundId;
    return (null, account);
  }

  private async Task<FundSourceDto> DtoAsync(FundSource fund) =>
    (await BuildAsync(context.FundSources.Where(f => f.FundSourceId == fund.FundSourceId), fund.Year ?? 0)).Items.Single();

  private async Task<(List<FundSourceDto> Items, bool FinanceAvailable)> BuildAsync(IQueryable<FundSource> query, int year)
  {
    var rows = await query.AsNoTracking()
      .Select(f => new
      {
        f.FundSourceId,
        f.Year,
        f.SourceId,
        Program = f.Source!.SourceDesc,
        f.AppropriationId,
        f.TrustFundId,
        Claims = f.Transactions.Count(t => t.Status || (t.CreatedBy == null && t.LastUpdatedBy == null)),
        ClaimsAmount = f.Transactions.Where(t => t.Status || (t.CreatedBy == null && t.LastUpdatedBy == null)).Sum(t => t.Amount) ?? 0,
        f.DateCreated,
        f.CreatedBy,
        f.DateLastUpdated,
        f.LastUpdatedBy,
      })
      .ToListAsync();

    var userIds = rows.SelectMany(r => new[] { r.CreatedBy, r.LastUpdatedBy }).OfType<int>().Distinct().ToList();
    var users = await context.Users.AsNoTracking()
      .Where(u => userIds.Contains(u.UserId))
      .Select(u => new { u.UserId, u.Lastname, u.Firstname, u.Username })
      .ToDictionaryAsync(u => u.UserId, u => u.Lastname is null && u.Firstname is null ? u.Username : $"{u.Lastname}, {u.Firstname}".Trim(' ', ','));

    var appropriationIds = rows.Select(r => r.AppropriationId).OfType<int>().Distinct().ToList();
    var trustFundIds = rows.Where(r => r.AppropriationId is null).Select(r => r.TrustFundId).OfType<int>().Distinct().ToList();
    Dictionary<int, FinanceAccountDto> appropriations = [], trustFunds = [];
    Dictionary<int, FinanceUsageDto> appropriationUse = [], trustFundUse = [];
    var financeAvailable = true;
    try
    {
      if (appropriationIds.Count > 0)
      {
        appropriations = (await finance.AppropriationsAsync(year)).ToDictionary(a => a.Id);
        appropriationUse = (await finance.AppropriationUsageAsync(appropriationIds)).ToDictionary(a => a.Id);
      }
      if (trustFundIds.Count > 0)
      {
        trustFunds = (await finance.TrustFundsAsync()).ToDictionary(a => a.Id);
        trustFundUse = (await finance.TrustFundUsageAsync(trustFundIds)).ToDictionary(a => a.Id);
      }
    }
    catch (FinanceUnavailableException ex)
    {
      // list still loads without finance
      logger.LogWarning(ex, "Finance accounts could not be read for {Year}", year);
      financeAvailable = false;
    }

    var items = rows
      .Select(r =>
      {
        var isAppropriation = r.AppropriationId is not null;
        var financeId = r.AppropriationId ?? r.TrustFundId;
        var account = financeId is { } id ? (isAppropriation ? appropriations : trustFunds).GetValueOrDefault(id) : null;
        var use = financeId is { } uid ? (isAppropriation ? appropriationUse : trustFundUse).GetValueOrDefault(uid) : null;
        return new FundSourceDto(
          r.FundSourceId, r.Year ?? 0, r.SourceId, r.Program?.Trim(), isAppropriation ? Appropriation : TrustFund, financeId,
          account, use?.Released, use?.Obligated, use?.Pending, use?.Original, use?.Supplemental, use?.Adjustments,
          r.Claims, r.ClaimsAmount,
          r.DateCreated, r.CreatedBy is { } c ? users.GetValueOrDefault(c) : null,
          r.DateLastUpdated, r.LastUpdatedBy is { } u ? users.GetValueOrDefault(u) : null);
      })
      .OrderBy(r => r.Program)
      .ThenBy(r => r.FundSourceId)
      .ToList();
    return (items, financeAvailable);
  }

  private static object Values(FundSource f) => new { f.Year, f.SourceId, f.AppropriationId, f.TrustFundId };

  private void Log(int id, string action, object? oldValues, object? newValues) =>
    context.AuditLogs.Add(new AuditLog
    {
      Entity = Entity,
      EntityId = id,
      Action = action,
      OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, LogJson),
      NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, LogJson),
      UserId = currentUser.Id,
      DateCreated = Now(),
    });

  private const string YearMessage = "Give a year from 2000 to next year.";

  private bool ValidYear(int year) => year >= 2000 && year <= Now().Year + 1;

  private ObjectResult FinanceDown() =>
    StatusCode(StatusCodes.Status503ServiceUnavailable,
      new { message = "The finance system can't be reached right now, so accounts can't be checked. Try again later." });

  private DateTime Now() => time.GetLocalNow().DateTime;
}
