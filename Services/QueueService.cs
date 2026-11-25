using Claims.API.Data;
using Claims.API.DTOs.Queue;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public enum QueueActionOutcome { Ok, NotFound, Forbidden, NotServing, Closed, InApproval }

// the queue is worked out from today's claims and their change log, no extra columns
public class QueueService(ApplicationDbContext db, TimeProvider time)
{
  public const string Entity = "Transaction";
  public const string CheckedOut = "CheckedOut";
  public const string TakenOver = "TakenOver";
  public const string ReturnedToQueue = "ReturnedToQueue";
  public const string NotServed = "NotServed";
  public const string Recalled = "Recalled";
  private static readonly string[] QueueActions = [CheckedOut, TakenOver, ReturnedToQueue, NotServed];

  // seniors/PWD first, then VIP, then regular
  private static readonly string[] LaneOrder = ["S", "V", "R"];

  // office time with no zone, like the dates stored on claims
  public DateTime Now() => DateTime.SpecifyKind(time.GetUtcNow().UtcDateTime.AddHours(8), DateTimeKind.Unspecified);

  // InApproval: sent or returned, the worker is free to call the next client
  private enum State { Waiting, Serving, NotServed, Done, InApproval }

  private sealed record Log(int TransactionId, string Action, string? Remark, string? By, DateTime At, int Id);

  private sealed record Row(Transaction Claim, State State, Log? Latest, DateTime? Since, DateTime? ReturnedAt)
  {
    public string Code => $"{Claim.LaneType?.LaneTypeCode}{Claim.QueueNo}";
  }

  private static int LaneRank(LaneType? lane)
  {
    var i = Array.IndexOf(LaneOrder, lane?.LaneTypeCode?.Trim().ToUpperInvariant());
    return i < 0 ? LaneOrder.Length : i;
  }

  private async Task<List<Row>> TodayRowsAsync(CancellationToken ct)
  {
    var day = Now().Date;
    var nextDay = day.AddDays(1);
    var claims = await db.Transactions.AsNoTracking()
      .Where(ClaimRules.Counted)
      .Where(t => t.Date >= day && t.Date < nextDay && t.LaneTypeId != null)
      .Include(t => t.LaneType)
      .Include(t => t.PatientPerson)
      .Include(t => t.SocialWorkerUser)
      .Include(t => t.TransactionServices).ThenInclude(s => s.Nature)
      .ToListAsync(ct);

    var ids = claims.Select(c => c.TransactionId).ToList();
    var logs = (await db.AuditLogs.AsNoTracking()
        .Where(a => a.Entity == Entity && ids.Contains(a.EntityId) && QueueActions.Contains(a.Action))
        .Select(a => new { a.EntityId, a.Action, a.Remark, a.DateCreated, a.AuditLogId, a.User.Firstname, a.User.Lastname, a.User.Username })
        .ToListAsync(ct))
      .Select(a => new Log(a.EntityId, a.Action, a.Remark,
        a.Firstname is null && a.Lastname is null ? a.Username : $"{a.Firstname} {a.Lastname}".Trim(),
        a.DateCreated, a.AuditLogId))
      .ToLookup(l => l.TransactionId);

    return claims.Select(c =>
    {
      var mine = logs[c.TransactionId].OrderBy(l => l.At).ThenBy(l => l.Id).ToList();
      var latest = mine.LastOrDefault();
      var since = mine.LastOrDefault(l => l.Action is CheckedOut or TakenOver)?.At ?? c.DateLastUpdated;
      var state = c.IsReleased == true || c.ApprovalStatusId == ApprovalStatuses.Approved ? State.Done
        : c.ApprovalStatusId is ApprovalStatuses.Waiting or ApprovalStatuses.Returned ? State.InApproval
        : c.SocialWorkerUserId is not null ? State.Serving
        : latest?.Action == NotServed ? State.NotServed
        : State.Waiting;
      var returnedAt = latest?.Action == ReturnedToQueue ? latest.At : (DateTime?)null;
      return new Row(c, state, latest, since, returnedAt);
    }).ToList();
  }

  // a returned client lines up again from the moment they came back: behind those waiting then, ahead of later arrivals
  private static IEnumerable<Row> InServingOrder(IEnumerable<Row> waiting) => waiting
    .OrderBy(r => LaneRank(r.Claim.LaneType))
    .ThenBy(r => r.Claim.LaneTypeId)
    .ThenBy(r => r.ReturnedAt ?? r.Claim.DateCreated ?? r.Claim.Date)
    .ThenBy(r => r.Claim.QueueNo)
    .ThenBy(r => r.Claim.TransactionId);

