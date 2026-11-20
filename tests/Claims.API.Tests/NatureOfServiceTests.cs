using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.NatureOfService;
using Claims.API.Models;
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

  private static async Task<(ClaimsApiFactory App, HttpClient Admin, HttpClient Front)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "admin", Pw, "Administrator");
    await CreateUserAsync(app, "front", Pw, "Frontliner");
    return (app, await LoginAsAsync(app, "admin", Pw), await LoginAsAsync(app, "front", Pw));
  }

  private static async Task<short> CreateAsync(HttpClient admin, string desc)
  {
    var response = await admin.PostAsJsonAsync("/api/NatureOfService", new { natureDesc = desc });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<NatureOfServiceResponseDto>())!.NatureId;
  }

  [Fact]
  public async Task Rename_WithoutStatus_KeepsItActive()
  {
    var (app, admin, _) = await StartAsync();
    await using (app)
    {
      var id = await CreateAsync(admin, "Medical");

      var response = await admin.PutAsJsonAsync($"/api/NatureOfService/{id}", new { natureDesc = "Medical Assistance" });

      Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
      var list = await admin.GetFromJsonAsync<List<NatureOfServiceResponseDto>>("/api/NatureOfService");
      var item = Assert.Single(list!);
      Assert.Equal("Medical Assistance", item.NatureDesc);
      Assert.True(item.Status);
    }
  }

  [Fact]
  public async Task TurnOff_HidesIt_AdminsCanStillFindAndTurnItBackOn()
  {
    var (app, admin, front) = await StartAsync();
    await using (app)
    {
      var id = await CreateAsync(admin, "Burial");
      await admin.PutAsJsonAsync($"/api/NatureOfService/{id}", new { natureDesc = "Burial", status = false });

      Assert.Empty((await front.GetFromJsonAsync<List<NatureOfServiceResponseDto>>("/api/NatureOfService?includeInactive=true"))!);
      var all = await admin.GetFromJsonAsync<List<NatureOfServiceResponseDto>>("/api/NatureOfService?includeInactive=true");
      Assert.False(Assert.Single(all!).Status);

      await admin.PutAsJsonAsync($"/api/NatureOfService/{id}", new { natureDesc = "Burial", status = true });
      Assert.Single((await front.GetFromJsonAsync<List<NatureOfServiceResponseDto>>("/api/NatureOfService"))!);
    }
  }

  [Fact]
  public async Task SameName_IsRefused_IgnoringCaseAndSpaces()
  {
    var (app, admin, _) = await StartAsync();
    await using (app)
    {
      await CreateAsync(admin, "Medical");
      var other = await CreateAsync(admin, "Burial");

      var create = await admin.PostAsJsonAsync("/api/NatureOfService", new { natureDesc = "  MEDICAL " });
      Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
      var rename = await admin.PutAsJsonAsync($"/api/NatureOfService/{other}", new { natureDesc = "medical" });
      Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
      var sameItself = await admin.PutAsJsonAsync($"/api/NatureOfService/{other}", new { natureDesc = "BURIAL" });
      Assert.Equal(HttpStatusCode.NoContent, sameItself.StatusCode);
    }
  }

  [Fact]
  public async Task Delete_UsedByClaims_IsRefused_UnusedIsDeleted()
  {
    var (app, admin, _) = await StartAsync();
    await using (app)
    {
      var used = await CreateAsync(admin, "Medical");
      var unused = await CreateAsync(admin, "Burial");
      await using (var db = app.CreateDbContext())
      {
        var claim = new Transaction { Status = true };
        db.Transactions.Add(claim);
        await db.SaveChangesAsync();
        db.TransactionServices.Add(new TransactionService { TransactionId = claim.TransactionId, NatureId = used });
        await db.SaveChangesAsync();
      }

      var refused = await admin.DeleteAsync($"/api/NatureOfService/{used}");
      Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
      Assert.Contains("Turn it off instead", await refused.Content.ReadAsStringAsync());
      Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/NatureOfService/{unused}")).StatusCode);
    }
  }
}
