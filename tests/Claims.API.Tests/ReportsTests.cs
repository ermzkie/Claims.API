using System.Net;
using System.Net.Http.Json;
using Claims.API.Controllers;
using Claims.API.DTOs.Reports;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class ReportsTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Front, int PersonId, int UserId)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "front", Password, "Social Worker");
    await using var db = app.CreateDbContext();
    var person = new Person { FirstName = "MARIA", LastName = "SANTOS", Status = true };
    db.People.Add(person);
    await db.SaveChangesAsync();
    return (app, await LoginAsAsync(app, "front", Password), person.PersonId, userId);
  }

  [Fact]
  public async Task Reports_CountOldSystemClaims_ButNotCancelledOnes()
  {
    var (app, front, personId, userId) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        var day = new DateTime(2025, 5, 2);
        db.Transactions.AddRange(
          // old system: Status 0 but never cancelled here
          new Transaction { PatientPersonId = personId, Date = day, Amount = 1000m, AssistanceCode = "MED", Status = false },
          new Transaction { PatientPersonId = personId, Date = day, Amount = 500m, AssistanceCode = "MED", Status = true, CreatedBy = userId },
          // cancelled in this system
          new Transaction { PatientPersonId = personId, Date = day, Amount = 900m, AssistanceCode = "MED", Status = false, CreatedBy = userId });
        await db.SaveChangesAsync();
      }

      var rows = await front.GetFromJsonAsync<List<TransactionReportDto>>("/api/reports/transactions");
      Assert.Equal(2, rows!.Count);
      Assert.Equal(1500m, rows.Sum(r => r.Amount));
      Assert.All(rows, r => Assert.True(r.Status));

      var clients = await front.GetFromJsonAsync<List<PerennialClientReportDto>>("/api/reports/perennial-clients");
      var client = Assert.Single(clients!);
      Assert.Equal(2, client.AvailmentCount);
      Assert.Equal(1500m, client.TotalAmount);
    }
  }

  [Fact]
  public async Task TransactionReport_TooManyClaims_AsksForNarrowerDates()
  {
    var (app, front, personId, _) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.Transactions.AddRange(Enumerable.Range(0, ReportsController.MaxReportRows + 1).Select(i => new Transaction
        {
          PatientPersonId = personId, Date = new DateTime(2024, 1, 1).AddHours(i), Amount = 1m, Status = true,
        }));
        await db.SaveChangesAsync();
      }

      var all = await front.GetAsync("/api/reports/transactions");
      Assert.Equal(HttpStatusCode.BadRequest, all.StatusCode);
      Assert.Contains("Narrow the dates", await all.Content.ReadAsStringAsync());

      var oneDay = await front.GetFromJsonAsync<List<TransactionReportDto>>(
        "/api/reports/transactions?dateFrom=2024-01-01&dateTo=2024-01-01");
      Assert.Equal(24, oneDay!.Count);
    }
  }

  [Fact]
  public async Task Balance_IsAdminsOnly()
  {
    var (app, front, _, _) = await StartAsync();
    await using (app)
    {
      var response = await front.GetAsync("/api/balance/annual?year=2026");
      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
  }
}
