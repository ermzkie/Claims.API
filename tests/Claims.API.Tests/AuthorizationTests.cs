using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class AuthorizationTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  [Fact]
  public async Task NoToken_ProtectedEndpoint_Returns401()
  {
    await using var app = new ClaimsApiFactory();
    var response = await app.CreateClient().GetAsync("/api/NatureOfService");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task GarbageToken_Returns401()
  {
    await using var app = new ClaimsApiFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
    var response = await client.GetAsync("/api/NatureOfService");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task ValidToken_Read_Returns200()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    var client = await LoginAsAsync(app, "fl", Pw);
    var response = await client.GetAsync("/api/NatureOfService");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task DeactivatedUser_ExistingToken_Gets401()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var id = await CreateUserAsync(app, "fl", Pw, "Frontliner");
    var client = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/NatureOfService")).StatusCode);

    await using (var db = app.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(u => u.UserId == id);
      user.Status = false;
      await db.SaveChangesAsync();
    }

    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/NatureOfService")).StatusCode);
  }

  [Fact]
  public async Task RoleRemovedAfterLogin_TakesEffectImmediately()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var id = await CreateUserAsync(app, "adm", Pw, "Administrator");
    var client = await LoginAsAsync(app, "adm", Pw);
    var created = await client.PostAsJsonAsync("/api/NatureOfService", new { NatureDesc = "A" });
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);

    await using (var db = app.CreateDbContext())
    {
      foreach (var ur in await db.UserRoles.Where(x => x.UserId == id).ToListAsync())
      {
        ur.Status = false;
      }
      await db.SaveChangesAsync();
    }

    var response = await client.PostAsJsonAsync("/api/NatureOfService", new { NatureDesc = "B" });
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task NatureOfService_Write_RequiresAdmin()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await CreateUserAsync(app, "adm", Pw, "Administrator");

    var fl = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.Forbidden, (await fl.PostAsJsonAsync("/api/NatureOfService", new { NatureDesc = "X" })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await fl.PutAsJsonAsync("/api/NatureOfService/1", new { NatureDesc = "X" })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await fl.DeleteAsync("/api/NatureOfService/1")).StatusCode);

    var adm = await LoginAsAsync(app, "adm", Pw);
    Assert.Equal(HttpStatusCode.Created, (await adm.PostAsJsonAsync("/api/NatureOfService", new { NatureDesc = "X" })).StatusCode);
  }

  [Fact]
  public async Task Health_RequiresLogin_AndHidesDatabaseName()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");

    Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/health/database")).StatusCode);

    var client = await LoginAsAsync(app, "fl", Pw);
    var response = await client.GetAsync("/api/health/database");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.DoesNotContain("sysCLAIMS", await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task ExpiredToken_Returns401()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    var client = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/NatureOfService")).StatusCode);

    app.Clock.Advance(TimeSpan.FromMinutes(481));

    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/NatureOfService")).StatusCode);
  }
}
