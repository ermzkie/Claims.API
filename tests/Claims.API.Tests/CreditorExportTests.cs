using System.Net;
using System.Net.Http.Json;
using System.Text;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CreditorExportTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Administrator");
    await using (var db = app.CreateDbContext())
    {
      db.Creditors.Add(new Creditor { CreditorId = 1, CreditorName = "DR. ROGELIO M. GARCIA MEMORIAL HOSPITAL", Status = true });
      db.People.AddRange(
        new Person { PersonId = 10, LastName = "PEÑA", FirstName = "MARIA", Status = true },
        new Person { PersonId = 11, LastName = "=HYPERLINK(\"x\")", FirstName = "BAD, \"NAME\"", Status = true });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, CreditorId = 1, Status = true, Amount = 1000.5m, Date = new DateTime(2026, 10, 5), RefNo = "A-1", PatientPersonId = 10, AssistanceCode = "A" },
        new Transaction { TransactionId = 2, CreditorId = 1, Status = true, Amount = 250m, Date = new DateTime(2026, 10, 20), RefNo = "C-2", PatientPersonId = 11, AssistanceCode = "C" },
        new Transaction { TransactionId = 3, CreditorId = 1, Status = false, Amount = 75m, Date = new DateTime(2026, 11, 2), RefNo = "OLD-3" },
        new Transaction { TransactionId = 4, CreditorId = 1, Status = false, Amount = 9999m, Date = new DateTime(2026, 11, 3), RefNo = "DELETED", CreatedBy = userId },
        new Transaction { TransactionId = 5, CreditorId = 1, Status = true, Amount = 40m, Date = new DateTime(2025, 1, 1), RefNo = "A-5", AssistanceCode = "A" });
      await db.SaveChangesAsync();
      // Assistance has no key in the model, so it can only be seeded with SQL
      await db.Database.ExecuteSqlRawAsync(
        "INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES (1, 'A', 'AICS'), (2, 'C', 'IHF-MEDICAL')");
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  [Fact]
  public async Task Export_IsACsvOfTheFilteredClaims()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var response = await client.GetAsync("/api/creditor/1/claims/export?from=2026-10-01&to=2026-11-30&sort=date&dir=asc");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
    Assert.Equal("DR-ROGELIO-M-GARCIA-MEMORIAL-HOSPITAL_2026-10-01_to_2026-11-30.csv",
      response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
    var bytes = await response.Content.ReadAsByteArrayAsync();
    Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
    var lines = Encoding.UTF8.GetString(bytes[3..]).TrimEnd().Split(Environment.NewLine);
    Assert.Equal(
      [
        "Ref No,Date,Patient,Program Code,Program,Amount",
        "\"A-1\",\"2026-10-05\",\"PEÑA, MARIA\",\"A\",\"AICS\",1000.50",
        "\"C-2\",\"2026-10-20\",\"'=HYPERLINK(\"\"x\"\"), BAD, \"\"NAME\"\"\",\"C\",\"IHF-MEDICAL\",250.00",
        "\"OLD-3\",\"2026-11-02\",,,,75.00",
      ],
      lines);
  }

  [Fact]
  public async Task Export_UsesTheSameFiltersAsTheList()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var csv = await client.GetStringAsync("/api/creditor/1/claims/export?program=A&sort=amount&dir=desc");
    var list = await client.GetFromJsonAsync<CreditorClaimsPageDto>("/api/creditor/1/claims?program=A&sort=amount&dir=desc");

    Assert.Equal(list!.Data.Select(c => c.RefNo), csv.TrimEnd().Split(Environment.NewLine).Skip(1).Select(l => l.Split(',')[0].Trim('"', '﻿')));
    Assert.Equal(HttpStatusCode.BadRequest,
      (await client.GetAsync("/api/creditor/1/claims/export?from=2026-12-01&to=2026-01-01")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/99/claims/export")).StatusCode);
  }

  [Fact]
  public async Task Months_GiveClaimsAndAmountPerMonth()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var months = await client.GetFromJsonAsync<List<CreditorMonthDto>>("/api/creditor/1/months?year=2026");

    Assert.Equal([new CreditorMonthDto(10, 2, 1250.5m), new CreditorMonthDto(11, 1, 75m)], months);
    Assert.Empty((await client.GetFromJsonAsync<List<CreditorMonthDto>>("/api/creditor/1/months?year=2024"))!);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/99/months?year=2026")).StatusCode);
  }
}
