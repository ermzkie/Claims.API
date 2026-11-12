using System.Net;
using System.Text.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class PersonSearchTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Sexes.AddRange(
        new Sex { SexId = 1, SexCode = "M", SexDesc = "Male" },
        new Sex { SexId = 2, SexCode = "F", SexDesc = "Female" });
      db.Provinces.Add(new Province { ProvinceId = 1, ProvinceName = "South Cotabato", ProvinceCode = "63" });
      db.CityMunicipalities.AddRange(
        new CityMunicipality { CityMunicipalityId = 10, CityMunicipalityName = "City of Koronadal", CityMunicipalityCode = "06", ProvinceId = 1 },
        new CityMunicipality { CityMunicipalityId = 11, CityMunicipalityName = "Banga", CityMunicipalityCode = "04", ProvinceId = 1 });
      db.Barangays.AddRange(
        new Barangay { BarangayId = 100, BarangayName = "Zone III (Pob.)", CityMunicipalityId = 10 },
        new Barangay { BarangayId = 101, BarangayName = "Morales", CityMunicipalityId = 10 },
        new Barangay { BarangayId = 200, BarangayName = "Reyes (Pob.)", CityMunicipalityId = 11 });
      db.People.AddRange(
        new Person { PersonId = 5001, FirstName = "MARIA", LastName = "SANTOS", SexId = 2, BarangayId = 100, IsSeniorCitizen = true, Is4P = true, ContactNo = "0917 123 4567" },
        new Person { PersonId = 5002, FirstName = "JOSE", LastName = "SANTOS", SexId = 1, BarangayId = 100, IsSeniorCitizen = true },
        new Person { PersonId = 5003, FirstName = "ANA", LastName = "REYES", SexId = 2, BarangayId = 101, IsPwd = true, ContactNo = "+63 (918) 555-0000" },
        new Person { PersonId = 5004, FirstName = "PEDRO", LastName = "CRUZ", SexId = 1, BarangayId = 200, Is4P = true },
        new Person { PersonId = 5005, FirstName = "LIZA", LastName = "SANTOS", SexId = 2, BarangayId = 200, IsSoloParent = true });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  private static async Task<int[]> Ids(HttpClient client, string query)
  {
    var response = await client.GetAsync($"/api/persons?{query}");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return [.. doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("personId").GetInt32()).Order()];
  }

  [Theory]
  [InlineData("search=5003", new[] { 5003 })]
  [InlineData("search=0917", new[] { 5001 })]
  [InlineData("search=09171234567", new[] { 5001 })]
  [InlineData("search=0917-123", new[] { 5001 })]
  [InlineData("search=918 555", new[] { 5003 })]
  [InlineData("search=santos", new[] { 5001, 5002, 5005 })]
  public async Task SearchBox_FindsIdContactOrName(string query, int[] expected)
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      Assert.Equal(expected, await Ids(client, query));
    }
  }

  [Theory]
  [InlineData("cityMunicipalityId=10", new[] { 5001, 5002, 5003 })]
  [InlineData("barangayId=100", new[] { 5001, 5002 })]
  [InlineData("cityMunicipalityId=11&barangayId=200", new[] { 5004, 5005 })]
  [InlineData("sexId=2", new[] { 5001, 5003, 5005 })]
  [InlineData("sector=senior", new[] { 5001, 5002 })]
  [InlineData("sector=senior&sector=4ps", new[] { 5001 })]
  [InlineData("sector=pwd", new[] { 5003 })]
  [InlineData("sector=solo-parent", new[] { 5005 })]
  [InlineData("search=santos&sexId=2&cityMunicipalityId=11", new[] { 5005 })]
  [InlineData("search=santos&sector=senior&barangayId=100", new[] { 5001, 5002 })]
  public async Task Filters_CombineWithEachOtherAndTheSearchText(string query, int[] expected)
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      Assert.Equal(expected, await Ids(client, query));
    }
  }

  [Fact]
  public async Task UnknownSector_Returns400()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/persons?sector=rich")).StatusCode);
    }
  }
}
