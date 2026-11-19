using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Queue;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class NowServingTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private sealed class Office : IAsyncDisposable
  {
    public required ClaimsApiFactory App { get; init; }
    public required HttpClient Front { get; init; }
    public required HttpClient Worker { get; init; }
    public required HttpClient Worker2 { get; init; }
    public required HttpClient Admin { get; init; }
    public required HttpClient Approver { get; init; }
    public required HttpClient Tv { get; init; }
    public short S { get; set; }
    public int Desk1 { get; set; }
    public int Desk2 { get; set; }
    public int PatientId { get; set; }

    public async ValueTask DisposeAsync()
    {
      foreach (var c in new[] { Front, Worker, Worker2, Admin, Approver, Tv })
        c.Dispose();
      await App.DisposeAsync();
    }
  }

  private static async Task<Office> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "front", Password, "Frontliner");
    await CreateUserAsync(app, "worker", Password, "Social Worker");
    await CreateUserAsync(app, "worker2", Password, "Social Worker");
    await CreateUserAsync(app, "admin", Password, "Administrator");
    await CreateUserAsync(app, "approver", Password, "Approver");
    await CreateUserAsync(app, "tv", Password, "Display");
    var o = new Office
    {
      App = app,
      Front = await LoginAsAsync(app, "front", Password),
      Worker = await LoginAsAsync(app, "worker", Password),
      Worker2 = await LoginAsAsync(app, "worker2", Password),
      Admin = await LoginAsAsync(app, "admin", Password),
      Approver = await LoginAsAsync(app, "approver", Password),
      Tv = await LoginAsAsync(app, "tv", Password),
    };

    await using var db = app.CreateDbContext();
    var s = new LaneType { LaneTypeCode = "S", LaneTypeDesc = "Senior Citizen/PWD Lane" };
    var patient = new Person { FirstName = "MARIA", LastName = "SANTOS", Status = true, Birthdate = new DateOnly(1958, 3, 1) };
    var desk1 = new Desk { DeskName = "Desk 1", SortOrder = 1, Status = true };
    var desk2 = new Desk { DeskName = "Desk 2", SortOrder = 2, Status = true };
    db.AddRange(s, patient, desk1, desk2, new Creditor { CreditorId = 501, CreditorName = "PROVINCIAL HOSPITAL", Status = true });
    db.ModeOfAssistances.AddRange(
      new ModeOfAssistance { ModeId = 2, ModeDesc = "Cash" },
      new ModeOfAssistance { ModeId = 3, ModeDesc = "Guarantee Letter" });
    await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync("INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES (1, 'AICS', 'AICS')");
    o.S = s.LaneTypeId;
    o.PatientId = patient.PersonId;
    o.Desk1 = desk1.DeskId;
    o.Desk2 = desk2.DeskId;
    return o;
  }

  // the test clock is 2026-11-10 4pm in the office
  private static async Task<int> FileAsync(Office o, string date = "2026-11-10T09:00")
  {
    var response = await o.Front.PostAsJsonAsync("/api/transactions/save", new
    {
      date, laneTypeId = o.S, patientPersonId = o.PatientId, confirmSameDay = true,
    });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
  }

  private static Task<HttpResponseMessage> Next(HttpClient client, int? deskId) =>
    deskId is null ? client.PostAsync("/api/queue/next", null) : client.PostAsJsonAsync("/api/queue/next", new { deskId });

  private static async Task<int> CallAsync(HttpClient client, int? deskId)
  {
    var response = await Next(client, deskId);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
  }

  private static Task<HttpResponseMessage> Recall(HttpClient client, int id, int? deskId = null) =>
    client.PostAsJsonAsync($"/api/queue/{id}/recall", new { deskId });

  private static async Task SendAsync(HttpClient worker, int id)
  {
    Assert.Equal(HttpStatusCode.NoContent, (await worker.PostAsJsonAsync("/api/transactions/save",
      new { transactionId = id, amount = 2500m, assistanceCode = "AICS", modeId = 3, creditorId = 501 })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await worker.PostAsync($"/api/transactions/{id}/send-for-approval", null)).StatusCode);
  }

  private static async Task<List<TransactionHistoryDto>> History(Office o, int id) =>
    (await o.Admin.GetFromJsonAsync<List<TransactionHistoryDto>>($"/api/transactions/{id}/history"))!;

  private static async Task<string?> Message(HttpResponseMessage response) =>
    (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())?["message"]?.ToString();

  [Fact]
  public async Task Call_StoresDeskInHistory()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    var id = await CallAsync(o.Worker, o.Desk1);
    Assert.Contains($"\"deskId\":{o.Desk1}", (await History(o, id)).Single(h => h.Action == "CheckedOut").NewValues);
  }

  [Fact]
  public async Task Call_WithoutDesk_StillWorks()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    await CallAsync(o.Worker, null);
  }

  [Fact]
  public async Task Call_InactiveDesk_Is400()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    await using (var db = o.App.CreateDbContext())
    {
      (await db.Desks.SingleAsync(d => d.DeskId == o.Desk1)).Status = false;
      await db.SaveChangesAsync();
    }
    var refused = await Next(o.Worker, o.Desk1);
    Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    Assert.Equal("That desk isn't in use.", await Message(refused));
    Assert.Equal(1, (await o.Admin.GetFromJsonAsync<QueueTodayDto>("/api/queue/today"))!.Waiting);

    var checkout = await o.Worker.PostAsJsonAsync("/api/transactions/1/checkout-social-worker", new { deskId = 999 });
    Assert.Equal(HttpStatusCode.BadRequest, checkout.StatusCode);
  }

  [Fact]
  public async Task Recall_Rules()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    var id = await CallAsync(o.Worker, o.Desk2);

    Assert.Equal(HttpStatusCode.NoContent, (await Recall(o.Worker, id)).StatusCode);
    Assert.Contains($"\"deskId\":{o.Desk2}", (await History(o, id)).Last(h => h.Action == "Recalled").NewValues);
    Assert.Equal(HttpStatusCode.Forbidden, (await Recall(o.Worker2, id)).StatusCode);

    await SendAsync(o.Worker, id);
    var waiting = await Recall(o.Worker, id);
    Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
    Assert.Equal("This claim is waiting for approval.", await Message(waiting));

    await o.Approver.PostAsJsonAsync($"/api/transactions/{id}/return", new { reason = "Check the bill" });
    Assert.Equal(HttpStatusCode.NoContent, (await Recall(o.Worker, id, o.Desk1)).StatusCode);
    Assert.Contains($"\"deskId\":{o.Desk1}", (await History(o, id)).Last(h => h.Action == "Recalled").NewValues);

    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/send-for-approval", null)).StatusCode);
    await o.Approver.PostAsync($"/api/transactions/{id}/approve", null);
    Assert.Equal(HttpStatusCode.NoContent, (await Recall(o.Worker, id)).StatusCode);

    var other = await FileAsync(o);
    await CallAsync(o.Worker2, o.Desk1);
    await o.Admin.PostAsJsonAsync($"/api/transactions/{other}/cancel", new { reason = "duplicate" });
    Assert.Equal(HttpStatusCode.Conflict, (await Recall(o.Admin, other)).StatusCode);
  }

  private static async Task<NowServingDto> NowServingAsync(HttpClient client) =>
    (await client.GetFromJsonAsync<NowServingDto>("/api/queue/now-serving"))!;

  private static async Task<string> CodeOf(Office o, int id) =>
    (await o.Admin.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}"))!.QueueNoCode!;

  [Fact]
  public async Task NowServing_OnlyCodesAndDesks()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    await FileAsync(o);
    await CallAsync(o.Worker, o.Desk1);

    var body = await o.Tv.GetStringAsync("/api/queue/now-serving");
    using var doc = System.Text.Json.JsonDocument.Parse(body);
    Assert.Equal(["serverTime", "calls", "next"], doc.RootElement.EnumerateObject().Select(p => p.Name));
    var call = Assert.Single(doc.RootElement.GetProperty("calls").EnumerateArray());
    Assert.Equal(["code", "desk", "calledAt", "isRecall"], call.EnumerateObject().Select(p => p.Name));
    Assert.DoesNotContain("SANTOS", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("MARIA", body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task NowServing_NewestCallFirst_AndNext()
  {
    await using var o = await SetupAsync();
    for (var i = 0; i < 5; i++)
      await FileAsync(o);
    var first = await CallAsync(o.Worker, o.Desk1);
    o.App.Clock.Advance(TimeSpan.FromMinutes(1));
    var second = await CallAsync(o.Worker2, o.Desk2);

    var now = await NowServingAsync(o.Front);
    Assert.Equal([await CodeOf(o, second), await CodeOf(o, first)], now.Calls.Select(c => c.Code));
    Assert.Equal(["Desk 2", "Desk 1"], now.Calls.Select(c => c.Desk));
    Assert.Equal(["S3", "S4", "S5"], now.Next);
  }

  [Fact]
  public async Task NowServing_ShowsDeskOfLatestCall()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    var id = await CallAsync(o.Worker, o.Desk1);
    Assert.Equal(HttpStatusCode.NoContent, (await o.Worker.PostAsJsonAsync($"/api/queue/{id}/return", new { })).StatusCode);
    o.App.Clock.Advance(TimeSpan.FromMinutes(1));
    await CallAsync(o.Worker2, o.Desk2);

    var call = Assert.Single((await NowServingAsync(o.Tv)).Calls);
    Assert.Equal("Desk 2", call.Desk);
  }

  [Fact]
  public async Task NowServing_TakeoverDoesNotMove()
  {
    await using var o = await SetupAsync();
    await FileAsync(o);
    await FileAsync(o);
    var a = await CallAsync(o.Worker, o.Desk1);
    o.App.Clock.Advance(TimeSpan.FromMinutes(1));
    var b = await CallAsync(o.Worker2, o.Desk2);
    var before = (await NowServingAsync(o.Tv)).Calls.Single(c => c.Code == "S1");
    o.App.Clock.Advance(TimeSpan.FromMinutes(1));
    Assert.Equal(HttpStatusCode.OK, (await o.Admin.PostAsJsonAsync($"/api/transactions/{a}/checkout-social-worker",
      new { takeOver = true, reason = "Ana went home" })).StatusCode);

    var calls = (await NowServingAsync(o.Tv)).Calls;
    Assert.Equal([await CodeOf(o, b), await CodeOf(o, a)], calls.Select(c => c.Code));
    Assert.Equal("Desk 1", calls[1].Desk);
    Assert.Equal(before.CalledAt, calls[1].CalledAt);
  }

  [Fact]
  public async Task NowServing_CallBackShowsThenDrops()
  {
    await using var o = await SetupAsync();
    var id = await FileAsync(o, "2026-11-09T09:00");
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsJsonAsync($"/api/transactions/{id}/checkout-social-worker", new { deskId = o.Desk1 })).StatusCode);
    await SendAsync(o.Worker, id);
    Assert.Equal(HttpStatusCode.OK, (await o.Approver.PostAsync($"/api/transactions/{id}/approve", null)).StatusCode);
    Assert.Empty((await NowServingAsync(o.Tv)).Calls);

    Assert.Equal(HttpStatusCode.NoContent, (await Recall(o.Worker, id, o.Desk2)).StatusCode);
    var call = Assert.Single((await NowServingAsync(o.Tv)).Calls);
    Assert.True(call.IsRecall);
    Assert.Equal("Desk 2", call.Desk);
    Assert.Equal("S1", call.Code);

    Assert.Equal(HttpStatusCode.OK, (await o.Worker.GetAsync($"/api/transactions/{id}/document")).StatusCode);
    Assert.Empty((await NowServingAsync(o.Tv)).Calls);

    o.App.Clock.Advance(TimeSpan.FromMinutes(1));
    await Recall(o.Worker, id);
    Assert.Single((await NowServingAsync(o.Tv)).Calls);
    o.App.Clock.Advance(TimeSpan.FromMinutes(16));
    Assert.Empty((await NowServingAsync(o.Tv)).Calls);
  }

  [Fact]
  public async Task NowServing_DisplayAndStaffCanRead()
  {
    await using var o = await SetupAsync();
    Assert.Equal(HttpStatusCode.OK, (await o.Tv.GetAsync("/api/queue/now-serving")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Front.GetAsync("/api/queue/now-serving")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Approver.GetAsync("/api/queue/now-serving")).StatusCode);
  }
}
