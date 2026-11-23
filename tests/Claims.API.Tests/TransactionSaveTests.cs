using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Creditor;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class TransactionSaveTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private sealed class Office : IAsyncDisposable
  {
    public required ClaimsApiFactory App { get; init; }
    public required HttpClient Front { get; init; }
    public required HttpClient Worker { get; init; }
    public required HttpClient Worker2 { get; init; }
    public required HttpClient Admin { get; init; }
    public required HttpClient Head { get; init; }
    public short LaneId { get; set; }
    public int PatientId { get; set; }
    public int CreditorId { get; set; }
    public int OldCreditorId { get; set; }
    public int FundSourceId { get; set; }
    public int LastYearFundSourceId { get; set; }

    public async ValueTask DisposeAsync()
    {
      Front.Dispose();
      Worker.Dispose();
      Worker2.Dispose();
      Admin.Dispose();
      Head.Dispose();
      await App.DisposeAsync();
    }
  }

  private static async Task<Office> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "front", Password, "Frontliner");
    var workerId = await CreateUserAsync(app, "worker", Password, "Social Worker");
    await CreateUserAsync(app, "worker2", Password, "Social Worker");
    await CreateUserAsync(app, "admin", Password, "Administrator");
    await CreateUserAsync(app, "head", Password, "Social Worker", "Approver");
    var office = new Office
    {
      App = app,
      Front = await LoginAsAsync(app, "front", Password),
      Worker = await LoginAsAsync(app, "worker", Password),
      Worker2 = await LoginAsAsync(app, "worker2", Password),
      Admin = await LoginAsAsync(app, "admin", Password),
      Head = await LoginAsAsync(app, "head", Password),
    };

    await using var db = app.CreateDbContext();
    var worker = await db.Users.SingleAsync(u => u.UserId == workerId);
    worker.Firstname = "Ana";
    worker.Lastname = "Reyes";
    var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
    var patient = new Person { FirstName = "MARIA", LastName = "SANTOS", Status = true, Birthdate = new DateOnly(1958, 3, 1) };
    var creditor = new Creditor { CreditorId = 501, CreditorName = "PROVINCIAL HOSPITAL", Status = true };
    var removed = new Creditor { CreditorId = 502, CreditorName = "OLD PHARMACY", Status = false };
    var fund = new FundSource { FundSourceId = 601, Year = 2026, Status = true };
    var lastYear = new FundSource { FundSourceId = 602, Year = 2025, Status = true };
    db.AddRange(lane, patient, creditor, removed, fund, lastYear);
    await db.SaveChangesAsync();
    await db.AddProgramsAsync(("AICS", "AICS"));
    office.LaneId = lane.LaneTypeId;
    office.PatientId = patient.PersonId;
    office.CreditorId = creditor.CreditorId;
    office.OldCreditorId = removed.CreditorId;
    office.FundSourceId = fund.FundSourceId;
    office.LastYearFundSourceId = lastYear.FundSourceId;
    return office;
  }

  private static async Task<int> CreateAsync(Office o, HttpClient? client = null)
  {
    var response = await (client ?? o.Front).PostAsJsonAsync("/api/transactions/save", new
    {
      date = "2026-11-10T09:00",
      laneTypeId = o.LaneId,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
    });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
  }

  private static Task<HttpResponseMessage> Save(HttpClient client, object body) =>
    client.PostAsJsonAsync("/api/transactions/save", body);

  private static Task<HttpResponseMessage> Submit(HttpClient client, int id) =>
    client.PostAsync($"/api/transactions/{id}/submit-for-approval", null);

  private static Task<HttpResponseMessage> Approve(HttpClient client, int id) =>
    client.PostAsync($"/api/transactions/{id}/approve", null);

  // the whole way: worker sets it, sends it, the admin approves, the worker releases
  private static async Task ReleaseAsync(Office o, int id, decimal amount = 100m)
  {
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount, assistanceCode = "AICS" })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Submit(o.Worker, id)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Approve(o.Admin, id)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/release", null)).StatusCode);
  }

  private static async Task<short> AddLaneAsync(Office o, string code, string name, bool on = true, bool needsEndorsement = false)
  {
    await using var db = o.App.CreateDbContext();
    var lane = new LaneType { LaneTypeCode = code, LaneTypeDesc = name, Status = on, NeedsEndorsement = needsEndorsement };
    db.LaneTypes.Add(lane);
    await db.SaveChangesAsync();
    return lane.LaneTypeId;
  }

  // filed before the lane rules existed, straight into the table
  private static async Task<int> SeedClaimAsync(Office o, short laneId)
  {
    await using var db = o.App.CreateDbContext();
    var claim = new Transaction
    {
      Date = new DateTime(2026, 11, 10, 8, 0, 0), LaneTypeId = laneId, QueueNo = 1, PatientPersonId = o.PatientId, Status = true,
    };
    db.Transactions.Add(claim);
    await db.SaveChangesAsync();
    return claim.TransactionId;
  }

  private static object NewClaim(Office o, short laneId, string? endorsedBy = null, string? reason = null) => new
  {
    date = "2026-11-10T09:00",
    laneTypeId = laneId,
    patientPersonId = o.PatientId,
    confirmSameDay = true,
    endorsedBy,
    endorsementReason = reason,
  };

  [Fact]
  public async Task EndorsedLane_NeedsEndorserAndReason()
  {
    await using var o = await SetupAsync();
    var v = await AddLaneAsync(o, "V", "Endorsed Cases Lane", needsEndorsement: true);

    var none = await Save(o.Front, NewClaim(o, v));
    Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    Assert.Contains("Endorsed Cases Lane needs who endorsed the client and why. Add them from the Front desk.",
      await none.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.BadRequest,
      (await Save(o.Front, NewClaim(o, v, "  ", "Surgery tomorrow"))).StatusCode);

    var ok = await Save(o.Front, NewClaim(o, v, "Office of the Governor", "Surgery tomorrow"));
    Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    var id = (await ok.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    var claim = await o.Admin.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal("Office of the Governor", claim!.EndorsedBy);
    Assert.Equal("Surgery tomorrow", claim.EndorsementReason);
  }

  [Fact]
  public async Task EndorsedLane_OldClaimsStillSave()
  {
    await using var o = await SetupAsync();
    var v = await AddLaneAsync(o, "V", "Endorsed Cases Lane", needsEndorsement: true);
    var id = await SeedClaimAsync(o, v);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = id, remarks = "follow-up" })).StatusCode);
  }

  [Fact]
  public async Task Endorsement_IsInTheHistory()
  {
    await using var o = await SetupAsync();
    var v = await AddLaneAsync(o, "V", "Endorsed Cases Lane", needsEndorsement: true);
    var r = await Save(o.Front, NewClaim(o, v, "Board Member Cruz", "Burial on Friday"));
    var id = (await r.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    var created = (await History(o, id)).Single(h => h.Action == "Created");
    Assert.Contains("Board Member Cruz", created.NewValues);
    Assert.Contains("Burial on Friday", created.NewValues);
  }

  [Fact]
  public async Task TurnedOffLane_RefusesNewClaims_ButOldOnesSave()
  {
    await using var o = await SetupAsync();
    var x = await AddLaneAsync(o, "X", "Extra Lane", on: false);

    var fresh = await Save(o.Front, NewClaim(o, x));
    Assert.Equal(HttpStatusCode.BadRequest, fresh.StatusCode);
    Assert.Contains("That lane is turned off.", await fresh.Content.ReadAsStringAsync());

    var old = await SeedClaimAsync(o, x);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = old, remarks = "still here" })).StatusCode);

    var other = await CreateAsync(o);
    var moved = await Save(o.Admin, new { transactionId = other, laneTypeId = x });
    Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
  }

  [Fact]
  public async Task LaneLookup_OnlyOnLanes_WithNeedsEndorsement()
  {
    await using var o = await SetupAsync();
    await AddLaneAsync(o, "V", "Endorsed Cases Lane", needsEndorsement: true);
    await AddLaneAsync(o, "X", "Extra Lane", on: false);
    using var doc = System.Text.Json.JsonDocument.Parse(await o.Front.GetStringAsync("/api/lookups/lane-types"));
    var lanes = doc.RootElement.EnumerateArray().ToList();
    Assert.DoesNotContain(lanes, l => l.GetProperty("code").GetString() == "X");
    Assert.True(lanes.Single(l => l.GetProperty("code").GetString() == "V").GetProperty("needsEndorsement").GetBoolean());
    Assert.False(lanes.Single(l => l.GetProperty("code").GetString() == "R").GetProperty("needsEndorsement").GetBoolean());
  }

  private static async Task<List<TransactionHistoryDto>> History(Office o, int id) =>
    (await o.Admin.GetFromJsonAsync<List<TransactionHistoryDto>>($"/api/transactions/{id}/history"))!;

  [Fact]
  public async Task SaveTransaction_UtcDateIsSavedAsPhilippineTime()
  {
    await using var o = await SetupAsync();

    // 7:30 am in the office, as toISOString() sends it
    var response = await Save(o.Front, new
    {
      date = "2026-11-09T23:30:00.000Z",
      laneTypeId = o.LaneId,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
    });
    var saved = await response.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(new DateTime(2026, 11, 10, 7, 30, 0), saved!.Date);

    var plain = await Save(o.Front, new
    {
      date = "2026-11-10T09:00",
      laneTypeId = o.LaneId,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
    });
    var plainSaved = await plain.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), plainSaved!.Date);
    // same day, so the same queue
    Assert.Equal((short)2, plainSaved.QueueNo);
  }

  [Fact]
  public async Task SaveTransaction_UnknownIdIsNotFoundAndCreatesNothing()
  {
    await using var o = await SetupAsync();

    var response = await Save(o.Front, new
    {
      transactionId = 999999,
      date = "2026-11-10T09:00",
      laneTypeId = o.LaneId,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
    });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    await using var db = o.App.CreateDbContext();
    Assert.Equal(0, await db.Transactions.CountAsync());
  }

  [Fact]
  public async Task GetAll_ReturnsTotalsInTheRightPlaces()
  {
    await using var o = await SetupAsync();
    for (var i = 0; i < 3; i++)
      await CreateAsync(o);

    var page = await o.Worker.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions?pageSize=2");

    Assert.Equal(3, page!.Total);
    Assert.Equal(1, page.Page);
    Assert.Equal(2, page.PageSize);
    Assert.Equal(2, page.TotalPages);
    Assert.Equal(2, page.Items.Count);

    var tooBig = await o.Worker.GetAsync("/api/transactions?pageSize=500");
    Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
  }

  [Fact]
  public async Task Frontliners_CannotSetTheMoneySide()
  {
    await using var o = await SetupAsync();

    var withAmount = await Save(o.Front, new
    {
      date = "2026-11-10T09:00",
      laneTypeId = o.LaneId,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
      amount = 5000m,
    });
    Assert.Equal(HttpStatusCode.Forbidden, withAmount.StatusCode);

    var id = await CreateAsync(o);
    Assert.Equal(HttpStatusCode.Forbidden, (await Save(o.Front, new { transactionId = id, amount = 5000m })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Save(o.Front, new { transactionId = id, creditorId = o.CreditorId })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Save(o.Front, new { transactionId = id, isReleased = true })).StatusCode);
    // the desk can fix a waiting claim, not once a social worker has it
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Front, new { transactionId = id, remarks = "walk-in" })).StatusCode);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.Forbidden, (await Save(o.Front, new { transactionId = id, remarks = "walk-in again" })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount = 5000m })).StatusCode);
  }

  [Fact]
  public async Task Edits_KeepTheFieldsThatAreLeftOut()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new
    {
      transactionId = id,
      amount = 12500.75m,
      creditorId = o.CreditorId,
      fundSourceId = o.FundSourceId,
    })).StatusCode);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, remarks = "follow-up" })).StatusCode);

    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal(12500.75m, claim!.Amount);
    Assert.Equal(o.CreditorId, claim.CreditorId);
    Assert.Equal(o.FundSourceId, claim.FundSourceId);
    Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), claim.Date);
    Assert.Equal(o.PatientId, claim.PatientPersonId);
    Assert.Equal("follow-up", claim.Remarks);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, remarks = "" })).StatusCode);
    claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Null(claim!.Remarks);
  }

  [Fact]
  public async Task ReleasedClaims_AreLockedUntilAnAdminReopensThem()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await ReleaseAsync(o, id, 3000m);

    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker, new { transactionId = id, amount = 4000m })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker, new { transactionId = id, isReleased = false })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = id, amount = 4000m })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = id, amount = 4000m, isReleased = false })).StatusCode);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = id, isReleased = false })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount = 4000m })).StatusCode);

    var history = await History(o, id);
    Assert.Equal(["Created", "CheckedOut", "Updated", "SubmittedForApproval", "Approved", "Released", "Reopened", "Updated"],
      history.Select(h => h.Action));
    Assert.Contains("3000", history[2].NewValues);
    // reopened claims go through approval again
    Assert.Contains("\"approvalStatusId\":1", history[6].NewValues);
    Assert.Contains("4000", history[7].NewValues);
    Assert.Contains("3000", history[7].OldValues);
    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal((short)1, claim!.ApprovalStatusId);
  }

  [Fact]
  public async Task SaveTransaction_RejectsBadValues()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    async Task Bad(HttpClient client, object body)
    {
      var response = await Save(client, body);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    await Bad(o.Front, new { date = "2026-11-10T09:00", laneTypeId = o.LaneId });
    await Bad(o.Front, new { date = "2026-11-10T09:00", laneTypeId = o.LaneId, patientPersonId = 999999 });
    await Bad(o.Front, new { date = "2026-11-10T09:00", laneTypeId = (short)999, patientPersonId = o.PatientId });
    await Bad(o.Worker, new { transactionId = id, amount = 0m });
    await Bad(o.Worker, new { transactionId = id, amount = -5m });
    await Bad(o.Worker, new { transactionId = id, amount = 100.125m });
    await Bad(o.Worker, new { transactionId = id, creditorId = o.OldCreditorId });
    await Bad(o.Worker, new { transactionId = id, fundSourceId = o.LastYearFundSourceId });
    await Bad(o.Worker, new { transactionId = id, natureIds = new short[] { 999 } });
    await Bad(o.Worker, new { transactionId = id, date = "2026-11-11T09:00" });

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = id, date = "2026-11-11T09:00" })).StatusCode);
  }

  [Fact]
  public async Task Checkout_DoesNotSilentlyTakeAnotherWorkersClaim()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    var url = $"/api/transactions/{id}/checkout-social-worker";

    Assert.Equal(HttpStatusCode.Forbidden, (await o.Front.PostAsync(url, null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync(url, null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync(url, null)).StatusCode);

    var taken = await o.Worker2.PostAsync(url, null);
    Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    Assert.Contains("Ana Reyes", await taken.Content.ReadAsStringAsync());

    var noReason = await o.Worker2.PostAsJsonAsync(url, new { takeOver = true });
    Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

    var takeOver = await o.Worker2.PostAsJsonAsync(url, new { takeOver = true, reason = "Ana is on leave" });
    Assert.Equal(HttpStatusCode.OK, takeOver.StatusCode);

    var history = await History(o, id);
    Assert.Equal(["Created", "CheckedOut", "TakenOver"], history.Select(h => h.Action));
    Assert.Equal("Ana is on leave", history[2].Remark);
  }

  [Fact]
  public async Task Cancel_NeedsAnAdminAndAReasonAndCanBeRestored()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    var keep = await CreateAsync(o);

    Assert.Equal(HttpStatusCode.Forbidden,
      (await o.Worker.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "duplicate" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,
      (await o.Admin.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = " " })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent,
      (await o.Admin.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "duplicate" })).StatusCode);

    var active = await o.Worker.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions");
    Assert.Equal([keep], active!.Items.Select(t => t.TransactionId));
    var cancelled = await o.Worker.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions?status=cancelled");
    Assert.Equal([id], cancelled!.Items.Select(t => t.TransactionId));

    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker, new { transactionId = id, amount = 100m })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await o.Worker.PostAsync($"/api/transactions/{id}/restore", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await o.Admin.PostAsync($"/api/transactions/{id}/restore", null)).StatusCode);

    var all = await o.Worker.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions");
    Assert.Equal(2, all!.Total);

    var history = await History(o, id);
    Assert.Equal(["Created", "Cancelled", "Restored"], history.Select(h => h.Action));
    Assert.Equal("duplicate", history[1].Remark);
  }

  [Fact]
  public async Task Cancel_FrontlinerCanCancelAndUpdatesAuditFields()
  {
    await using var o = await SetupAsync();
    int id;
    int adminTransactionId;
    await using (var db = o.App.CreateDbContext())
    {
      var transaction = new Transaction { Status = true, ApprovalStatusId = 1 };
      var adminTransaction = new Transaction { Status = true, ApprovalStatusId = 1 };
      db.Transactions.AddRange(transaction, adminTransaction);
      await db.SaveChangesAsync();
      id = transaction.TransactionId;
      adminTransactionId = adminTransaction.TransactionId;
    }

    Assert.Equal(HttpStatusCode.Forbidden,
      (await o.Worker.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "duplicate" })).StatusCode);

    var response = await o.Front.PostAsJsonAsync(
      $"/api/transactions/{id}/cancel", new { reason = "duplicate" });
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    var adminResponse = await o.Admin.PostAsJsonAsync(
      $"/api/transactions/{adminTransactionId}/cancel", new { reason = "duplicate" });
    Assert.Equal(HttpStatusCode.NoContent, adminResponse.StatusCode);

    await using var verify = o.App.CreateDbContext();
    var cancelled = await verify.Transactions.SingleAsync(transaction => transaction.TransactionId == id);
    var frontlinerId = await verify.Users
      .Where(user => user.Username == "front")
      .Select(user => user.UserId)
      .SingleAsync();
    Assert.False(cancelled.Status);
    Assert.Equal(frontlinerId, cancelled.LastUpdatedBy);
    Assert.Equal(o.App.Clock.GetUtcNow().UtcDateTime.AddHours(8), cancelled.DateLastUpdated);

    var adminCancelled = await verify.Transactions
      .SingleAsync(transaction => transaction.TransactionId == adminTransactionId);
    var adminId = await verify.Users
      .Where(user => user.Username == "admin")
      .Select(user => user.UserId)
      .SingleAsync();
    Assert.False(adminCancelled.Status);
    Assert.Equal(adminId, adminCancelled.LastUpdatedBy);
    Assert.Equal(o.App.Clock.GetUtcNow().UtcDateTime.AddHours(8), adminCancelled.DateLastUpdated);
  }

  [Fact]
  public async Task OldSystemClaim_CountsAndCantBeCancelled()
  {
    await using var o = await SetupAsync();
    int id;
    await using (var db = o.App.CreateDbContext())
    {
      // old system: no CreatedBy, Status 0, came over approved and released
      var old = new Transaction { Date = new DateTime(2023, 5, 1), Status = false }.WithMoney(creditorId: o.CreditorId, amount: 1000m).Released();
      db.Transactions.Add(old);
      await db.SaveChangesAsync();
      id = old.TransactionId;
    }

    async Task<int> Claims() =>
      (await o.Admin.GetFromJsonAsync<CreditorSummaryDto>($"/api/creditor/{o.CreditorId}/summary"))!.Claims;

    Assert.Equal(1, await Claims());
    Assert.Equal(HttpStatusCode.Conflict,
      (await o.Admin.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "wrong creditor" })).StatusCode);
    Assert.Equal(1, await Claims());
  }

  [Fact]
  public async Task TwoSavesOnTheSameClaim_CannotBothWin()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);

    await using var first = o.App.CreateDbContext();
    await using var second = o.App.CreateDbContext();
    var a = await first.Transactions.SingleAsync(t => t.TransactionId == id);
    var b = await second.Transactions.SingleAsync(t => t.TransactionId == id);

    a.Remarks = "first";
    a.DateLastUpdated = new DateTime(2026, 11, 10, 9, 0, 1);
    await first.SaveChangesAsync();

    b.Remarks = "second";
    b.DateLastUpdated = new DateTime(2026, 11, 10, 9, 0, 2);
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
  }

  [Fact]
  public async Task MovingAClaimToAnotherLane_GivesItThatLanesNextNumber()
  {
    await using var o = await SetupAsync();
    short otherLane;
    await using (var db = o.App.CreateDbContext())
    {
      var lane = new LaneType { LaneTypeCode = "S", LaneTypeDesc = "Senior Citizen/PWD Lane" };
      db.LaneTypes.Add(lane);
      await db.SaveChangesAsync();
      otherLane = lane.LaneTypeId;
    }
    var id = await CreateAsync(o);
    var inOther = await Save(o.Front, new { date = "2026-11-10T09:05", laneTypeId = otherLane, patientPersonId = o.PatientId, confirmSameDay = true });
    Assert.Equal((short)1, (await inOther.Content.ReadFromJsonAsync<TransactionResponseDto>())!.QueueNo);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, laneTypeId = otherLane })).StatusCode);

    var moved = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal((short)2, moved!.QueueNo);
  }

  [Fact]
  public async Task GetAll_RejectsAPageThatWouldOverflow()
  {
    await using var o = await SetupAsync();
    var response = await o.Worker.GetAsync($"/api/transactions?page={int.MaxValue}&pageSize=100");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ResendingOldTextWithSpaces_IsNotAChange()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await using (var db = o.App.CreateDbContext())
    {
      var claim = await db.Transactions.SingleAsync(t => t.TransactionId == id);
      claim.SocialWorkerAssessment = "needs follow-up  ";
      await db.SaveChangesAsync();
    }

    var response = await Save(o.Worker, new { transactionId = id, socialWorkerAssessment = "needs follow-up" });

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Equal(["Created"], (await History(o, id)).Select(h => h.Action));
  }

  [Fact]
  public async Task Save_ProgramIsCheckedAndStoredUpper()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, assistanceCode = " aics " })).StatusCode);
    var saved = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal("AICS", saved!.AssistanceCode);

    var bad = await Save(o.Worker, new { transactionId = id, assistanceCode = "NOPE" });
    Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    Assert.Contains("Program was not found.", await bad.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Submit_NeedsProgramAmountAndBirthdate()
  {
    await using var o = await SetupAsync();
    var noBirthdate = await PersonAsync(o, "NOBDAY");
    var created = await Save(o.Front, new { date = "2026-11-10T09:00", laneTypeId = o.LaneId, patientPersonId = noBirthdate });
    var id = (await created.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    var noProgram = await Submit(o.Worker, id);
    Assert.Equal(HttpStatusCode.BadRequest, noProgram.StatusCode);
    Assert.Contains("Set the program before sending the claim for approval.", await noProgram.Content.ReadAsStringAsync());

    await Save(o.Worker, new { transactionId = id, assistanceCode = "AICS" });
    var noAmount = await Submit(o.Worker, id);
    Assert.Equal(HttpStatusCode.BadRequest, noAmount.StatusCode);
    Assert.Contains("Set the amount before sending the claim for approval.", await noAmount.Content.ReadAsStringAsync());

    await Save(o.Worker, new { transactionId = id, amount = 100m });
    var noBirth = await Submit(o.Worker, id);
    Assert.Equal(HttpStatusCode.BadRequest, noBirth.StatusCode);
    Assert.Contains("Set the client's birthdate or approximate age before sending the claim for approval.", await noBirth.Content.ReadAsStringAsync());

    await using (var db = o.App.CreateDbContext())
    {
      var p = await db.People.FindAsync(noBirthdate);
      p!.Birthdate = new DateOnly(1956, 1, 1);
      p.BirthdateEstimated = true;
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.OK, (await Submit(o.Worker, id)).StatusCode);
    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal((short)2, claim!.ApprovalStatusId);
  }

  [Fact]
  public async Task Submit_ApprovedClaimIsRefused()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS" });
    await Submit(o.Worker, id);
    Assert.Equal(HttpStatusCode.OK, (await Approve(o.Admin, id)).StatusCode);

    var again = await Submit(o.Worker, id);
    Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    Assert.Contains("This claim is already approved.", await again.Content.ReadAsStringAsync());
    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal((short)3, claim!.ApprovalStatusId);
  }

  [Fact]
  public async Task Approve_RefusedForTheApproverWhoWorkedOnIt()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Head.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Head, new { transactionId = id, amount = 100m, assistanceCode = "AICS" });
    Assert.Equal(HttpStatusCode.OK, (await Submit(o.Head, id)).StatusCode);

    var own = await Approve(o.Head, id);
    Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
    Assert.Contains("You worked on this claim, so another approver has to approve it.", await own.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.OK, (await Approve(o.Admin, id)).StatusCode);
  }

  [Fact]
  public async Task Approve_RefusedWhenTheApproverSetTheAmountBeforeAHandover()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Head.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Head, new { transactionId = id, amount = 100m, assistanceCode = "AICS" });
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsJsonAsync($"/api/transactions/{id}/checkout-social-worker",
      new { takeOver = true, reason = "head went to a meeting" })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Submit(o.Worker, id)).StatusCode);

    Assert.Equal(HttpStatusCode.Forbidden, (await Approve(o.Head, id)).StatusCode);
  }

  [Fact]
  public async Task Approve_AnApproverWhoNeverTouchedItCan()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS" });
    await Submit(o.Worker, id);

    Assert.Equal(HttpStatusCode.OK, (await Approve(o.Head, id)).StatusCode);
  }

  [Fact]
  public async Task Save_LockedWhileWithTheApproversOrApproved()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS" });
    await Submit(o.Worker, id);

    var waiting = await Save(o.Worker, new { transactionId = id, amount = 200m });
    Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
    Assert.Contains("with the approvers", await waiting.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = id, remarks = "late note" })).StatusCode);

    await Approve(o.Admin, id);
    var approved = await Save(o.Worker, new { transactionId = id, amount = 200m });
    Assert.Equal(HttpStatusCode.Conflict, approved.StatusCode);
    Assert.Contains("already approved", await approved.Content.ReadAsStringAsync());

    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal(100m, claim!.Amount);
  }

  [Fact]
  public async Task Save_MoneyOnClaimHeldByOther_Is403_AdminCan()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    var other = await Save(o.Worker2, new { transactionId = id, amount = 50m });
    Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    Assert.Contains("Ana Reyes", await other.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = id, amount = 50m })).StatusCode);
  }

  [Fact]
  public async Task Save_MoneyOnUnheldClaim_NeedsCheckout()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);

    var money = await Save(o.Worker, new { transactionId = id, amount = 50m });
    Assert.Equal(HttpStatusCode.Forbidden, money.StatusCode);
    Assert.Contains("Check out the claim first.", await money.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker2, new { transactionId = id, remarks = "brought papers" })).StatusCode);
  }

  private static Task<HttpResponseMessage> File(Office o, string date = "2026-11-10T09:00", bool confirm = false) =>
    Save(o.Front, new { date, laneTypeId = o.LaneId, patientPersonId = o.PatientId, confirmSameDay = confirm });

  [Fact]
  public async Task SameDay_SecondOpenClaim_Is409_UnlessConfirmed()
  {
    await using var o = await SetupAsync();
    var first = await CreateAsync(o);

    var again = await File(o);
    Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    using var json = System.Text.Json.JsonDocument.Parse(await again.Content.ReadAsStringAsync());
    Assert.True(json.RootElement.GetProperty("sameDay").GetBoolean());
    Assert.Equal("R1", json.RootElement.GetProperty("queueNoCode").GetString());
    Assert.Equal(first, json.RootElement.GetProperty("transactionId").GetInt32());

    Assert.Equal(HttpStatusCode.Created, (await File(o, confirm: true)).StatusCode);
  }

  [Fact]
  public async Task SameDay_ReleasedEarlierToday_IsAllowed()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await ReleaseAsync(o, id);

    Assert.Equal(HttpStatusCode.Created, (await File(o)).StatusCode);
  }

  [Fact]
  public async Task SameDay_OtherDay_IsAllowed()
  {
    await using var o = await SetupAsync();
    Assert.Equal(HttpStatusCode.Created, (await File(o, "2026-11-09T09:00")).StatusCode);
    Assert.Equal(HttpStatusCode.Created, (await File(o, "2026-11-10T09:00")).StatusCode);
  }

  [Fact]
  public async Task SameDay_CancelledClaim_IsAllowed()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Admin.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "wrong lane" });

    Assert.Equal(HttpStatusCode.Created, (await File(o)).StatusCode);
  }

  private static async Task<int> PersonAsync(Office o, string first, DateOnly? birthdate = null, bool estimated = false)
  {
    await using var db = o.App.CreateDbContext();
    var p = new Person { FirstName = first, LastName = "TEST", Status = true, Birthdate = birthdate, BirthdateEstimated = estimated };
    db.People.Add(p);
    await db.SaveChangesAsync();
    return p.PersonId;
  }

  [Fact]
  public async Task Save_CantRelease_EvenAnOldClaim()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    var refused = await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS", isReleased = true });
    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    Assert.Contains("Send the claim for approval, then release it once it's approved.", await refused.Content.ReadAsStringAsync());
    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.NotEqual(true, claim!.IsReleased);

    // old-system claim
    int oldId;
    await using (var db = o.App.CreateDbContext())
    {
      var old = new Transaction { PatientPersonId = o.PatientId, LaneTypeId = o.LaneId, Date = new DateTime(2025, 3, 1), AssistanceCode = "AICS", Status = false }.WithMoney(amount: 500m);
      db.Transactions.Add(old);
      await db.SaveChangesAsync();
      oldId = old.TransactionId;
    }
    await o.Worker2.PostAsync($"/api/transactions/{oldId}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker2, new { transactionId = oldId, isReleased = true })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = oldId, isReleased = true })).StatusCode);
  }

  [Fact]
  public async Task ReleaseAfterApproval_NeedsBirthdate()
  {
    await using var o = await SetupAsync();
    var noBirthdate = await PersonAsync(o, "NOBDAY");
    var created = await Save(o.Front, new { date = "2026-11-10T09:00", laneTypeId = o.LaneId, patientPersonId = noBirthdate });
    var id = (await created.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS" })).StatusCode);
    // sent before sending needed a birthdate
    await using (var db = o.App.CreateDbContext())
    {
      (await db.Transactions.FindAsync(id))!.ApprovalStatusId = 2;
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.OK, (await o.Admin.PostAsync($"/api/transactions/{id}/approve", null)).StatusCode);

    var refused = await o.Worker.PostAsync($"/api/transactions/{id}/release", null);
    Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    Assert.Contains("Set the client's birthdate or approximate age before releasing.", await refused.Content.ReadAsStringAsync());

    await using (var db = o.App.CreateDbContext())
    {
      var p = await db.People.FindAsync(noBirthdate);
      p!.Birthdate = new DateOnly(1956, 1, 1);
      p.BirthdateEstimated = true;
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/release", null)).StatusCode);
  }

  [Fact]
  public async Task ClearRepresentative_ClearsLinkAndOldText()
  {
    await using var o = await SetupAsync();
    var rep = await PersonAsync(o, "REPRESENT");
    var id = await CreateAsync(o);
    await using (var db = o.App.CreateDbContext())
    {
      var t = await db.Transactions.FindAsync(id);
      t!.RepresentativePersonId = rep;
      t.Representative = "LOLA";
      await db.SaveChangesAsync();
    }
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, clearRepresentative = true })).StatusCode);

    await using var check = o.App.CreateDbContext();
    var saved = await check.Transactions.FindAsync(id);
    Assert.Null(saved!.RepresentativePersonId);
    Assert.Null(saved.Representative);
    var last = (await History(o, id)).Last();
    Assert.Contains("LOLA", last.OldValues);
  }

  [Fact]
  public async Task Representative_CanBeTheClient()
  {
    // the intake sheet's "same as the patient" saves it this way
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, representativePersonId = o.PatientId })).StatusCode);
    var claim = await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}");
    Assert.Equal(o.PatientId, claim!.RepresentativePersonId);
  }
}
