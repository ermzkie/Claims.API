using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.DTOs.Creditor;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CreditorClaimsBrowserTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Users.AddRange(
        new User { UserId = 50, Username = "sw", Password = "x", Lastname = "REYES", Firstname = "ANA", Status = true },
        new User { UserId = 51, Username = "enc", Password = "x", Lastname = "CRUZ", Firstname = "PEDRO", Status = true });
      db.Officers.Add(new Officer { Idnumber = 123, Name = "OLD WORKER", Position = "SWO II", Password = "legacy-secret" });
      db.Creditors.AddRange(
        new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true },
        new Creditor { CreditorId = 2, CreditorName = "PHARMACY B", Status = true });
      db.Sexes.Add(new Sex { SexId = 2, SexCode = "F", SexDesc = "Female" });
      db.People.AddRange(
        new Person { PersonId = 10, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", SexId = 2, Barangay = "ZONE III", Municipality = "KORONADAL", Status = true },
        new Person { PersonId = 11, LastName = "DELA CRUZ", FirstName = "JUAN", Status = true },
        new Person { PersonId = 12, LastName = "SANTOS", FirstName = "JOSE", Status = true });
      db.NatureOfServices.Add(new NatureOfService { NatureId = 1, NatureDesc = "Medical", Status = true });
      db.Transactions.AddRange(
        new Transaction
        {
          TransactionId = 101, CreditorId = 1, Status = true, Amount = 1000m, Date = new DateTime(2026, 10, 1),
          RefNo = "A-10-01-26-0001", PatientPersonId = 10, AssistanceCode = "A", SocialWorkerUserId = 50,
          RepresentativePersonId = 11, IsReleased = true, SocialWorkerAssessment = "Needs help", Remarks = "OK",
          CreatedBy = 51, DateCreated = new DateTime(2026, 10, 1, 9, 0, 0), LastUpdatedBy = 50,
        },
        new Transaction
        {
          TransactionId = 102, CreditorId = 1, Status = true, Amount = 5000m, Date = new DateTime(2026, 10, 2),
          RefNo = "C-10-02-26-0002", PatientPersonId = 11, AssistanceCode = "C", SocialWorkerId = "123",
          Representative = "  wife  ",
        },
        new Transaction { TransactionId = 103, CreditorId = 1, Status = true, Amount = 300m, Date = new DateTime(2026, 11, 5), RefNo = "A-11-05-26-0003", PatientPersonId = 12, AssistanceCode = "A " },
        new Transaction { TransactionId = 104, CreditorId = 1, Status = false, CreatedBy = 51, Amount = 7m, Date = new DateTime(2026, 11, 6), RefNo = "A-DELETED", PatientPersonId = 10, AssistanceCode = "A" },
        new Transaction { TransactionId = 201, CreditorId = 2, Status = true, Amount = 75m, Date = new DateTime(2026, 11, 1), RefNo = "B-1", PatientPersonId = 10, AssistanceCode = "A" });
      db.TransactionServices.Add(new TransactionService { TransactionId = 101, NatureId = 1 });
      await db.SaveChangesAsync();
      // Assistance has no key in the model, so it can only be seeded with SQL.
      await db.Database.ExecuteSqlRawAsync(
        "INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES (1, 'A', 'AICS'), (2, 'C', 'IHF-MEDICAL')");
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  private static Task<CreditorClaimsPageDto?> Page(HttpClient client, string query) =>
    client.GetFromJsonAsync<CreditorClaimsPageDto>($"/api/creditor/1/claims?{query}");

  [Fact]
  public async Task Search_MatchesRefNoOrEveryWordOfThePatientName()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    Assert.Equal(["C-10-02-26-0002"], (await Page(client, "search=10-02-26"))!.Data.Select(c => c.RefNo));
    Assert.Equal(["A-11-05-26-0003", "A-10-01-26-0001"], (await Page(client, "search=santos"))!.Data.Select(c => c.RefNo));
    Assert.Equal(["A-10-01-26-0001"], (await Page(client, "search=maria santos"))!.Data.Select(c => c.RefNo));
    Assert.Empty((await Page(client, "search=nobody"))!.Data);
  }

  [Fact]
  public async Task ProgramFilter_SortAndTotalAmount_CoverTheFilteredClaims()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var aics = await Page(client, "program=A");
    Assert.Equal(["A-11-05-26-0003", "A-10-01-26-0001"], aics!.Data.Select(c => c.RefNo));
    Assert.Equal(1300m, aics.TotalAmount);

    var byAmount = await Page(client, "sort=amount&dir=desc");
    Assert.Equal([5000m, 1000m, 300m], byAmount!.Data.Select(c => c.Amount));
    Assert.Equal(6300m, byAmount.TotalAmount);

    var october = await Page(client, "from=2026-10-01&to=2026-10-31&sort=date&dir=asc");
    Assert.Equal(["A-10-01-26-0001", "C-10-02-26-0002"], october!.Data.Select(c => c.RefNo));
    Assert.Equal(6000m, october.TotalAmount);

    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/creditor/1/claims?sort=patient")).StatusCode);
  }

  [Fact]
  public async Task Programs_ListCodesUsedByTheCreditor_WithNames()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var programs = await client.GetFromJsonAsync<List<CreditorProgramDto>>("/api/creditor/1/programs");

    Assert.Equal([new CreditorProgramDto("A", "AICS", 2), new CreditorProgramDto("C", "IHF-MEDICAL", 1)], programs);
  }

  [Fact]
  public async Task Claim_ShowsNamesInsteadOfIds()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var c = await client.GetFromJsonAsync<CreditorClaimDetailDto>("/api/creditor/1/claims/101");

    Assert.Equal("A-10-01-26-0001", c!.RefNo);
    Assert.Equal("AICS", c.AssistanceType);
    Assert.Equal(["Medical"], c.NaturesOfService);
    Assert.True(c.IsReleased);
    Assert.Equal(new ClaimPersonDto(10, "SANTOS, MARIA CRUZ", "Female", "ZONE III, KORONADAL"), c.Patient);
    Assert.Equal("DELA CRUZ, JUAN", c.RepresentativeName);
    Assert.Equal("REYES, ANA", c.SocialWorker);
    Assert.Equal("CRUZ, PEDRO", c.CreatedBy);
    Assert.Equal("REYES, ANA", c.LastUpdatedBy);
    Assert.Equal("Needs help", c.SocialWorkerAssessment);
  }

  [Fact]
  public async Task Claim_OldClaims_UseTheLegacyOfficerName_WithoutOtherOfficerColumns()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var response = await client.GetAsync("/api/creditor/1/claims/102");
    var json = await response.Content.ReadAsStringAsync();
    var c = JsonSerializer.Deserialize<CreditorClaimDetailDto>(json, JsonSerializerOptions.Web);

    Assert.Equal("OLD WORKER", c!.SocialWorker);
    Assert.Equal("SWO II", c.SocialWorkerPosition);
    Assert.Equal("wife", c.RepresentativeName);
    Assert.DoesNotContain("legacy-secret", json);
  }

  [Fact]
  public async Task Claim_OfAnotherCreditor_IsNotFound()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/1/claims/201")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/creditor/999/programs")).StatusCode);
  }
}
