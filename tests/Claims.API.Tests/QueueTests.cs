using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Queue;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class QueueTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private sealed class Office : IAsyncDisposable
  {
    public required ClaimsApiFactory App { get; init; }
    public required HttpClient Front { get; init; }
    public required HttpClient Worker { get; init; }
    public required HttpClient Worker2 { get; init; }
    public required HttpClient Admin { get; init; }
    public short R { get; set; }
    public short S { get; set; }
    public short V { get; set; }
    public int PatientId { get; set; }

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
    var worker2Id = await CreateUserAsync(app, "worker2", Password, "Social Worker");
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
    (await db.Users.SingleAsync(u => u.UserId == workerId)).Firstname = "Ana";
    (await db.Users.SingleAsync(u => u.UserId == workerId)).Lastname = "Reyes";
    (await db.Users.SingleAsync(u => u.UserId == worker2Id)).Firstname = "Ben";
    (await db.Users.SingleAsync(u => u.UserId == worker2Id)).Lastname = "Lim";
    var r = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
    var s = new LaneType { LaneTypeCode = "S", LaneTypeDesc = "Senior Citizen/PWD Lane" };
    var v = new LaneType { LaneTypeCode = "V", LaneTypeDesc = "Very Important Persons Lane" };
    var patient = new Person { FirstName = "MARIA", MiddleName = "CRUZ", LastName = "SANTOS", Status = true, Birthdate = new DateOnly(1958, 3, 1) };
    db.AddRange(r, s, v, patient);
    await db.SaveChangesAsync();
    await db.AddProgramsAsync(("AICS", "AICS"));
    office.R = r.LaneTypeId;
    office.S = s.LaneTypeId;
    office.V = v.LaneTypeId;
    office.PatientId = patient.PersonId;
    return office;
  }

  // the test clock is 2026-11-10 4pm in the office
  private static async Task<int> FileAsync(Office o, short lane, string date = "2026-11-10T09:00")
  {
    var response = await o.Front.PostAsJsonAsync("/api/transactions/save", new
    {
      date,
      laneTypeId = lane,
      patientPersonId = o.PatientId,
      confirmSameDay = true,
    });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
  }

  private static async Task<QueueTodayDto> TodayAsync(HttpClient client) =>
    (await client.GetFromJsonAsync<QueueTodayDto>("/api/queue/today"))!;

  private static QueueLaneDto Lane(QueueTodayDto q, string code) => q.Lanes.Single(l => l.Code == code);

  [Fact]
  public async Task Today_OrdersLanesSThenVThenR()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.R);
    await FileAsync(o, o.S);
    await FileAsync(o, o.V);

    var q = await TodayAsync(o.Front);

    Assert.Equal(["S", "V", "R"], q.Lanes.Select(l => l.Code));
    Assert.Equal("S1", q.NextQueueCode);
    Assert.Equal(3, q.Waiting);
    Assert.Equal("SANTOS, MARIA CRUZ", Lane(q, "R").Waiting[0].PatientName);
    Assert.Equal(new DateOnly(2026, 11, 10), q.Day);
  }

  [Fact]
  public async Task Today_ShowsServingWithWorkerName()
  {
    await using var o = await SetupAsync();
    var s1 = await FileAsync(o, o.S);
    await FileAsync(o, o.V);

    await o.Worker.PostAsync($"/api/transactions/{s1}/checkout-social-worker", null);
    var q = await TodayAsync(o.Front);

    var serving = Assert.Single(Lane(q, "S").Serving);
    Assert.Equal("Ana Reyes", serving.SocialWorkerName);
    Assert.NotNull(serving.Since);
    Assert.Equal(1, q.Serving);
    Assert.Empty(Lane(q, "S").Waiting);
    Assert.Equal("V1", q.NextQueueCode);
  }

  [Fact]
  public async Task Today_LeavesOutOtherDaysAndCancelled()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.R, "2026-11-09T09:00");
    var cancelled = await FileAsync(o, o.R);
    await o.Admin.PostAsJsonAsync($"/api/transactions/{cancelled}/cancel", new { reason = "duplicate" });
    await FileAsync(o, o.R);

    var q = await TodayAsync(o.Front);

    Assert.Equal(1, q.Waiting);
    var only = Assert.Single(Lane(q, "R").Waiting);
    Assert.NotEqual(cancelled, only.TransactionId);
    Assert.Equal(0, q.Serving + q.Done + q.NotServed.Count);
  }

  [Fact]
  public async Task Today_IsForAnyLoggedInUser()
  {
    await using var o = await SetupAsync();

    Assert.Equal(HttpStatusCode.OK, (await o.Front.GetAsync("/api/queue/today")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await o.App.CreateClient().GetAsync("/api/queue/today")).StatusCode);
  }

  private static async Task<TransactionResponseDto?> NextAsync(HttpClient client)
  {
    var response = await client.PostAsync("/api/queue/next", null);
    Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent, response.StatusCode.ToString());
    return response.StatusCode == HttpStatusCode.OK
      ? await response.Content.ReadFromJsonAsync<TransactionResponseDto>()
      : null;
  }

  // released the only way there is: sent, approved by the admin, released by the worker
  private static async Task ReleaseAsync(Office o, int id)
  {
    await SendForApprovalAsync(o.Worker, id);
    Assert.Equal(HttpStatusCode.OK, (await o.Admin.PostAsync($"/api/transactions/{id}/approve", null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/release", null)).StatusCode);
  }

  [Fact]
  public async Task TakeNext_FollowsLaneOrder()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.R);
    await FileAsync(o, o.S);
    await FileAsync(o, o.V);

    var first = await NextAsync(o.Worker);
    Assert.Equal("S1", first!.QueueNoCode);
    await ReleaseAsync(o, first.TransactionId);
    var second = await NextAsync(o.Worker);
    Assert.Equal("V1", second!.QueueNoCode);
    await ReleaseAsync(o, second.TransactionId);
    Assert.Equal("R1", (await NextAsync(o.Worker))!.QueueNoCode);
  }

  [Fact]
  public async Task TakeNext_OneOpenClaimPerWorker()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    await FileAsync(o, o.S);
    await NextAsync(o.Worker);

    var again = await o.Worker.PostAsync("/api/queue/next", null);

    Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    Assert.Contains("You're still serving S1", await again.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task TakeNext_NobodyWaiting_Is204()
  {
    await using var o = await SetupAsync();
    Assert.Equal(HttpStatusCode.NoContent, (await o.Worker.PostAsync("/api/queue/next", null)).StatusCode);
  }

  [Fact]
  public async Task TakeNext_Frontliner_Is403()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    Assert.Equal(HttpStatusCode.Forbidden, (await o.Front.PostAsync("/api/queue/next", null)).StatusCode);
  }

  // the shared test database can't run two requests at once, so the race is set up by hand:
  // worker2 takes S1 after this worker's list was worked out
  [Fact]
  public async Task TakeNext_TwoWorkersAtOnce_GetDifferentClaims()
  {
    await using var o = await SetupAsync();
    var s1 = await FileAsync(o, o.S);
    var s2 = await FileAsync(o, o.S);
    using var scope = o.App.Services.CreateScope();
    var queue = scope.ServiceProvider.GetRequiredService<Claims.API.Services.QueueService>();
    var staleOrder = await queue.WaitingOrderAsync(default);

    var worker2Got = await NextAsync(o.Worker2);
    Assert.Equal(s1, worker2Got!.TransactionId);
    await using var db = o.App.CreateDbContext();
    var workerId = await db.Users.Where(u => u.Username == "worker").Select(u => u.UserId).SingleAsync();

    var workerGot = await queue.TakeFirstFreeAsync(workerId, staleOrder, default);

    Assert.Equal([s1, s2], staleOrder);
    Assert.Equal(s2, workerGot);
    Assert.Equal(1, await db.Transactions.CountAsync(t => t.SocialWorkerUserId == workerId));
  }

  [Fact]
  public async Task TakeNext_IsLogged()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);

    var taken = await NextAsync(o.Worker);

    var history = await o.Admin.GetFromJsonAsync<List<TransactionHistoryDto>>($"/api/transactions/{taken!.TransactionId}/history");
    Assert.Contains(history!, h => h.Action == "CheckedOut" && h.UserName == "Reyes, Ana");
  }

  [Fact]
  public async Task Return_PutsClaimAfterOthersInItsLane()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    o.App.Clock.Advance(TimeSpan.FromMinutes(5));

    var back = await o.Worker.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/return", new { reason = "went to the restroom" });

    Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
    var q = await TodayAsync(o.Front);
    Assert.Equal("S2", q.NextQueueCode);
    Assert.Equal(["S2", "S1"], Lane(q, "S").Waiting.Select(w => w.QueueCode));
    Assert.Equal(0, q.Serving);
  }

  [Fact]
  public async Task NotServed_LeavesQueue_TakeoverBringsItBack()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);

    var gone = await o.Worker.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/not-served", new { reason = "No show" });

    Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
    var q = await TodayAsync(o.Front);
    var item = Assert.Single(q.NotServed);
    Assert.Equal("No show", item.Reason);
    Assert.Equal("Ana Reyes", item.By);
    Assert.Equal(0, q.Waiting);
    Assert.Null(await NextAsync(o.Worker));

    var back = await o.Worker2.PostAsJsonAsync($"/api/transactions/{s1.TransactionId}/checkout-social-worker", new { takeOver = true, reason = "client came back" });
    Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    q = await TodayAsync(o.Front);
    Assert.Empty(q.NotServed);
    Assert.Equal("Ben Lim", Assert.Single(Lane(q, "S").Serving).SocialWorkerName);
  }

  [Fact]
  public async Task QueueActions_OnlyHolderOrAdmin()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);

    Assert.Equal(HttpStatusCode.Forbidden,
      (await o.Worker2.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/return", new { })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
      (await o.Front.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/not-served", new { reason = "x" })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent,
      (await o.Admin.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/return", new { })).StatusCode);
    // nobody has it now
    Assert.Equal(HttpStatusCode.Conflict,
      (await o.Admin.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/return", new { })).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound,
      (await o.Admin.PostAsJsonAsync("/api/queue/999999/return", new { })).StatusCode);
  }

  [Fact]
  public async Task NotServed_NeedsReason()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);

    var empty = await o.Worker.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/not-served", new { reason = " " });
    Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    Assert.Contains("Say why the client wasn't served.", await empty.Content.ReadAsStringAsync());
    var tooLong = await o.Worker.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/not-served", new { reason = new string('x', 501) });
    Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
  }

  private static async Task SendForApprovalAsync(HttpClient client, int id)
  {
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/transactions/save",
      new { transactionId = id, amount = 100m, assistanceCode = "AICS" })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/transactions/{id}/submit-for-approval", null)).StatusCode);
  }

  // the approvers have it now, so the worker can call the next client
  [Fact]
  public async Task SentForApproval_FreesTheWorker()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    await SendForApprovalAsync(o.Worker, s1!.TransactionId);

    var lane = Lane(await TodayAsync(o.Worker), "S");
    Assert.DoesNotContain(lane.Serving, x => x.TransactionId == s1.TransactionId);
    Assert.DoesNotContain(lane.Waiting, x => x.TransactionId == s1.TransactionId);
    Assert.Equal("S2", (await NextAsync(o.Worker))!.QueueNoCode);

    // approved and not released yet: still off the serving list
    Assert.Equal(HttpStatusCode.OK, (await o.Admin.PostAsync($"/api/transactions/{s1.TransactionId}/approve", null)).StatusCode);
    Assert.DoesNotContain(Lane(await TodayAsync(o.Worker), "S").Serving, x => x.TransactionId == s1.TransactionId);
  }

  [Fact]
  public async Task QueueActions_ClaimWithTheApprovers_Is409()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    await SendForApprovalAsync(o.Worker, s1!.TransactionId);

    var response = await o.Worker.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/return", new { });
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Contains("sent for approval", await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task QueueActions_ReleasedClaim_Is409()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    await ReleaseAsync(o, s1!.TransactionId);

    Assert.Equal(HttpStatusCode.Conflict,
      (await o.Admin.PostAsJsonAsync($"/api/queue/{s1.TransactionId}/return", new { })).StatusCode);
  }

  // office time without a zone, or the page shifts it another 8 hours
  [Fact]
  public async Task Today_ServerTimeIsOfficeTimeWithoutZone()
  {
    await using var o = await SetupAsync();

    var json = await o.Front.GetStringAsync("/api/queue/today");

    Assert.Contains("\"serverTime\":\"2026-11-10T16:00:00\"", json);
  }

  [Fact]
  public async Task Return_StaysAheadOfClientsWhoArriveLater()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    o.App.Clock.Advance(TimeSpan.FromMinutes(10));
    await o.Worker.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/return", new { });
    o.App.Clock.Advance(TimeSpan.FromMinutes(10));
    await FileAsync(o, o.S);

    var q = await TodayAsync(o.Front);

    Assert.Equal(["S1", "S2"], Lane(q, "S").Waiting.Select(w => w.QueueCode));
  }

  [Fact]
  public async Task BringingBackANotServedClient_KeepsTheReason()
  {
    await using var o = await SetupAsync();
    await FileAsync(o, o.S);
    var s1 = await NextAsync(o.Worker);
    await o.Worker.PostAsJsonAsync($"/api/queue/{s1!.TransactionId}/not-served", new { reason = "Went out for lunch" });

    var back = await o.Worker2.PostAsJsonAsync($"/api/transactions/{s1.TransactionId}/checkout-social-worker", new { reason = "Client came back" });

    Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    var history = await o.Admin.GetFromJsonAsync<List<TransactionHistoryDto>>($"/api/transactions/{s1.TransactionId}/history");
    var last = history!.Last();
    Assert.Equal("CheckedOut", last.Action);
    Assert.Equal("Client came back", last.Remark);
  }
}