  public async Task<List<int>> WaitingOrderAsync(CancellationToken ct) =>
    InServingOrder((await TodayRowsAsync(ct)).Where(r => r.State == State.Waiting))
      .Select(r => r.Claim.TransactionId)
      .ToList();

  public async Task<QueueTodayDto> TodayAsync(CancellationToken ct)
  {
    var rows = await TodayRowsAsync(ct);
    var lanes = await db.LaneTypes.AsNoTracking().ToListAsync(ct);
    var waiting = InServingOrder(rows.Where(r => r.State == State.Waiting)).ToList();

    var laneDtos = lanes
      .OrderBy(LaneRank).ThenBy(l => l.LaneTypeId)
      .Select(l => new QueueLaneDto(
        l.LaneTypeId,
        l.LaneTypeCode?.Trim() ?? "",
        l.LaneTypeDesc?.Trim() ?? "",
        waiting.Where(r => r.Claim.LaneTypeId == l.LaneTypeId).Select(Item).ToList(),
        rows.Where(r => r.State == State.Serving && r.Claim.LaneTypeId == l.LaneTypeId)
          .OrderBy(r => r.Since).Select(Item).ToList(),
        rows.Count(r => r.State == State.Done && r.Claim.LaneTypeId == l.LaneTypeId)))
      .ToList();

    var notServed = rows.Where(r => r.State == State.NotServed)
      .OrderBy(r => r.Latest!.At).Select(Item).ToList();

    return new QueueTodayDto(
      DateOnly.FromDateTime(Now()),
      Now(),
      waiting.FirstOrDefault()?.Code,
      laneDtos,
      notServed,
      waiting.Count,
      rows.Count(r => r.State == State.Serving),
      rows.Count(r => r.State == State.Done));
  }

  /// <summary>Today's open (not released) claim the worker is serving, if any.</summary>
  public async Task<(int Id, string Code)?> OpenClaimOfAsync(int userId, CancellationToken ct)
  {
    var row = (await TodayRowsAsync(ct))
      .FirstOrDefault(r => r.State == State.Serving && r.Claim.SocialWorkerUserId == userId);
    return row is null ? null : (row.Claim.TransactionId, row.Code);
  }

  /// <summary>Gives the next waiting claim to the worker; null when nobody is waiting.</summary>
  public async Task<int?> TakeNextAsync(int userId, int? deskId, CancellationToken ct) =>
    await TakeFirstFreeAsync(userId, await WaitingOrderAsync(ct), ct, deskId);

  public Task<bool> DeskInUseAsync(int deskId, CancellationToken ct) =>
    db.Desks.AnyAsync(d => d.DeskId == deskId && d.Status, ct);

  // the list can be out of date by the time we get here; the update below is what decides
  public async Task<int?> TakeFirstFreeAsync(int userId, IReadOnlyList<int> order, CancellationToken ct, int? deskId = null)
  {
    foreach (var id in order)
    {
      var now = Now();
      // only wins if nobody grabbed it in between, so two workers never get the same client
      var taken = await db.Transactions
        .Where(t => t.TransactionId == id && t.SocialWorkerUserId == null && t.IsReleased != true
          && t.ApprovalStatusId == ApprovalStatuses.Pending)
        .ExecuteUpdateAsync(u => u
          .SetProperty(t => t.SocialWorkerUserId, userId)
          .SetProperty(t => t.LastUpdatedBy, userId)
          .SetProperty(t => t.DateLastUpdated, now), ct);
      if (taken == 0)
        continue;

      var patientId = await db.Transactions.Where(t => t.TransactionId == id).Select(t => t.PatientPersonId).FirstAsync(ct);
      db.AuditLogs.Add(LogEntry(id, patientId, CheckedOut, null, userId, null, userId, now, deskId));
      await db.SaveChangesAsync(ct);
      return id;
    }
    return null;
  }

  /// <summary>Calls the client again: while served, after a return, or after approval for the handover.</summary>
  public async Task<QueueActionOutcome> RecallAsync(int id, int userId, bool isAdmin, int? deskId, CancellationToken ct)
  {
    var claim = await db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == id, ct);
    if (claim is null)
      return QueueActionOutcome.NotFound;
    if (ClaimRules.IsCancelled(claim))
      return QueueActionOutcome.Closed;
    if (claim.ApprovalStatusId == ApprovalStatuses.Waiting)
      return QueueActionOutcome.InApproval;
    if (claim.SocialWorkerUserId is null)
      return QueueActionOutcome.NotServing;
    if (claim.SocialWorkerUserId != userId && !isAdmin)
      return QueueActionOutcome.Forbidden;

