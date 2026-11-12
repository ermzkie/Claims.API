using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class PersonTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client, int UserId)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Sexes.AddRange(
        new Sex { SexId = 1, SexCode = "M", SexDesc = "Male" },
        new Sex { SexId = 2, SexCode = "F", SexDesc = "Female" });
      db.Provinces.Add(new Province { ProvinceId = 1, ProvinceName = "South Cotabato", ProvinceCode = "63" });
      db.CityMunicipalities.Add(new CityMunicipality
      {
        CityMunicipalityId = 10, CityMunicipalityName = "City of Koronadal", CityMunicipalityCode = "06", ProvinceId = 1,
      });
      db.Barangays.Add(new Barangay { BarangayId = 100, BarangayName = "Zone III", CityMunicipalityId = 10 });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "fl", Pw), userId);
  }

  private static object NewPerson(string first = "Juan", string last = "Dela Cruz") => new
  {
    firstName = first,
    middleName = "Santos",
    lastName = last,
    sexId = 1,
    barangayId = 100,
    street = "Rizal St.",
    birthdate = "1980-05-17",
    isSeniorCitizen = false,
    contactNo = "09171234567",
    estimatedMonthlyIncome = 8000m,
  };

  private static async Task<JsonElement> Json(HttpResponseMessage r)
  {
    using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    return doc.RootElement.Clone();
  }

  private static async Task<int> SeedLegacyAsync(ClaimsApiFactory app, string first, string last, string? barangay = null)
  {
    await using var db = app.CreateDbContext();
    var p = new Person { FirstName = first, LastName = last, Gender = "Female", Barangay = barangay, Municipality = "Banga" };
    db.People.Add(p);
    await db.SaveChangesAsync();
    return p.PersonId;
  }

  [Fact]
  public async Task Create_SavesPerson_FillsTextColumnsAndAudit()
  {
    var (app, client, userId) = await StartAsync();
    await using (app)
    {
      var response = await client.PostAsJsonAsync("/api/persons", NewPerson());

      Assert.Equal(HttpStatusCode.Created, response.StatusCode);
      var body = await Json(response);
      var id = body.GetProperty("personId").GetInt32();
      Assert.Equal("Male", body.GetProperty("sex").GetString());
      Assert.Equal("Zone III", body.GetProperty("barangay").GetString());
      Assert.Equal("City of Koronadal", body.GetProperty("municipality").GetString());
      Assert.Equal(10, body.GetProperty("cityMunicipalityId").GetInt32());
      Assert.Equal(1, body.GetProperty("provinceId").GetInt32());

      await using var db = app.CreateDbContext();
      var saved = await db.People.AsNoTracking().SingleAsync(p => p.PersonId == id);
      Assert.True(saved.Status);
      Assert.Equal(userId, saved.CreatedBy);
      Assert.Equal("Male", saved.Gender);
      Assert.Equal("Zone III", saved.Barangay);
      Assert.Equal("City of Koronadal", saved.Municipality);
      Assert.Equal(new DateOnly(1980, 5, 17), saved.Birthdate);
    }
  }

  [Theory]
  [InlineData("", "Dela Cruz", "First name is required.")]
  [InlineData("Juan", "  ", "Last name is required.")]
  public async Task Create_RequiresNames(string first, string last, string message)
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      var response = await client.PostAsJsonAsync("/api/persons", NewPerson(first, last));

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Equal(message, (await Json(response)).GetProperty("message").GetString());
    }
  }

  [Fact]
  public async Task Create_RejectsUnknownLookupIds()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      var response = await client.PostAsJsonAsync("/api/persons", new { firstName = "A", lastName = "B", barangayId = 999 });

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Equal("Barangay not found.", (await Json(response)).GetProperty("message").GetString());
    }
  }

  [Fact]
  public async Task Create_RejectsFutureBirthdate_AndNegativeIncome()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      var future = await client.PostAsJsonAsync("/api/persons", new { firstName = "A", lastName = "B", birthdate = "2099-01-01" });
      var income = await client.PostAsJsonAsync("/api/persons", new { firstName = "A", lastName = "B", estimatedMonthlyIncome = -1 });

      Assert.Equal("Birthdate can't be in the future.", (await Json(future)).GetProperty("message").GetString());
      Assert.Equal("Estimated monthly income can't be negative.", (await Json(income)).GetProperty("message").GetString());
    }
  }

  [Fact]
  public async Task Update_ChangesFields_SetsLastUpdated()
  {
    var (app, client, userId) = await StartAsync();
    await using (app)
    {
      var id = (await Json(await client.PostAsJsonAsync("/api/persons", NewPerson()))).GetProperty("personId").GetInt32();

      var response = await client.PutAsJsonAsync($"/api/persons/{id}", new { firstName = "Juana", lastName = "Dela Cruz", sexId = 2 });

      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      await using var db = app.CreateDbContext();
      var saved = await db.People.AsNoTracking().SingleAsync(p => p.PersonId == id);
      Assert.Equal("Juana", saved.FirstName);
      Assert.Equal("Female", saved.Gender);
      Assert.Null(saved.BarangayId);
      Assert.Equal(userId, saved.LastUpdatedBy);
      Assert.NotNull(saved.DateLastUpdated);
    }
  }

  [Fact]
  public async Task Update_KeepsLegacyAddressText_WhenNoBarangayChosen()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      var id = await SeedLegacyAsync(app, "Maria", "Santos", barangay: "Poblacion");

      await client.PutAsJsonAsync($"/api/persons/{id}", new { firstName = "Maria", lastName = "Santos" });

      await using var db = app.CreateDbContext();
      var saved = await db.People.AsNoTracking().SingleAsync(p => p.PersonId == id);
      Assert.Equal("Poblacion", saved.Barangay);
      Assert.Equal("Banga", saved.Municipality);
      Assert.Equal("Female", saved.Gender);
    }
  }

  [Fact]
  public async Task Get_ReturnsPerson_404WhenMissing()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      var id = await SeedLegacyAsync(app, "Maria", "Santos", barangay: "Poblacion");

      var found = await Json(await client.GetAsync($"/api/persons/{id}"));
      var missing = await client.GetAsync("/api/persons/99999");

      Assert.Equal("Maria", found.GetProperty("firstName").GetString());
      Assert.Equal("Female", found.GetProperty("sex").GetString());
      Assert.Equal("Poblacion", found.GetProperty("barangay").GetString());
      Assert.Equal(0, found.GetProperty("family").GetArrayLength());
      Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
  }

  [Fact]
  public async Task Search_MatchesEveryWord_InAnyNamePart_Paged()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      await SeedLegacyAsync(app, "Juan", "Dela Cruz");
      await SeedLegacyAsync(app, "Juana", "Cruz");
      await SeedLegacyAsync(app, "Pedro", "Dela Cruz");
      await SeedLegacyAsync(app, "Maria", "Santos");

      var both = await Json(await client.GetAsync("/api/persons?search=cruz juan"));
      var comma = await Json(await client.GetAsync("/api/persons?search=Dela Cruz, Pedro"));
      var paged = await Json(await client.GetAsync("/api/persons?search=cruz&page=2&pageSize=2"));

      Assert.Equal(2, both.GetProperty("total").GetInt32());
      Assert.Equal(1, comma.GetProperty("total").GetInt32());
      Assert.Equal(3, paged.GetProperty("total").GetInt32());
      Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
    }
  }

  [Fact]
  public async Task Search_SortsByLastThenFirstName()
  {
    var (app, client, _) = await StartAsync();
    await using (app)
    {
      await SeedLegacyAsync(app, "Zed", "Abad");
      await SeedLegacyAsync(app, "Ana", "Bautista");
      await SeedLegacyAsync(app, "Ben", "Abad");

      var items = (await Json(await client.GetAsync("/api/persons?search=a"))).GetProperty("items");

      Assert.Equal(["Ben", "Zed", "Ana"], items.EnumerateArray().Select(i => i.GetProperty("firstName").GetString()));
    }
  }

  [Fact]
  public async Task NoToken_Returns401()
  {
    await using var app = new ClaimsApiFactory();
    Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/persons?search=cruz")).StatusCode);
  }
}
