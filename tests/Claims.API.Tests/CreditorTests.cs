using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CreditorTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  [Fact]
  public async Task Admin_CreatesUpdatesAndDeactivatesCreditor()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var adminId = await CreateUserAsync(app, "admin", Pw, "Administrator");
    var client = await LoginAsAsync(app, "admin", Pw);

    var create = await client.PostAsJsonAsync("/api/Creditor", new { creditorName = "  Hospital  " });

    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await create.Content.ReadFromJsonAsync<CreditorResponseDto>();
    Assert.NotNull(created);
    Assert.Equal("Hospital", created.CreditorName);
    await using (var db = app.CreateDbContext())
    {
      var saved = await db.Creditors.AsNoTracking().SingleAsync();
      Assert.True(saved.Status);
      Assert.Equal(adminId, saved.CreatedBy);
      Assert.NotEqual(default, saved.DateCreated);
    }

    var update = await client.PutAsJsonAsync($"/api/Creditor/{created.CreditorId}",
      new { creditorName = "Regional Hospital" });

    Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
    await using (var db = app.CreateDbContext())
    {
      var saved = await db.Creditors.AsNoTracking().SingleAsync();
      Assert.Equal("Regional Hospital", saved.CreditorName);
      Assert.Equal(adminId, saved.LastUpdatedBy);
      Assert.NotNull(saved.DateLastUpdated);
    }

    var delete = await client.DeleteAsync($"/api/Creditor/{created.CreditorId}");

    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    await using (var db = app.CreateDbContext())
    {
      var saved = await db.Creditors.AsNoTracking().SingleAsync();
      Assert.False(saved.Status);
      Assert.Equal(adminId, saved.LastUpdatedBy);
      Assert.NotNull(saved.DateLastUpdated);
    }
    Assert.Equal(HttpStatusCode.NotFound,
      (await client.GetAsync($"/api/Creditor/{created.CreditorId}")).StatusCode);
  }
}
