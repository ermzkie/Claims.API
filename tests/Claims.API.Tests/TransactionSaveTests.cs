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
    var office = new Office
    {
      App = app,
      Front = await LoginAsAsync(app, "front", Password),
      Worker = await LoginAsAsync(app, "worker", Password),
      Worker2 = await LoginAsAsync(app, "worker2", Password),
      Admin = await LoginAsAsync(app, "admin", Password),
    };

    await using var db = app.CreateDbContext();
    var worker = await db.Users.SingleAsync(u => u.UserId == workerId);
    worker.Firstname = "Ana";
    worker.Lastname = "Reyes";
    var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
    var patient = new Person { FirstName = "MARIA", LastName = "SANTOS", Status = true };
    var creditor = new Creditor { CreditorId = 501, CreditorName = "PROVINCIAL HOSPITAL", Status = true };
    var removed = new Creditor { CreditorId = 502, CreditorName = "OLD PHARMACY", Status = false };
    var fund = new FundSource { FundSourceId = 601, Year = 2026, Status = true };
    var lastYear = new FundSource { FundSourceId = 602, Year = 2025, Status = true };
    db.AddRange(lane, patient, creditor, removed, fund, lastYear);
    await db.SaveChangesAsync();
    // keyless table
    await db.Database.ExecuteSqlRawAsync("INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES (1, 'AICS', 'AICS')");
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
    // a filed claim belongs to the social worker now
    Assert.Equal(HttpStatusCode.Forbidden, (await Save(o.Front, new { transactionId = id, remarks = "walk-in" })).StatusCode);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
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
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);

    var noAmount = await Save(o.Worker, new { transactionId = id, isReleased = true });
    Assert.Equal(HttpStatusCode.BadRequest, noAmount.StatusCode);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount = 3000m, isReleased = true, assistanceCode = "AICS" })).StatusCode);

    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker, new { transactionId = id, amount = 4000m })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Worker, new { transactionId = id, isReleased = false })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = id, amount = 4000m })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Save(o.Admin, new { transactionId = id, amount = 4000m, isReleased = false })).StatusCode);

    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Admin, new { transactionId = id, isReleased = false })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await Save(o.Worker, new { transactionId = id, amount = 4000m })).StatusCode);

    var history = await History(o, id);
    Assert.Equal(["Created", "CheckedOut", "Released", "Reopened", "Updated"], history.Select(h => h.Action));
    Assert.Contains("3000", history[2].NewValues);
    Assert.Contains("4000", history[4].NewValues);
    Assert.Contains("3000", history[4].OldValues);
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
  public async Task CancellingAnOldSystemClaim_RemovesItFromTheTotals()
  {
    await using var o = await SetupAsync();
    int id;
    await using (var db = o.App.CreateDbContext())
    {
      // old system: no CreatedBy, and Status 0 still counts
      var old = new Transaction { CreditorId = o.CreditorId, Amount = 1000m, Date = new DateTime(2023, 5, 1), Status = false };
      db.Transactions.Add(old);
      await db.SaveChangesAsync();
      id = old.TransactionId;
    }

    async Task<int> Claims() =>
      (await o.Admin.GetFromJsonAsync<CreditorSummaryDto>($"/api/creditor/{o.CreditorId}/summary"))!.Claims;

    Assert.Equal(1, await Claims());
    Assert.Equal(HttpStatusCode.NoContent,
      (await o.Admin.PostAsJsonAsync($"/api/transactions/{id}/cancel", new { reason = "wrong creditor" })).StatusCode);
    Assert.Equal(0, await Claims());
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

    a.Amount = 100m;
    a.DateLastUpdated = new DateTime(2026, 11, 10, 9, 0, 1);
    await first.SaveChangesAsync();

    b.Amount = 200m;
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
  public async Task Release_NeedsProgram()
  {
    await using var o = await SetupAsync();
    var id = await CreateAsync(o);
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    await Save(o.Worker, new { transactionId = id, amount = 100m });

    var noProgram = await Save(o.Worker, new { transactionId = id, isReleased = true });
    Assert.Equal(HttpStatusCode.BadRequest, noProgram.StatusCode);
    Assert.Contains("Set the program before releasing the claim.", await noProgram.Content.ReadAsStringAsync());

    var ok = await Save(o.Worker, new { transactionId = id, isReleased = true, assistanceCode = "AICS" });
    Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
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
    await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null);
    Assert.Equal(HttpStatusCode.NoContent,
      (await Save(o.Worker, new { transactionId = id, amount = 100m, assistanceCode = "AICS", isReleased = true })).StatusCode);

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
}