    deskId ??= await LastDeskAsync(id, ct);
    var now = Now();
    db.AuditLogs.Add(LogEntry(id, claim.PatientPersonId, Recalled, null, claim.SocialWorkerUserId, null, userId, now, deskId));
    await db.SaveChangesAsync(ct);
    return QueueActionOutcome.Ok;
  }

  private static readonly string[] CallActions = [CheckedOut, Recalled];

  private async Task<int?> LastDeskAsync(int id, CancellationToken ct)
  {
    var values = await db.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == Entity && a.EntityId == id && CallActions.Contains(a.Action) && a.NewValues != null)
      .OrderByDescending(a => a.DateCreated).ThenByDescending(a => a.AuditLogId)
      .Select(a => a.NewValues)
      .ToListAsync(ct);
    return values.Select(DeskOf).FirstOrDefault(d => d is not null);
  }

  internal static int? DeskOf(string? newValues)
  {
    if (string.IsNullOrEmpty(newValues))
      return null;
    using var doc = System.Text.Json.JsonDocument.Parse(newValues);
    return doc.RootElement.TryGetProperty("deskId", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number
      ? d.GetInt32() : null;
  }

  private const int TvRows = 8;
  private static readonly TimeSpan CallBackShown = TimeSpan.FromMinutes(15);
  // after these a call-back has done its job and leaves the TV
  private static readonly string[] CallBackDone = ["Printed", "Released", "SubmittedForApproval"];

  /// <summary>What the waiting-area TV shows: codes and desks only.</summary>
  public async Task<NowServingDto> NowServingAsync(CancellationToken ct)
  {
    var now = Now();
    var rows = await TodayRowsAsync(ct);
    var serving = rows.Where(r => r.State == State.Serving).ToList();

    // handover and fix-up call-backs: any day, recalled in the last 15 minutes
    var since = now - CallBackShown;
    var recalledIds = await db.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == Entity && a.Action == Recalled && a.DateCreated >= since)
      .Select(a => a.EntityId).Distinct().ToListAsync(ct);
    var callBacks = await db.Transactions.AsNoTracking()
      .Where(ClaimRules.Counted)
      .Where(t => recalledIds.Contains(t.TransactionId)
        && (t.ApprovalStatusId == ApprovalStatuses.Returned || t.ApprovalStatusId == ApprovalStatuses.Approved))
      .Include(t => t.LaneType)
      .ToListAsync(ct);

    var ids = serving.Select(r => r.Claim.TransactionId).Concat(callBacks.Select(c => c.TransactionId)).ToList();
    var logs = (await db.AuditLogs.AsNoTracking()
        .Where(a => a.Entity == Entity && ids.Contains(a.EntityId)
          && (CallActions.Contains(a.Action) || CallBackDone.Contains(a.Action)))
        .Select(a => new { a.EntityId, a.Action, a.NewValues, a.DateCreated, a.AuditLogId })
        .ToListAsync(ct))
      .ToLookup(a => a.EntityId);

    var calls = new List<(NowServingCallDto Call, int? DeskId, int Order)>();
    foreach (var r in serving)
    {
      var call = logs[r.Claim.TransactionId].Where(a => CallActions.Contains(a.Action)).MaxBy(a => a.AuditLogId);
      calls.Add((new NowServingCallDto(r.Code, null, call?.DateCreated ?? r.Since ?? now, call?.Action == Recalled),
        DeskOf(call?.NewValues), call?.AuditLogId ?? 0));
    }
    foreach (var c in callBacks)
    {
      var mine = logs[c.TransactionId].OrderBy(a => a.AuditLogId).ToList();
      var recall = mine.LastOrDefault(a => a.Action == Recalled);
      if (recall is null || recall.DateCreated < since || mine.Any(a => a.AuditLogId > recall.AuditLogId && CallBackDone.Contains(a.Action)))
        continue;
      var code = $"{c.LaneType?.LaneTypeCode?.Trim()}{c.QueueNo}";
      calls.Add((new NowServingCallDto(code, null, recall.DateCreated, true), DeskOf(recall.NewValues), recall.AuditLogId));
    }

    var deskIds = calls.Select(c => c.DeskId).OfType<int>().Distinct().ToList();
    var desks = await db.Desks.AsNoTracking().Where(d => deskIds.Contains(d.DeskId))
      .ToDictionaryAsync(d => d.DeskId, d => d.DeskName, ct);

    return new NowServingDto(
      now,
      calls
        .OrderByDescending(c => c.Call.CalledAt).ThenByDescending(c => c.Order)
        .Take(TvRows)
        .Select(c => c.Call with { Desk = c.DeskId is { } d ? desks.GetValueOrDefault(d) : null })
        .ToList(),
      InServingOrder(rows.Where(r => r.State == State.Waiting)).Take(TvRows).Select(r => r.Code).ToList());
  }

  /// <summary>Takes the claim off its worker: back to the queue, or out of it as not served.</summary>
  public async Task<QueueActionOutcome> LetGoAsync(
    int id, int userId, bool isAdmin, string action, string? reason, CancellationToken ct)
  {
    var claim = await db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == id, ct);
    if (claim is null)
      return QueueActionOutcome.NotFound;
    if (claim.IsReleased == true || claim.ApprovalStatusId == ApprovalStatuses.Approved || ClaimRules.IsCancelled(claim))
      return QueueActionOutcome.Closed;
    if (claim.ApprovalStatusId != ApprovalStatuses.Pending)
      return QueueActionOutcome.InApproval;
    if (claim.SocialWorkerUserId is null)
      return QueueActionOutcome.NotServing;
    if (claim.SocialWorkerUserId != userId && !isAdmin)
      return QueueActionOutcome.Forbidden;

    var now = Now();
    var previous = claim.SocialWorkerUserId;
    claim.SocialWorkerUserId = null;
    claim.LastUpdatedBy = userId;
    claim.DateLastUpdated = now;
    db.AuditLogs.Add(LogEntry(id, claim.PatientPersonId, action, previous, null, reason, userId, now));
    await db.SaveChangesAsync(ct);
    return QueueActionOutcome.Ok;
  }

  public static AuditLog LogEntry(
    int transactionId, int? patientId, string action, int? oldWorker, int? newWorker, string? remark, int userId, DateTime now,
    int? deskId = null) => new()
  {
    Entity = Entity,
    EntityId = transactionId,
    PersonId = patientId,
    Action = action,
    OldValues = oldWorker is null ? null : System.Text.Json.JsonSerializer.Serialize(new { socialWorkerUserId = oldWorker }),
    // the desk of a call is what the TV shows
    NewValues = newWorker is null ? null
      : deskId is null ? System.Text.Json.JsonSerializer.Serialize(new { socialWorkerUserId = newWorker })
      : System.Text.Json.JsonSerializer.Serialize(new { socialWorkerUserId = newWorker, deskId }),
    Remark = remark,
    UserId = userId,
    DateCreated = now,
  };

  public Task<Transaction?> LoadAsync(int id, CancellationToken ct) => db.Transactions.AsNoTracking()
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.FundSource)
          .ThenInclude(fundSource => fundSource!.Source)
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.Creditor)
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.Mode)
    .Include(x => x.PatientPerson)
    .Include(x => x.RepresentativePerson)
    .Include(x => x.SocialWorkerUser)
    .Include(x => x.LaneType)
    .FirstOrDefaultAsync(x => x.TransactionId == id, ct);

  private static QueueItemDto Item(Row r)
  {
    var c = r.Claim;
    var p = c.PatientPerson;
    var name = p is null ? null
      : string.Join(" ", new[] { p.LastName is null ? null : $"{p.LastName.Trim()},", p.FirstName?.Trim(), p.MiddleName?.Trim() }
          .Where(x => !string.IsNullOrWhiteSpace(x)));
    var worker = c.SocialWorkerUser is { } u
      ? string.Join(" ", new[] { u.Firstname, u.Middlename, u.Lastname }.Where(x => !string.IsNullOrWhiteSpace(x)))
      : null;
    var notServed = r.State == State.NotServed;
    return new QueueItemDto(
      c.TransactionId,
      r.Code,
      c.PatientPersonId,
      name,
      c.TransactionServices.Where(s => s.Nature?.NatureDesc is not null).Select(s => s.Nature!.NatureDesc!.Trim()).ToList(),
      c.DateCreated ?? c.Date,
      c.SocialWorkerUserId,
      worker,
      r.State == State.Serving ? r.Since : null,
      notServed ? r.Latest!.Remark : null,
      notServed ? r.Latest!.By : null);
  }
}
