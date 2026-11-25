using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

// who may see what, per the role table
public class AccessTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private sealed record Office(
    ClaimsApiFactory App, HttpClient Front, HttpClient Worker, HttpClient Admin, HttpClient Both,
    int PersonId, int ClaimId, int CreditorId) : IAsyncDisposable
  {
    public async ValueTask DisposeAsync()
    {
      Front.Dispose();
      Worker.Dispose();
      Admin.Dispose();
      Both.Dispose();
      await App.DisposeAsync();
    }
  }

  private static async Task<Office> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "front", Password, "Frontliner");
    await CreateUserAsync(app, "worker", Password, "Social Worker");
    await CreateUserAsync(app, "admin", Password, "Administrator");
    await CreateUserAsync(app, "both", Password, "Frontliner", "Social Worker");
    int personId, creditorId;
    short laneId;
    await using (var db = app.CreateDbContext())
    {
      var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
      var person = new Person { FirstName = "MARIA", LastName = "SANTOS", Status = true, EstimatedMonthlyIncome = 8000m };
      var creditor = new Creditor { CreditorId = 501, CreditorName = "PROVINCIAL HOSPITAL", Status = true };
      db.AddRange(lane, person, creditor);
      await db.SaveChangesAsync();
      personId = person.PersonId;
      creditorId = creditor.CreditorId;
      laneId = lane.LaneTypeId;
    }
    var front = await LoginAsAsync(app, "front", Password);
    var created = await front.PostAsJsonAsync("/api/transactions/save",
      new { date = "2026-11-10T09:00", laneTypeId = laneId, patientPersonId = personId });
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    var claimId = (await created.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    return new Office(app, front, await LoginAsAsync(app, "worker", Password), await LoginAsAsync(app, "admin", Password),
      await LoginAsAsync(app, "both", Password), personId, claimId, creditorId);
  }

  private static string[] ClaimAndMoneyViews(Office o) =>
  [
    "/api/transactions",
    $"/api/transactions/{o.ClaimId}",
    $"/api/transactions/{o.ClaimId}/history",
    "/api/dashboard",
    "/api/creditor/summary",
    "/api/creditor",
    "/api/reports/transactions",
    $"/api/persons/{o.PersonId}/transactions",
  ];

  [Fact]
  public async Task Frontliner_CannotReadClaimsOrMoney()
  {
    await using var o = await SetupAsync();
    foreach (var path in ClaimAndMoneyViews(o))
      Assert.True((await o.Front.GetAsync(path)).StatusCode == HttpStatusCode.Forbidden, $"{path} should be 403 for a frontliner");
  }

  [Fact]
  public async Task SocialWorker_CanReadClaimsAndMoney()
  {
    await using var o = await SetupAsync();
    foreach (var path in ClaimAndMoneyViews(o))
      Assert.True((await o.Worker.GetAsync(path)).StatusCode == HttpStatusCode.OK, $"{path} should be 200 for a social worker");
  }

  // approvers see the claims they approve, the role table gives them the same views
  [Fact]
  public async Task Approver_CanReadClaimsAndMoney()
  {
    await using var o = await SetupAsync();
    await CreateUserAsync(o.App, "approver", Password, "Approver");
    using var approver = await LoginAsAsync(o.App, "approver", Password);
    foreach (var path in ClaimAndMoneyViews(o))
      Assert.True((await approver.GetAsync(path)).StatusCode == HttpStatusCode.OK, $"{path} should be 200 for an approver");
    Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync($"/api/creditor/{o.CreditorId}/claims/export")).StatusCode);
  }

  [Fact]
  public async Task Export_IsAdminOnly()
  {
    await using var o = await SetupAsync();
    var path = $"/api/creditor/{o.CreditorId}/claims/export";
    Assert.Equal(HttpStatusCode.Forbidden, (await o.Worker.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Admin.GetAsync(path)).StatusCode);
  }

  [Fact]
  public async Task Frontliner_CanFixAClaimStillWaiting()
  {
    await using var o = await SetupAsync();
    // the setup filed one as the frontliner already; the intake sheet goes back to fix it
    var edit = await o.Front.PostAsJsonAsync("/api/transactions/save", new { transactionId = o.ClaimId, remarks = "late" });
    Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
  }

  [Fact]
  public async Task Frontliner_CannotChangeAClaimAWorkerPickedUp()
  {
    await using var o = await SetupAsync();
    await o.Worker.PostAsync($"/api/transactions/{o.ClaimId}/checkout-social-worker", null);
    var edit = await o.Front.PostAsJsonAsync("/api/transactions/save", new { transactionId = o.ClaimId, remarks = "late" });
    Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent,
      (await o.Worker.PostAsJsonAsync("/api/transactions/save", new { transactionId = o.ClaimId, remarks = "late" })).StatusCode);
  }

  [Fact]
  public async Task Frontliner_CanStillUseQueueAndPersons()
  {
    await using var o = await SetupAsync();
    foreach (var path in new[] { "/api/queue/today", "/api/persons?search=santos", $"/api/persons/{o.PersonId}", "/api/lookups/lane-types" })
      Assert.True((await o.Front.GetAsync(path)).StatusCode == HttpStatusCode.OK, $"{path} should be 200 for a frontliner");
  }

  private static object Basics(string contact, decimal? income = 8000m, short? civil = null, string? job = null) => new
  {
    firstName = "Maria",
    lastName = "Santos",
    contactNo = contact,
    estimatedMonthlyIncome = income,
    civilStatusId = civil,
    skillOccupation = job,
  };

  [Fact]
  public async Task Frontliner_CanEditBasics_NotIncome()
  {
    await using var o = await SetupAsync();
    await using (var db = o.App.CreateDbContext())
    {
      db.CivilStatuses.Add(new CivilStatus { CivilStatusId = 1, CivilStatusDesc = "Single" });
      await db.SaveChangesAsync();
    }

    Assert.Equal(HttpStatusCode.OK, (await o.Front.PutAsJsonAsync($"/api/persons/{o.PersonId}", Basics("09171234567"))).StatusCode);
    foreach (var body in new[] { Basics("09171234567", income: 9000m), Basics("09171234567", civil: 1), Basics("09171234567", job: "Farmer") })
    {
      var r = await o.Front.PutAsJsonAsync($"/api/persons/{o.PersonId}", body);
      Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
      Assert.Contains("filled in by the social worker", await r.Content.ReadAsStringAsync());
    }
  }

  [Fact]
  public async Task Frontliner_ResendingSameIncome_IsAllowed()
  {
    await using var o = await SetupAsync();
    var r = await o.Front.PutAsJsonAsync($"/api/persons/{o.PersonId}", Basics("09281112233", income: 8000m));
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
  }

  [Fact]
  public async Task Frontliner_CannotChangeFamily()
  {
    await using var o = await SetupAsync();
    var r = await o.Front.PostAsJsonAsync($"/api/persons/{o.PersonId}/family", new { familyPersonId = o.PersonId, relationshipStatusId = 1 });
    Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
  }

  [Fact]
  public async Task FrontlinerAndSocialWorker_CanEditIncome()
  {
    await using var o = await SetupAsync();
    var r = await o.Both.PutAsJsonAsync($"/api/persons/{o.PersonId}", Basics("09171234567", income: 9500m));
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
  }

  [Fact]
  public async Task Frontliner_OldUntrimmedOccupation_IsNotAChange()
  {
    await using var o = await SetupAsync();
    foreach (var stored in new[] { "FARMER ", "", "  " })
    {
      await using (var db = o.App.CreateDbContext())
      {
        var p = await db.People.FindAsync(o.PersonId);
        p!.SkillOccupation = stored;
        await db.SaveChangesAsync();
      }
      // the page sends back what it got
      var r = await o.Front.PutAsJsonAsync($"/api/persons/{o.PersonId}", Basics("09171234567", job: stored));
      Assert.True(r.StatusCode == HttpStatusCode.OK, $"stored '{stored}' gave {r.StatusCode}");
    }
  }

  [Fact]
  public async Task Frontliner_CreatesWithBasicsOnly()
  {
    await using var o = await SetupAsync();
    var withIncome = await o.Front.PostAsJsonAsync("/api/persons", new { firstName = "Ana", lastName = "Reyes", estimatedMonthlyIncome = 5000m });
    Assert.Equal(HttpStatusCode.Forbidden, withIncome.StatusCode);
    var withJob = await o.Front.PostAsJsonAsync("/api/persons", new { firstName = "Ana", lastName = "Reyes", skillOccupation = "Vendor" });
    Assert.Equal(HttpStatusCode.Forbidden, withJob.StatusCode);
    var basics = await o.Front.PostAsJsonAsync("/api/persons", new { firstName = "Ana", lastName = "Reyes", contactNo = "09171234567" });
    Assert.Equal(HttpStatusCode.Created, basics.StatusCode);
    Assert.Equal(HttpStatusCode.Created,
      (await o.Worker.PostAsJsonAsync("/api/persons", new { firstName = "Ben", lastName = "Lim", estimatedMonthlyIncome = 5000m })).StatusCode);
  }

  private static async Task<(ClaimsApiFactory App, HttpClient Tv)> DisplaySetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "tv", Password, "Display");
    return (app, await LoginAsAsync(app, "tv", Password));
  }

  [Fact]
  public async Task Display_RefusedEverywhereButNowServing()
  {
    var (app, tv) = await DisplaySetupAsync();
    await using var _ = app;
    foreach (var url in new[]
    {
      "/api/queue/today", "/api/queue/mine", "/api/transactions", "/api/persons?search=a", "/api/desks", "/api/desks/all",
      "/api/approvals", "/api/lookups/sexes", "/api/dashboard",
    })
      Assert.True((await tv.GetAsync(url)).StatusCode == HttpStatusCode.Forbidden, url);
    Assert.Equal(HttpStatusCode.Forbidden, (await tv.PostAsync("/api/queue/next", null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await tv.GetAsync("/api/queue/now-serving")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await tv.GetAsync("/api/auth/me")).StatusCode);
  }

  [Fact]
  public async Task Display_WithAnotherRole_IsNotLocked()
  {
    var app = new ClaimsApiFactory();
    await using var _ = app;
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "tvfront", Password, "Display", "Frontliner");
    using var client = await LoginAsAsync(app, "tvfront", Password);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/queue/today")).StatusCode);
  }

  [Fact]
  public async Task Display_TokenLasts16Hours()
  {
    var app = new ClaimsApiFactory();
    await using var _ = app;
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "tv", Password, "Display");
    await CreateUserAsync(app, "front", Password, "Frontliner");
    using var anon = app.CreateClient();
    async Task<TimeSpan> Lasts(string user)
    {
      var login = await anon.PostAsJsonAsync("/api/auth/login", new { username = user, password = Password });
      var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
      return DateTime.Parse(body!["expiresAt"].ToString()!, null,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal) - app.Clock.GetUtcNow().UtcDateTime;
    }
    Assert.Equal(TimeSpan.FromHours(16), await Lasts("tv"));
    Assert.Equal(TimeSpan.FromHours(8), await Lasts("front"));
  }
}
