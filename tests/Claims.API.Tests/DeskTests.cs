using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Desks;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class DeskTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Admin, HttpClient Worker)> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "admin", Password, "Administrator");
    await CreateUserAsync(app, "worker", Password, "Social Worker");
    return (app, await LoginAsAsync(app, "admin", Password), await LoginAsAsync(app, "worker", Password));
  }

  private static async Task<string?> Message(HttpResponseMessage response) =>
    (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())?["message"]?.ToString();

  [Fact]
  public async Task Desks_AdminCreatesAndWorkersSeeActive()
  {
    var (app, admin, worker) = await SetupAsync();
    await using var _ = app;
    Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/desks", new { name = "Desk 2", sortOrder = 2 })).StatusCode);
    Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/desks", new { name = " Desk 1 ", sortOrder = 1 })).StatusCode);

    var desks = (await worker.GetFromJsonAsync<List<DeskDto>>("/api/desks"))!;
    Assert.Equal(["Desk 1", "Desk 2"], desks.Select(d => d.Name));
  }

  [Fact]
  public async Task Desks_NameRules()
  {
    var (app, admin, _) = await SetupAsync();
    await using var __ = app;
    var blank = await admin.PostAsJsonAsync("/api/desks", new { name = "  " });
    Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    Assert.Equal("Give the desk a name.", await Message(blank));

    var tooLong = await admin.PostAsJsonAsync("/api/desks", new { name = new string('D', 31) });
    Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    Assert.Equal("Keep the desk name to 30 characters.", await Message(tooLong));

    await admin.PostAsJsonAsync("/api/desks", new { name = "Desk 1" });
    var twin = await admin.PostAsJsonAsync("/api/desks", new { name = " desk 1" });
    Assert.Equal(HttpStatusCode.Conflict, twin.StatusCode);
    Assert.Equal("Another desk already has that name.", await Message(twin));
  }

  [Fact]
  public async Task Desks_DeactivateHidesFromPicker()
  {
    var (app, admin, worker) = await SetupAsync();
    await using var _ = app;
    var desk = (await (await admin.PostAsJsonAsync("/api/desks", new { name = "Desk 1" })).Content.ReadFromJsonAsync<DeskDto>())!;
    var off = await admin.PutAsJsonAsync($"/api/desks/{desk.DeskId}", new { name = "Desk 1", sortOrder = 0, status = false });
    Assert.Equal(HttpStatusCode.OK, off.StatusCode);

    Assert.Empty((await worker.GetFromJsonAsync<List<DeskDto>>("/api/desks"))!);
    Assert.False(Assert.Single((await admin.GetFromJsonAsync<List<DeskDto>>("/api/desks/all"))!).Status);
    Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/desks", new { name = "Desk 1" })).StatusCode);
  }

  [Fact]
  public async Task Desks_AdminsOnly()
  {
    var (app, admin, worker) = await SetupAsync();
    await using var _ = app;
    var desk = (await (await admin.PostAsJsonAsync("/api/desks", new { name = "Desk 1" })).Content.ReadFromJsonAsync<DeskDto>())!;
    Assert.Equal(HttpStatusCode.Forbidden, (await worker.PostAsJsonAsync("/api/desks", new { name = "Desk 9" })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await worker.PutAsJsonAsync($"/api/desks/{desk.DeskId}", new { name = "X" })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await worker.GetAsync("/api/desks/all")).StatusCode);
  }
}
