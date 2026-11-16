using System.Net.Http.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

// Claims imported from the old system have no CreatedBy; many came over with Status false (all of 2022)
// and IsReleased false, which there means "not recorded", not "deleted" or "not released".
public class CreditorLegacyClaimsTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.Add(new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, CreditorId = 1, Status = false, IsReleased = false, Amount = 100m, Date = new DateTime(2022, 5, 1), RefNo = "OLD-2022" },
        new Transaction { TransactionId = 2, CreditorId = 1, Status = false, Amount = 40m, Date = null, RefNo = "OLD-NODATE" },
        new Transaction { TransactionId = 3, CreditorId = 1, Status = true, IsReleased = false, Amount = 10m, Date = new DateTime(2026, 11, 1), RefNo = "NEW", CreatedBy = userId },
        new Transaction { TransactionId = 4, CreditorId = 1, Status = false, Amount = 5000m, Date = new DateTime(2026, 11, 2), RefNo = "NEW-DELETED", CreatedBy = userId });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  [Fact]
  public async Task OldSystemClaimsWithStatusFalse_Count_NewSystemDeletionsDoNot()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var totals = (await client.GetFromJsonAsync<List<CreditorTotalsDto>>("/api/creditor/summary"))!.Single();
    Assert.Equal(3, totals.Claims);
    Assert.Equal(150m, totals.Amount);
    Assert.Equal(4, totals.AllClaims);

    var summary = await client.GetFromJsonAsync<CreditorSummaryDto>("/api/creditor/1/summary");
    Assert.Equal([new CreditorYearDto(2026, 1, 10m), new CreditorYearDto(2022, 1, 100m)], summary!.ByYear);
    Assert.Equal(1, summary.ClaimsWithoutDate);

    var page = await client.GetFromJsonAsync<CreditorClaimsPageDto>("/api/creditor/1/claims");
    Assert.Equal(["NEW", "OLD-2022", "OLD-NODATE"], page!.Data.Select(c => c.RefNo));
  }

  [Fact]
  public async Task Release_IsUnknownOnOldSystemClaims()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var old = await client.GetFromJsonAsync<CreditorClaimDetailDto>("/api/creditor/1/claims/1");
    Assert.Null(old!.IsReleased);
    Assert.True(old.IsActive);

    var recent = await client.GetFromJsonAsync<CreditorClaimDetailDto>("/api/creditor/1/claims/3");
    Assert.False(recent!.IsReleased);

    var deleted = await client.GetFromJsonAsync<CreditorClaimDetailDto>("/api/creditor/1/claims/4");
    Assert.False(deleted!.IsActive);
  }
}
