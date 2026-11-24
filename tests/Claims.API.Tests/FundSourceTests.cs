using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.FundSources;
using Claims.API.Models;
using Claims.API.Services;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class FundSourceTests
{
  private const string Pw = "Str0ng-Passw0rd!x";
  private static readonly FinanceAccountDto Mooe = new(27408, "5-02-99-990", "Other Maintenance and Operating Expenses", 40_784_000m, 807);
  private static readonly FinanceAccountDto Mooe2 = new(27407, "5-02-99-080", "Donations", 1_000_000m, 4);
  private static readonly FinanceAccountDto Salaries = new(26865, "5-01-01-010", "Salaries and Wages - Regular", 14_019_881m);
  private static readonly FinanceAccountDto Pagcor = new(2534, "2-02-01-050 (-0030)", "HOST PROVINCIAL SHARE (PAGCOR)", 1_000_000m, 381);
  private static readonly FinanceAccountDto Pension = new(2503, "416-0017(00004)", "SOCIAL PENSION - KORONADAL", 449_928_000m);
  // reworded, but paid Claims creditors
  private static readonly FinanceAccountDto Reworded = new(27500, "5-02-99-990", "Other Maintenance and Operating Expenses", 2_000_000m, 0, 12);

  private static async Task<(ClaimsApiFactory App, HttpClient Admin)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var adminId = await CreateUserAsync(app, "admin", Pw, "Administrator");
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    app.Finance.Appropriations.AddRange([(2026, Mooe), (2026, Mooe2), (2026, Salaries), (2026, Reworded), (2025, new FinanceAccountDto(25000, "5-02", "Old", 1m, 9))]);
    app.Finance.TrustFunds.AddRange([Pagcor, Pension]);
    app.Finance.Usage[27408] = new FinanceUsageDto(27408, 20_000_000m, 7_500_000m, 250_000m, 15_000_000m, 4_000_000m, 1_000_000m);
    await using (var db = app.CreateDbContext())
    {
      db.SourceOfAssistances.AddRange(
        new SourceOfAssistance { SourceId = 1, SourceDesc = "AICS" },
        new SourceOfAssistance { SourceId = 8, SourceDesc = "IHF - Financial" });
      db.FundSources.Add(new FundSource { FundSourceId = 1, Year = 2026, SourceId = 1, AppropriationId = 27408, Status = true, DateCreated = new DateTime(2026, 1, 1), CreatedBy = 500 });
      db.People.Add(new Person { PersonId = 10, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Status = true, DateCreated = new DateTime(2025, 1, 1) });
      db.Creditors.Add(new Creditor { CreditorId = 5, CreditorName = "MERCURY DRUG", Status = true });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, Status = true, Date = new DateTime(2026, 3, 1), PatientPersonId = 10, RefNo = " A-1 " }.WithMoney(fundSourceId: 1, creditorId: 5, amount: 2500m),
        new Transaction { TransactionId = 3, Status = true, Date = new DateTime(2026, 4, 1), }.WithMoney(fundSourceId: 1, amount: 1000m),
        // deleted, but still points here
        new Transaction { TransactionId = 2, Status = false, CreatedBy = adminId, Date = new DateTime(2026, 3, 2), }.WithMoney(fundSourceId: 1));
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "admin", Pw));
  }

  private static SaveFundSourceDto Body(int year = 2026, short sourceId = 8, int? appropriationId = null, int? trustFundId = 2534) =>
    new() { Year = year, SourceId = sourceId, AppropriationId = appropriationId, TrustFundId = trustFundId };

  [Fact]
  public async Task List_ShowsTheFinanceAccountAndClaims()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var list = await admin.GetFromJsonAsync<FundSourceListDto>("/api/fund-sources");

    Assert.Equal((2026, true), (list!.Year, list.FinanceAvailable));
    Assert.Equal([2026], list.Years);
    var aics = Assert.Single(list.Items);
    Assert.Equal((1, "AICS", "appropriation", 27408, Mooe, 2, 3500m), (aics.FundSourceId, aics.Program, aics.Kind, aics.FinanceId, aics.Finance, aics.Claims, aics.ClaimsAmount));
    Assert.Equal((20_000_000m, 7_500_000m, 250_000m), (aics.Released, aics.Obligated, aics.Pending));
    Assert.Equal((15_000_000m, 4_000_000m, 1_000_000m), (aics.Original, aics.Supplemental, aics.Adjustments));
    // CreatedBy isn't a user here, so only the date shows
    Assert.Equal((new DateTime(2026, 1, 1), null, null, null), (aics.AddedOn, aics.AddedBy, aics.ChangedOn, aics.ChangedBy));

    var empty = await admin.GetFromJsonAsync<FundSourceListDto>("/api/fund-sources?year=2025");
    Assert.Empty(empty!.Items);
    Assert.Equal([2026, 2025], empty.Years);

    var frontliner = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.GetAsync("/api/fund-sources")).StatusCode);
  }

  [Fact]
  public async Task List_StillShowsWhenFinanceIsDown()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;
    app.Finance.Down = true;

    var list = await admin.GetFromJsonAsync<FundSourceListDto>("/api/fund-sources?year=2026");

    Assert.False(list!.FinanceAvailable);
    var item = Assert.Single(list.Items);
    Assert.Equal((null, null, null, null, 2), (item.Finance, item.Released, item.Obligated, item.Pending, item.Claims));
  }

  [Fact]
  public async Task FinanceAccounts_ByKind()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var appropriations = await admin.GetFromJsonAsync<List<FinanceAccountDto>>("/api/fund-sources/finance-accounts?kind=appropriation&year=2026");
    // salaries and social pension aren't offered
    Assert.Equal([27408, 27407, 27500], appropriations!.Select(a => a.Id));
    var trustFunds = await admin.GetFromJsonAsync<List<FinanceAccountDto>>("/api/fund-sources/finance-accounts?kind=trustFund");
    Assert.Equal([2534], trustFunds!.Select(a => a.Id));

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/fund-sources/finance-accounts?kind=other")).StatusCode);
    app.Finance.Down = true;
    Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync("/api/fund-sources/finance-accounts?kind=trustFund")).StatusCode);
  }

  [Fact]
  public async Task Create_ChecksProgramAndAccount()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var created = await admin.PostAsJsonAsync("/api/fund-sources", Body());
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    var fund = await created.Content.ReadFromJsonAsync<FundSourceDto>();
    Assert.Equal(("IHF - Financial", "trustFund", 2534, Pagcor, 0), (fund!.Program, fund.Kind, fund.FinanceId, fund.Finance, fund.Claims));
    Assert.Equal("admin", fund.AddedBy);

    async Task<string?> Rejected(SaveFundSourceDto body)
    {
      var response = await admin.PostAsJsonAsync("/api/fund-sources", body);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      return (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["message"];
    }
    Assert.Equal("Pick either an appropriation or a trust fund.", await Rejected(Body(appropriationId: 27408)));
    Assert.Equal("Pick either an appropriation or a trust fund.", await Rejected(Body(trustFundId: null)));
    Assert.Equal("Pick the program.", await Rejected(Body(sourceId: 99)));
    Assert.Equal("Give a year from 2000 to next year.", await Rejected(Body(year: 2028)));
    Assert.Equal("That account is already set up for AICS in 2026.", await Rejected(Body(appropriationId: 27408, trustFundId: null)));
    // a 2025 appropriation can't pay for 2026
    Assert.StartsWith("Appropriation 25000 isn't one", await Rejected(Body(appropriationId: 25000, trustFundId: null)));
    // nor saved through the API
    Assert.StartsWith("Salaries and Wages - Regular (#26865) hasn't paid any medical", await Rejected(Body(appropriationId: 26865, trustFundId: null)));
    Assert.StartsWith("SOCIAL PENSION - KORONADAL (#2503) has never paid", await Rejected(Body(trustFundId: 2503)));

    app.Finance.Down = true;
    var down = await admin.PostAsJsonAsync("/api/fund-sources", Body(appropriationId: 27407, trustFundId: null));
    Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);

    await using var db = app.CreateDbContext();
    Assert.Equal(2, await db.FundSources.CountAsync());
    var log = await db.AuditLogs.SingleAsync();
    Assert.Equal(("FundSource", fund.FundSourceId, "Added"), (log.Entity, log.EntityId, log.Action));
  }

  [Fact]
  public async Task Update_KeepsYearAndProgramOnceClaimsUseIt()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var changed = await admin.PutAsJsonAsync("/api/fund-sources/1", Body(sourceId: 1, appropriationId: 27407, trustFundId: null));
    Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    var after = await changed.Content.ReadFromJsonAsync<FundSourceDto>();
    Assert.Equal(27407, after!.FinanceId);
    Assert.Equal((app.Clock.GetLocalNow().DateTime, "admin"), (after.ChangedOn, after.ChangedBy));

    var moved = await admin.PutAsJsonAsync("/api/fund-sources/1", Body(sourceId: 8, appropriationId: 27407, trustFundId: null));
    Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);

    // saving it unchanged doesn't log anything
    await admin.PutAsJsonAsync("/api/fund-sources/1", Body(sourceId: 1, appropriationId: 27407, trustFundId: null));
    await using var db = app.CreateDbContext();
    var log = await db.AuditLogs.SingleAsync();
    Assert.Equal("Changed", log.Action);
    Assert.Contains("27408", log.OldValues);
    Assert.Contains("27407", log.NewValues);
  }

  [Fact]
  public async Task Delete_OnlyWhenNoClaimUsesIt()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var used = await admin.DeleteAsync("/api/fund-sources/1");
    Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);

    var created = await (await admin.PostAsJsonAsync("/api/fund-sources", Body())).Content.ReadFromJsonAsync<FundSourceDto>();
    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/fund-sources/{created!.FundSourceId}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/fund-sources/{created.FundSourceId}")).StatusCode);

    await using var db = app.CreateDbContext();
    Assert.Equal([1], await db.FundSources.Select(f => f.FundSourceId).ToListAsync());
  }

  [Fact]
  public async Task Claims_LatestFirstWithoutDeleted()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var claims = await admin.GetFromJsonAsync<List<FundSourceClaimDto>>("/api/fund-sources/1/claims");

    Assert.Equal([3, 1], claims!.Select(c => c.TransactionId));
    var first = claims[1];
    Assert.Equal(("A-1", "SANTOS, MARIA CRUZ", 10, 5, "MERCURY DRUG", 2500m), (first.RefNo, first.Patient, first.PatientPersonId, first.CreditorId, first.CreditorName, first.Amount));
    Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/fund-sources/99/claims")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/fund-sources/1/claims?take=0")).StatusCode);
  }
}
