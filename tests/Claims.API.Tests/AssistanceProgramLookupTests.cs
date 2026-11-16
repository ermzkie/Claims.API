using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Lookups;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class AssistanceProgramLookupTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  [Fact]
  public async Task Programs_OneEntryPerCode_SortedByCode()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      // Assistance has no key in the model, so it can only be seeded with SQL
      await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO Assistance (AssistanceID, AssistanceCode, AssistanceType) VALUES
          (1, 'C', 'IHF-MEDICAL'), (2, 'A', 'AICS'), (3, 'A', 'AICS'),
          (4, 'c ', 'IHF-MEDICAL'), (5, NULL, 'NO CODE'), (6, 'Z', NULL)
        """);
    }
    using var client = await LoginAsAsync(app, "fl", Pw);

    var programs = await client.GetFromJsonAsync<List<AssistanceProgramDto>>("/api/lookups/assistance-programs");

    Assert.Equal([new AssistanceProgramDto("A", "AICS"), new AssistanceProgramDto("C", "IHF-MEDICAL")], programs);
  }

  [Fact]
  public async Task Programs_NeedLogin()
  {
    await using var app = new ClaimsApiFactory();
    Assert.Equal(HttpStatusCode.Unauthorized,
      (await app.CreateClient().GetAsync("/api/lookups/assistance-programs")).StatusCode);
  }
}
