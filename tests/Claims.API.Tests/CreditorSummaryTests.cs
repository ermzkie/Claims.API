using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CreditorSummaryTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Social Worker");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.AddRange(
        new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true },
        new Creditor { CreditorId = 2, CreditorName = "PHARMACY B", Status = true },
        new Creditor { CreditorId = 3, CreditorName = "OLD C", Status = false });
      db.People.Add(new Person { PersonId = 10, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Status = true });

      db.Transactions.AddRange(
        new Transaction { TransactionId = 101, Status = true, Date = new DateTime(2025, 3, 1), RefNo = "A-1", PatientPersonId = 10, AssistanceCode = "A" }.WithMoney(creditorId: 1, amount: 1000m).Released(),
        new Transaction { TransactionId = 102, Status = true, Date = new DateTime(2026, 10, 5), RefNo = "A-2" }.WithMoney(creditorId: 1, amount: 2500.50m).Released(),
        new Transaction { TransactionId = 103, Status = true, Date = new DateTime(2026, 11, 9), RefNo = "A-3" }.WithMoney(creditorId: 1, amount: 300m).Released(),
        new Transaction { TransactionId = 104, Status = true, Date = null, RefNo = "A-4" }.WithMoney(creditorId: 1, amount: 50m).Released(),
        new Transaction { TransactionId = 105, Status = false, CreatedBy = userId, Date = new DateTime(2026, 1, 1), RefNo = "A-DELETED" }.WithMoney(creditorId: 1, amount: 9999m),
        new Transaction { TransactionId = 201, Status = true, Date = new DateTime(2024, 6, 1) }.WithMoney(creditorId: 2, amount: 75m).Released(),
        new Transaction { TransactionId = 301, Status = true, }.WithMoney(creditorId: null, amount: 5m).Released());
      await db.SaveChangesAsync();
      await db.AddProgramsAsync(("A", "AICS"));
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  [Fact]
  public async Task Totals_CoverActiveClaims_AndCountAllForWarnings()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var totals = await client.GetFromJsonAsync<List<CreditorTotalsDto>>("/api/creditor/summary");

    var a = totals!.Single(t => t.CreditorId == 1);
    Assert.Equal(4, a.Claims);
    Assert.Equal(3850.50m, a.Amount);
    Assert.Equal(new DateTime(2026, 11, 9), a.LastClaim);
    Assert.Equal(5, a.AllClaims);
    Assert.Equal(1, totals!.Single(t => t.CreditorId == 2).Claims);
    Assert.DoesNotContain(totals!, t => t.CreditorId == 3);
  }

  [Fact]
  public async Task Summary_GivesTotalsDatesAndYears()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var s = await client.GetFromJsonAsync<CreditorSummaryDto>("/api/creditor/1/summary");

    Assert.Equal("HOSPITAL A", s!.CreditorName);
    Assert.True(s.IsActive);
    Assert.Equal(4, s.Claims);
    Assert.Equal(3850.50m, s.Amount);
    Assert.Equal(new DateTime(2025, 3, 1), s.FirstClaim);
    Assert.Equal(new DateTime(2026, 11, 9), s.LastClaim);
    Assert.Equal(5, s.AllClaims);
    Assert.Equal(1, s.ClaimsWithoutDate);
    Assert.Equal([new CreditorYearDto(2026, 2, 2800.50m), new CreditorYearDto(2025, 1, 1000m)], s.ByYear);
  }

  [Fact]
  public async Task Summary_WorksForRemovedCreditors_And404ForUnknown()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var removed = await client.GetFromJsonAsync<CreditorSummaryDto>("/api/creditor/3/summary");
    Assert.False(removed!.IsActive);
    Assert.Equal(0, removed.Claims);
    Assert.Empty(removed.ByYear);

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/999/summary")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/999/claims")).StatusCode);
  }

  [Fact]
  public async Task Claims_AreNewestFirst_PagedAndNamed()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var page1 = await client.GetFromJsonAsync<CreditorClaimsPageDto>("/api/creditor/1/claims?pageSize=2");
    Assert.Equal(4, page1!.TotalRecords);
    Assert.Equal(2, page1.TotalPages);
    Assert.Equal(["A-3", "A-2"], page1.Data.Select(c => c.RefNo));

    var page2 = await client.GetFromJsonAsync<CreditorClaimsPageDto>("/api/creditor/1/claims?pageSize=2&page=2");
    Assert.Equal(["A-1", "A-4"], page2!.Data.Select(c => c.RefNo));
    var first = page2.Data[0];
    Assert.Equal("SANTOS, MARIA CRUZ", first.PatientName);
    Assert.Equal("AICS", first.AssistanceType);
    Assert.Equal(1000m, first.Amount);
  }

  [Fact]
  public async Task Claims_FilterByDateRange_Inclusive()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var october = await client.GetFromJsonAsync<CreditorClaimsPageDto>(
      "/api/creditor/1/claims?from=2026-10-01&to=2026-10-05");
    Assert.Equal(["A-2"], october!.Data.Select(c => c.RefNo));

    Assert.Equal(HttpStatusCode.BadRequest,
      (await client.GetAsync("/api/creditor/1/claims?from=2026-11-01&to=2026-10-01")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,
      (await client.GetAsync("/api/creditor/1/claims?pageSize=500")).StatusCode);
  }

  [Fact]
  public async Task Routes_DoNotClashWithCreditorCrud_AndNeedLogin()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/creditor/1")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/creditor/summary")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
      (await app.CreateClient().GetAsync("/api/creditor/summary")).StatusCode);
  }
}
