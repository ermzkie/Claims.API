using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class ReferenceDataTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private record Item(int Id, string? Code, string Name, int UsedBy);

  private static async Task<(ClaimsApiFactory App, HttpClient Admin)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "admin", Pw, "Administrator");
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.CivilStatuses.AddRange(
        new CivilStatus { CivilStatusId = 1, CivilStatusCode = "S", CivilStatusDesc = "Single" },
        new CivilStatus { CivilStatusId = 2, CivilStatusCode = "M", CivilStatusDesc = "Married" });
      db.NameSuffixes.Add(new NameSuffix { NameSuffixId = 1, NameSuffixCode = "Jr." });
      db.ModeOfAssistances.Add(new ModeOfAssistance { ModeDesc = "Cash" });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "admin", Pw));
  }

  private static async Task<string> Message(HttpResponseMessage r)
  {
    using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("message").GetString()!;
  }

  [Fact]
  public async Task Lists_AreAdminOnly()
  {
    var (app, _) = await StartAsync();
    await using (app)
    {
      var frontliner = await LoginAsAsync(app, "fl", Pw);
      Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.GetAsync("/api/reference-data")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden,
        (await frontliner.PostAsJsonAsync("/api/reference-data/civil-statuses", new { name = "X" })).StatusCode);
    }
  }

  [Fact]
  public async Task Index_NamesTheFiveLists()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      using var doc = JsonDocument.Parse(await admin.GetStringAsync("/api/reference-data"));
      var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()).ToList();

      Assert.Equal(
        ["name-suffixes", "civil-statuses", "relationships", "sources-of-assistance", "modes-of-assistance"],
        keys);
    }
  }

  [Fact]
  public async Task Get_ReturnsItemsWithUsage_404ForUnknownList()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.People.AddRange(
          new Person { PersonId = 1, FirstName = "A", LastName = "B", CivilStatusId = 2 },
          new Person { PersonId = 2, FirstName = "C", LastName = "D", CivilStatusId = 2 });
        await db.SaveChangesAsync();
      }

      var items = await admin.GetFromJsonAsync<List<Item>>("/api/reference-data/civil-statuses");

      Assert.Equal([new Item(1, "S", "Single", 0), new Item(2, "M", "Married", 2)], items);
      Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/reference-data/nope")).StatusCode);
    }
  }

  [Fact]
  public async Task Add_PicksNextId_ForHandNumberedTables()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var response = await admin.PostAsJsonAsync("/api/reference-data/civil-statuses", new { code = " W ", name = " Widowed " });

      Assert.Equal(HttpStatusCode.Created, response.StatusCode);
      Assert.Equal(new Item(3, "W", "Widowed", 0), await response.Content.ReadFromJsonAsync<Item>());
      await using var db = app.CreateDbContext();
      Assert.Equal("Widowed", (await db.CivilStatuses.SingleAsync(c => c.CivilStatusId == 3)).CivilStatusDesc);
    }
  }

  [Fact]
  public async Task Add_FirstValue_StartsAtOne_AndSuffixUsesNameAsItsOnlyColumn()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var relationship = await admin.PostAsJsonAsync("/api/reference-data/relationships", new { name = "Spouse" });
      var suffix = await admin.PostAsJsonAsync("/api/reference-data/name-suffixes", new { name = "Sr." });

      Assert.Equal(new Item(1, null, "Spouse", 0), await relationship.Content.ReadFromJsonAsync<Item>());
      Assert.Equal(new Item(2, null, "Sr.", 0), await suffix.Content.ReadFromJsonAsync<Item>());
    }
  }

  [Fact]
  public async Task Add_IdentityTable_LetsTheDatabaseNumberIt()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var response = await admin.PostAsJsonAsync("/api/reference-data/modes-of-assistance", new { name = "Guarantee Letter" });

      var item = await response.Content.ReadFromJsonAsync<Item>();
      Assert.Equal("Guarantee Letter", item!.Name);
      Assert.True(item.Id > 1);
    }
  }

  [Theory]
  [InlineData("", "Name is required.")]
  [InlineData("married", "That value is already in the list.")]
  public async Task Add_Validates(string name, string message)
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var response = await admin.PostAsJsonAsync("/api/reference-data/civil-statuses", new { name });

      Assert.Equal(name == "" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, response.StatusCode);
      Assert.Equal(message, await Message(response));
    }
  }

  [Fact]
  public async Task Add_RejectsTooLong()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var response = await admin.PostAsJsonAsync("/api/reference-data/civil-statuses", new { name = new string('x', 51) });

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Equal("Name can be at most 50 characters.", await Message(response));
    }
  }

  [Fact]
  public async Task Rename_Works_EvenWhenInUse_ButNotToADuplicate()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.People.Add(new Person { PersonId = 3, FirstName = "A", LastName = "B", CivilStatusId = 2 });
        await db.SaveChangesAsync();
      }

      var renamed = await admin.PutAsJsonAsync("/api/reference-data/civil-statuses/2", new { code = "M", name = "Married (civil)" });
      var duplicate = await admin.PutAsJsonAsync("/api/reference-data/civil-statuses/2", new { name = "SINGLE" });
      var missing = await admin.PutAsJsonAsync("/api/reference-data/civil-statuses/99", new { name = "X" });

      Assert.Equal(new Item(2, "M", "Married (civil)", 1), await renamed.Content.ReadFromJsonAsync<Item>());
      Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
      Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
  }

  [Fact]
  public async Task Delete_OnlyWhenUnused()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.People.AddRange(
          new Person { PersonId = 4, FirstName = "A", LastName = "B", NameSuffixId = 1 },
          new Person { PersonId = 5, FirstName = "C", LastName = "D", CivilStatusId = 2 });
        await db.SaveChangesAsync();
      }

      var inUse = await admin.DeleteAsync("/api/reference-data/name-suffixes/1");
      var unused = await admin.DeleteAsync("/api/reference-data/civil-statuses/1");

      Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
      Assert.Equal("Can't delete \"Jr.\": 1 record uses it. You can rename it instead.", await Message(inUse));
      Assert.Equal(HttpStatusCode.NoContent, unused.StatusCode);
      await using var check = app.CreateDbContext();
      Assert.False(await check.CivilStatuses.AnyAsync(c => c.CivilStatusId == 1));
    }
  }

  [Fact]
  public async Task Suffix_UsageCountsPeopleAndUsers()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await using (var db = app.CreateDbContext())
      {
        db.People.Add(new Person { PersonId = 6, FirstName = "A", LastName = "B", NameSuffixId = 1 });
        (await db.Users.SingleAsync(u => u.Username == "fl")).NameSuffixId = 1;
        await db.SaveChangesAsync();
      }

      var items = await admin.GetFromJsonAsync<List<Item>>("/api/reference-data/name-suffixes");

      Assert.Equal(2, items!.Single().UsedBy);
    }
  }
}
