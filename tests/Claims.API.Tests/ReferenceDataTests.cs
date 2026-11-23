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
  public async Task Index_NamesTheSixLists()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      using var doc = JsonDocument.Parse(await admin.GetStringAsync("/api/reference-data"));
      var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()).ToList();

      Assert.Equal(
        ["name-suffixes", "civil-statuses", "relationships", "sources-of-assistance", "modes-of-assistance", "lanes"],
        keys);
      var lanes = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("key").GetString() == "lanes");
      Assert.True(lanes.GetProperty("hasCode").GetBoolean());
      Assert.Equal(["active", "needsEndorsement"],
        lanes.GetProperty("flags").EnumerateArray().Select(f => f.GetProperty("key").GetString()).ToList());
    }
  }

  private const string LockedLane = "The S, V and R lanes are used by the queue order and can't be renamed to another code or deleted.";

  private static async Task<Dictionary<string, short>> SeedLanesAsync(ClaimsApiFactory app)
  {
    await using var db = app.CreateDbContext();
    var lanes = new[]
    {
      new LaneType { LaneTypeCode = "S", LaneTypeDesc = "Senior Citizen/PWD Lane", Status = true },
      new LaneType { LaneTypeCode = "V", LaneTypeDesc = "Endorsed Cases Lane", Status = true, NeedsEndorsement = true },
      new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane", Status = true },
      new LaneType { LaneTypeCode = "X", LaneTypeDesc = "Extra Lane", Status = true },
    };
    db.LaneTypes.AddRange(lanes);
    await db.SaveChangesAsync();
    return lanes.ToDictionary(l => l.LaneTypeCode!, l => l.LaneTypeId);
  }

  private static async Task<JsonElement> LaneAsync(HttpClient admin, string code)
  {
    var doc = JsonDocument.Parse(await admin.GetStringAsync("/api/reference-data/lanes"));
    return doc.RootElement.EnumerateArray().Single(e => e.GetProperty("code").GetString() == code);
  }

  [Fact]
  public async Task Lanes_ListShowsFlagsAndLocks()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await SeedLanesAsync(app);
      var v = await LaneAsync(admin, "V");
      Assert.True(v.GetProperty("flags").GetProperty("needsEndorsement").GetBoolean());
      Assert.True(v.GetProperty("flags").GetProperty("active").GetBoolean());
      foreach (var code in new[] { "S", "V", "R" })
        Assert.True((await LaneAsync(admin, code)).GetProperty("locked").GetBoolean(), code);
      Assert.False((await LaneAsync(admin, "X")).GetProperty("locked").GetBoolean());
    }
  }

  [Fact]
  public async Task Lanes_AddNewLane()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await SeedLanesAsync(app);
      var r = await admin.PostAsJsonAsync("/api/reference-data/lanes",
        new { code = "ew", name = "Emergency", flags = new { active = true, needsEndorsement = true } });
      Assert.Equal(HttpStatusCode.Created, r.StatusCode);
      var ew = await LaneAsync(admin, "EW");
      Assert.Equal("Emergency", ew.GetProperty("name").GetString());
      Assert.True(ew.GetProperty("flags").GetProperty("active").GetBoolean());
      Assert.True(ew.GetProperty("flags").GetProperty("needsEndorsement").GetBoolean());
    }
  }

  [Theory]
  [InlineData("", "Lane code is 1 to 3 letters.")]
  [InlineData("ABCD", "Lane code is 1 to 3 letters.")]
  [InlineData("E1", "Lane code is 1 to 3 letters.")]
  [InlineData("r", "That code is already used by another lane.")]
  public async Task Lanes_CodeRules(string code, string message)
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      await SeedLanesAsync(app);
      var r = await admin.PostAsJsonAsync("/api/reference-data/lanes", new { code, name = "Another Lane" });
      Assert.Equal(code == "r" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, r.StatusCode);
      Assert.Equal(message, await Message(r));
    }
  }

  [Fact]
  public async Task Lanes_LockedCodeCantChange_ButNameAndFlagsCan()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var ids = await SeedLanesAsync(app);
      var bad = await admin.PutAsJsonAsync($"/api/reference-data/lanes/{ids["V"]}", new { code = "VP", name = "Endorsed" });
      Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
      Assert.Equal(LockedLane, await Message(bad));

      var ok = await admin.PutAsJsonAsync($"/api/reference-data/lanes/{ids["V"]}",
        new { code = "V", name = "Endorsed / special cases", flags = new { active = false, needsEndorsement = true } });
      Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
      var v = await LaneAsync(admin, "V");
      Assert.Equal("Endorsed / special cases", v.GetProperty("name").GetString());
      Assert.False(v.GetProperty("flags").GetProperty("active").GetBoolean());
    }
  }

  [Fact]
  public async Task Lanes_LockedCantBeDeleted()
  {
    var (app, admin) = await StartAsync();
    await using (app)
    {
      var ids = await SeedLanesAsync(app);
      var s = await admin.DeleteAsync($"/api/reference-data/lanes/{ids["S"]}");
      Assert.Equal(HttpStatusCode.Conflict, s.StatusCode);
      Assert.Equal(LockedLane, await Message(s));
      Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/reference-data/lanes/{ids["X"]}")).StatusCode);
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
      var relationship = await admin.PostAsJsonAsync("/api/reference-data/relationships", new { name = "Spouse", group = "Spouse/Partner" });
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
