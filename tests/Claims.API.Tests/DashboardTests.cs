using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Dashboard;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class DashboardTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  // the test clock is 2026-11-10, so the default month is November 2026
  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.AddRange(
        new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true },
        new Creditor { CreditorId = 2, CreditorName = "PHARMACY B", Status = true });
      db.People.AddRange(
        new Person { PersonId = 10, LastName = "SANTOS", FirstName = "MARIA", Status = true, DateCreated = new DateTime(2026, 11, 3) },
        new Person { PersonId = 11, LastName = "CRUZ", FirstName = "JUAN", Status = true, DateCreated = new DateTime(2026, 10, 3) });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, CreditorId = 1, Status = true, Amount = 1000m, Date = new DateTime(2026, 11, 2), RefNo = "A-1", AssistanceCode = "A", PatientPersonId = 10 },
        new Transaction { TransactionId = 2, CreditorId = 1, Status = true, Amount = 5000m, Date = new DateTime(2026, 11, 5), RefNo = "C-2", AssistanceCode = "C" },
        new Transaction { TransactionId = 3, CreditorId = 2, Status = false, Amount = 300m, Date = new DateTime(2026, 11, 8), RefNo = "OLD", AssistanceCode = "A " },
        new Transaction { TransactionId = 4, CreditorId = 2, Status = false, Amount = 9999m, Date = new DateTime(2026, 11, 9), RefNo = "DELETED", CreatedBy = userId },
        new Transaction { TransactionId = 5, CreditorId = 2, Status = true, Amount = 700m, Date = new DateTime(2026, 10, 20), RefNo = "OCT" },
        new Transaction { TransactionId = 6, CreditorId = 1, Status = true, Amount = 50m, Date = new DateTime(2026, 1, 15), RefNo = "JAN" },
        new Transaction { TransactionId = 7, CreditorId = 1, Status = true, Amount = 80m, Date = new DateTime(2025, 11, 15), RefNo = "LAST-YEAR" });
      await db.SaveChangesAsync();
      // Assistance has no key in the model, so it can only be seeded with SQL
      await db.Database.ExecuteSqlRawAsync(
        "INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES (1, 'A', 'AICS'), (2, 'C', 'IHF-MEDICAL')");
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  [Fact]
  public async Task ThisMonth_TotalsComparedWithLastMonth_AndTheYear()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var d = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard");

    Assert.Equal((2026, 11), (d!.Year, d.Month));
    Assert.Equal(3, d.Claims);
    Assert.Equal(6300m, d.Amount);
    Assert.Equal(1, d.PreviousClaims);
    Assert.Equal(700m, d.PreviousAmount);
    Assert.Equal(5, d.YearClaims);
    Assert.Equal(7050m, d.YearAmount);
    Assert.Equal(1, d.NewPersons);
  }

  [Fact]
  public async Task ProgramsCreditorsTrendAndRecent()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var d = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard");

    Assert.Equal([new DashboardProgramDto("C", "IHF-MEDICAL", 1, 5000m), new DashboardProgramDto("A", "AICS", 2, 1300m)], d!.ByProgram);
    Assert.Equal([new DashboardCreditorDto(1, "HOSPITAL A", 2, 6000m), new DashboardCreditorDto(2, "PHARMACY B", 1, 300m)], d.TopCreditors);

    Assert.Equal(12, d.Trend.Count);
    Assert.Equal((2025, 12), (d.Trend[0].Year, d.Trend[0].Month));
    Assert.Equal(new DashboardMonthDto(2026, 1, 1, 50m), d.Trend[1]);
    Assert.Equal(new DashboardMonthDto(2026, 11, 3, 6300m), d.Trend[11]);
    Assert.Equal(0, d.Trend[5].Claims);

    Assert.Equal(["OLD", "C-2", "A-1"], d.Recent.Select(r => r.RefNo));
    var first = d.Recent[2];
    Assert.Equal("SANTOS, MARIA", first.PatientName);
    Assert.Equal("AICS", first.Program);
    Assert.Equal("HOSPITAL A", first.CreditorName);
  }

  [Fact]
  public async Task OtherMonths_AndBadInput()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var october = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard?year=2026&month=10");
    Assert.Equal(1, october!.Claims);
    Assert.Equal(1, october.NewPersons);
    Assert.Equal(["OCT"], october.Recent.Select(r => r.RefNo));

    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard?year=2026&month=13")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/dashboard")).StatusCode);
  }
}
