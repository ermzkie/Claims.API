using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CreditorArchiveTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Admin, HttpClient Staff, int AdminId)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var adminId = await CreateUserAsync(app, "admin", Pw, "Administrator");
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.AddRange(
        new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true },
        new Creditor { CreditorId = 2, CreditorName = "OLD PHARMACY", Status = false },
        new Creditor { CreditorId = 3, CreditorName = "hospital a ", Status = false },
        new Creditor { CreditorId = 4, CreditorName = null, Status = false });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "admin", Pw), await LoginAsAsync(app, "fl", Pw), adminId);
  }

  [Fact]
  public async Task Removed_ListsOnlyRemovedCreditors()
  {
    var (app, _, staff, _) = await StartAsync();
    await using var __ = app;

    var removed = await staff.GetFromJsonAsync<List<CreditorResponseDto>>("/api/creditor/removed");

    Assert.Equal([4, 3, 2], removed!.Select(c => c.CreditorId));
  }

  [Fact]
  public async Task Restore_BringsItBack_WithAuditFields()
  {
    var (app, admin, _, adminId) = await StartAsync();
    await using var __ = app;

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/creditor/2/restore", null)).StatusCode);

    await using var db = app.CreateDbContext();
    var restored = await db.Creditors.AsNoTracking().SingleAsync(c => c.CreditorId == 2);
    Assert.True(restored.Status);
    Assert.Equal(adminId, restored.LastUpdatedBy);
    Assert.NotNull(restored.DateLastUpdated);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/creditor/2")).StatusCode);
  }

  [Fact]
  public async Task Restore_RefusesDuplicatesActiveOnesAndNonAdmins()
  {
    var (app, admin, staff, _) = await StartAsync();
    await using var __ = app;

    Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/creditor/3/restore", null)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/creditor/1/restore", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/creditor/99/restore", null)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/creditor/2/restore", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/creditor/4/restore", null)).StatusCode);
  }
}
