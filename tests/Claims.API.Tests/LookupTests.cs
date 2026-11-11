using System.Net;
using System.Net.Http.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class LookupTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private record Item(int Id, string? Code, string Name);

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    return (app, await LoginAsAsync(app, "fl", Pw));
  }

  private static async Task SeedAddressesAsync(ClaimsApiFactory app)
  {
    await using var db = app.CreateDbContext();
    db.Provinces.AddRange(
      new Province { ProvinceId = 1, ProvinceName = "South Cotabato", ProvinceCode = "63" },
      new Province { ProvinceId = 2, ProvinceName = "Cotabato", ProvinceCode = "47" });
    db.CityMunicipalities.AddRange(
      new CityMunicipality { CityMunicipalityId = 10, CityMunicipalityName = "Polomolok", CityMunicipalityCode = "06", ProvinceId = 1 },
      new CityMunicipality { CityMunicipalityId = 11, CityMunicipalityName = "Koronadal", CityMunicipalityCode = "06", ProvinceId = 1 },
      new CityMunicipality { CityMunicipalityId = 20, CityMunicipalityName = "Kidapawan", CityMunicipalityCode = "01", ProvinceId = 2 });
    db.Barangays.AddRange(
      new Barangay { BarangayId = 100, BarangayName = "Zone IV", BarangayCode = "004", CityMunicipalityId = 11 },
      new Barangay { BarangayId = 101, BarangayName = "Morales", BarangayCode = "001", CityMunicipalityId = 11 },
      new Barangay { BarangayId = 102, BarangayName = "Cannery Site", CityMunicipalityId = 10 });
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task NoToken_Returns401()
  {
    await using var app = new ClaimsApiFactory();
    var response = await app.CreateClient().GetAsync("/api/lookups/sexes");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Sexes_ReturnsIdCodeName_InIdOrder()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.Sexes.AddRange(
          new Sex { SexId = 2, SexCode = "F", SexDesc = "Female" },
          new Sex { SexId = 1, SexCode = "M", SexDesc = "Male" });
        await db.SaveChangesAsync();
      }

      var items = await client.GetFromJsonAsync<List<Item>>("/api/lookups/sexes");

      Assert.Equal([new Item(1, "M", "Male"), new Item(2, "F", "Female")], items);
    }
  }

  [Fact]
  public async Task ModesAndSources_Return200()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.ModeOfAssistances.Add(new ModeOfAssistance { ModeId = 2, ModeDesc = "Cash" });
        db.SourceOfAssistances.Add(new SourceOfAssistance { SourceId = 1, SourceDesc = "AICS" });
        await db.SaveChangesAsync();
      }

      Assert.Equal([new Item(2, null, "Cash")],
        await client.GetFromJsonAsync<List<Item>>("/api/lookups/modes-of-assistance"));
      Assert.Equal([new Item(1, null, "AICS")],
        await client.GetFromJsonAsync<List<Item>>("/api/lookups/sources-of-assistance"));
    }
  }

  [Theory]
  [InlineData("/api/lookups/civil-statuses")]
  [InlineData("/api/lookups/name-suffixes")]
  [InlineData("/api/lookups/relationship-statuses")]
  public async Task EmptyTables_ReturnEmptyList(string url)
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      Assert.Empty((await client.GetFromJsonAsync<List<Item>>(url))!);
    }
  }

  [Fact]
  public async Task Provinces_SortedByName()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      await SeedAddressesAsync(app);

      var items = await client.GetFromJsonAsync<List<Item>>("/api/lookups/provinces");

      Assert.Equal(["Cotabato", "South Cotabato"], items!.Select(i => i.Name));
    }
  }

  [Fact]
  public async Task Cities_FilteredByProvince_SortedByName()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      await SeedAddressesAsync(app);

      var items = await client.GetFromJsonAsync<List<Item>>("/api/lookups/cities?provinceId=1");

      Assert.Equal([11, 10], items!.Select(i => i.Id));
    }
  }

  [Fact]
  public async Task Barangays_FilteredByCity_SortedByName()
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      await SeedAddressesAsync(app);

      var items = await client.GetFromJsonAsync<List<Item>>("/api/lookups/barangays?cityMunicipalityId=11");

      Assert.Equal([new Item(101, "001", "Morales"), new Item(100, "004", "Zone IV")], items);
    }
  }

  [Theory]
  [InlineData("/api/lookups/cities")]
  [InlineData("/api/lookups/barangays")]
  public async Task Cities_And_Barangays_RequireParent(string url)
  {
    var (app, client) = await StartAsync();
    await using (app)
    {
      var response = await client.GetAsync(url);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
      Assert.Contains("required", body!["message"]);
    }
  }
}
