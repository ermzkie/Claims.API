using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

// old-system claims: Status false and IsReleased false mean not recorded
public class CreditorLegacyClaimsTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Social Worker");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.Add(new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true });
      db.Transactions.AddRange(
        // old claims came over approved and released, Status 0 included
        new Transaction { TransactionId = 1, Status = false, Date = new DateTime(2022, 5, 1), RefNo = "OLD-2022" }.WithMoney(creditorId: 1, amount: 100m).Released(),
        new Transaction { TransactionId = 2, Status = false, Date = null, RefNo = "OLD-NODATE" }.WithMoney(creditorId: 1, amount: 40m).Released(),
        new Transaction { TransactionId = 3, Status = true, IsReleased = false, Date = new DateTime(2026, 11, 1), RefNo = "NEW", CreatedBy = userId }.WithMoney(creditorId: 1, amount: 10m),
        new Transaction { TransactionId = 4, Status = false, Date = new DateTime(2026, 11, 2), RefNo = "NEW-DELETED", CreatedBy = userId }.WithMoney(creditorId: 1, amount: 5000m));
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  [Fact]
  public async Task OldSystemClaims_Count_UnreleasedAndDeletedDoNot()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var totals = (await client.GetFromJsonAsync<List<CreditorTotalsDto>>("/api/creditor/summary"))!.Single();
    Assert.Equal(2, totals.Claims);
    Assert.Equal(140m, totals.Amount);

    var summary = await client.GetFromJsonAsync<CreditorSummaryDto>("/api/creditor/1/summary");
    Assert.Equal([new CreditorYearDto(2022, 1, 100m)], summary!.ByYear);
    Assert.Equal(1, summary.ClaimsWithoutDate);

    var page = await client.GetFromJsonAsync<CreditorClaimsPageDto>("/api/creditor/1/claims");
    Assert.Equal(["OLD-2022", "OLD-NODATE"], page!.Data.Select(c => c.RefNo));
  }

  [Fact]
  public async Task OldSystemClaims_ShowAsReleased_OthersAreNotTheCreditors()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var old = await client.GetFromJsonAsync<CreditorClaimDetailDto>("/api/creditor/1/claims/1");
    Assert.True(old!.IsReleased);
    Assert.True(old.IsActive);

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/1/claims/3")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/1/claims/4")).StatusCode);
  }
}
