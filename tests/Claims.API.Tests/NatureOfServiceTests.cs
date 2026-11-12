using System.Net;
using System.Net.Http.Json;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class NatureOfServiceTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  [Fact]
  public async Task Create_SavesActive_WithCreatedBy()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var adminId = await CreateUserAsync(app, "admin", Pw, "Administrator");
    var admin = await LoginAsAsync(app, "admin", Pw);

    var response = await admin.PostAsJsonAsync("/api/NatureOfService", new { natureDesc = "Medical" });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    await using var db = app.CreateDbContext();
    var saved = await db.NatureOfServices.AsNoTracking().SingleAsync();
    Assert.True(saved.Status);
    Assert.Equal(adminId, saved.CreatedBy);
  }
}
