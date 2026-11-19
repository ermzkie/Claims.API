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
    var userId = await CreateUserAsync(app, "fl", Pw, "Social Worker");
    await CreateUserAsync(app, "admin", Pw, "Administrator");
    await using (var db = app.CreateDbContext())
    {
      db.Sexes.AddRange(new Sex { SexId = 1, SexDesc = "Male" }, new Sex { SexId = 2, SexDesc = "Female" });
      db.Creditors.AddRange(
        new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true },
        new Creditor { CreditorId = 2, CreditorName = "PHARMACY B", Status = true });
      db.People.AddRange(
        new Person
        {
          PersonId = 10, LastName = "SANTOS", FirstName = "MARIA", Status = true, DateCreated = new DateTime(2026, 11, 3),
          SexId = 2, Municipality = "Koronadal ", Birthdate = new DateOnly(1960, 5, 1), IsSeniorCitizen = true,
        },
        new Person
        {
          PersonId = 11, LastName = "CRUZ", FirstName = "JUAN", Status = true, DateCreated = new DateTime(2026, 10, 3),
          SexId = 1, Birthdate = new DateOnly(2001, 4, 2), IsPwd = true,
        });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, CreditorId = 1, Status = true, Amount = 1000m, Date = new DateTime(2026, 11, 2), RefNo = "A-1", AssistanceCode = "A", PatientPersonId = 10 },
        new Transaction { TransactionId = 2, CreditorId = 1, Status = true, Amount = 5000m, Date = new DateTime(2026, 11, 5), RefNo = "C-2", AssistanceCode = "C", PatientPersonId = 11 },
        new Transaction { TransactionId = 3, CreditorId = 2, Status = false, Amount = 300m, Date = new DateTime(2026, 11, 8), RefNo = "OLD", AssistanceCode = "A " },
        new Transaction { TransactionId = 4, CreditorId = 2, Status = false, Amount = 9999m, Date = new DateTime(2026, 11, 9), RefNo = "DELETED", CreatedBy = userId },
        new Transaction { TransactionId = 5, CreditorId = 2, Status = true, Amount = 700m, Date = new DateTime(2026, 10, 20), RefNo = "OCT", PatientPersonId = 11 },
        new Transaction { TransactionId = 6, CreditorId = 1, Status = true, Amount = 50m, Date = new DateTime(2026, 1, 15), RefNo = "JAN" },
        new Transaction { TransactionId = 7, CreditorId = 1, Status = true, Amount = 80m, Date = new DateTime(2025, 11, 15), RefNo = "LAST-YEAR" },
        new Transaction { TransactionId = 8, CreditorId = 1, Status = true, Amount = 200m, Date = new DateTime(2026, 8, 1), RefNo = "WAIT", AssistanceCode = "A", PatientPersonId = 11, CreatedBy = userId, IsReleased = false },
        new Transaction { TransactionId = 9, CreditorId = 1, Status = true, Amount = 400m, Date = new DateTime(2026, 8, 2), RefNo = "RELEASED", CreatedBy = userId, IsReleased = true });
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
    Assert.Equal(7, d.YearClaims);
    Assert.Equal(7650m, d.YearAmount);
    Assert.Equal(1, d.NewPersons);
  }

  [Fact]
  public async Task ProgramsCreditorsTrendAndWaiting()
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

    // only claims made here and not yet released; old ones have no release status
    Assert.Equal(1, d.WaitingClaims);
    Assert.Equal(200m, d.WaitingAmount);
    var waiting = Assert.Single(d.Waiting);
    Assert.Equal("WAIT", waiting.RefNo);
    Assert.Equal("CRUZ, JUAN", waiting.PatientName);
    Assert.Equal("AICS", waiting.Program);
    Assert.Equal("HOSPITAL A", waiting.CreditorName);
  }

  [Fact]
  public async Task OtherMonths_AndBadInput()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var october = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard?year=2026&month=10");
    Assert.Equal(1, october!.Claims);
    Assert.Equal(1, october.NewPersons);
    Assert.Equal(1, october.WaitingClaims);

    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard?year=2026&month=13")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/dashboard")).StatusCode);
  }

  [Fact]
  public async Task Beneficiaries_ThisMonth()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var b = await client.GetFromJsonAsync<DashboardBeneficiariesDto>("/api/dashboard/beneficiaries");

    Assert.Equal("month", b!.Period);
    Assert.Equal(3, b.Claims);
    Assert.Equal(2, b.Beneficiaries);
    Assert.Equal(1, b.ClaimsWithoutPatient);
    // Juan also had a claim in October
    Assert.Equal((1, 1), (b.FirstTime, b.Returning));
    Assert.Equal([new DashboardGroupDto("KORONADAL", 1, 1, 1000m), new DashboardGroupDto("Not recorded", 1, 1, 5000m)], b.Municipalities);
    Assert.Equal(["Female", "Male"], b.Sexes.Select(s => s.Name));
    Assert.Equal([new DashboardGroupDto("18-29", 1, 1, 5000m), new DashboardGroupDto("60 and over", 1, 1, 1000m)], b.AgeGroups);
    Assert.Equal(
      [
        new DashboardGroupDto("Senior citizen", 1, 1, 1000m),
        new DashboardGroupDto("PWD", 1, 1, 5000m),
        new DashboardGroupDto("Solo parent", 0, 0, 0m),
        new DashboardGroupDto("4Ps", 0, 0, 0m),
      ],
      b.Sectors);
  }

  [Fact]
  public async Task Beneficiaries_YearSoFar_AndBadPeriod()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var b = await client.GetFromJsonAsync<DashboardBeneficiariesDto>("/api/dashboard/beneficiaries?period=year");

    Assert.Equal("year", b!.Period);
    Assert.Equal(7, b.Claims);
    Assert.Equal(2, b.Beneficiaries);
    Assert.Equal((2, 0), (b.FirstTime, b.Returning));
    Assert.Equal(new DashboardGroupDto("18-29", 1, 3, 5900m), b.AgeGroups[0]);

    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard/beneficiaries?period=week")).StatusCode);
  }

  [Fact]
  public async Task Budget_AdminsOnly_AndFailsSoftly()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard/budget")).StatusCode);

    // the test database has no finance procedure, like when the finance server is down
    var admin = await LoginAsAsync(app, "admin", Pw);
    var response = await admin.GetAsync("/api/dashboard/budget?year=2026");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.Contains("not available", await response.Content.ReadAsStringAsync());
  }
}
