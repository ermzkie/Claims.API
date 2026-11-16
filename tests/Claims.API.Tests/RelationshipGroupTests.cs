using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class RelationshipGroupTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private record Item(int Id, string? Code, string Name, int UsedBy, string? Group);
  private record ListInfo(string Key, string Title, bool HasCode, List<string>? Groups);
  private record LookupItem(int Id, string? Code, string Name, string? Group);

  private static async Task<(ClaimsApiFactory App, HttpClient Admin)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "admin", Pw, "Administrator");
    return (app, await LoginAsAsync(app, "admin", Pw));
  }

  [Fact]
  public async Task Relationships_NeedAGroupFromTheFixedList()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var noGroup = await admin.PostAsJsonAsync("/api/reference-data/relationships", new { name = "Cousin" });
    var unknown = await admin.PostAsJsonAsync("/api/reference-data/relationships", new { name = "Cousin", group = "Friends" });
    var ok = await admin.PostAsJsonAsync("/api/reference-data/relationships", new { name = "Cousin", group = "extended FAMILY" });

    Assert.Equal(HttpStatusCode.BadRequest, noGroup.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    Assert.Equal(new Item(1, null, "Cousin", 0, "Extended family"), await ok.Content.ReadFromJsonAsync<Item>());

    var moved = await admin.PutAsJsonAsync("/api/reference-data/relationships/1", new { name = "Cousin", group = "Others" });
    Assert.Equal("Others", (await moved.Content.ReadFromJsonAsync<Item>())!.Group);
  }

  [Fact]
  public async Task OnlyRelationshipsHaveGroups()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var lists = await admin.GetFromJsonAsync<List<ListInfo>>("/api/reference-data");
    Assert.Equal("Spouse/Partner", lists!.Single(l => l.Key == "relationships").Groups![0]);
    Assert.All(lists!.Where(l => l.Key != "relationships"), l => Assert.Null(l.Groups));

    var suffix = await admin.PostAsJsonAsync("/api/reference-data/name-suffixes", new { name = "Jr.", group = "Children" });
    Assert.Null((await suffix.Content.ReadFromJsonAsync<Item>())!.Group);
  }

  [Fact]
  public async Task Lookup_ListsRelationshipsByGroupOrder_AndOtherListsHaveNoGroupField()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;
    await using (var db = app.CreateDbContext())
    {
      db.RelationshipStatuses.AddRange(
        new RelationshipStatus { RelationshipStatusId = 1, RelationshipStatusDesc = "Son", RelationshipGroup = "Children" },
        new RelationshipStatus { RelationshipStatusId = 2, RelationshipStatusDesc = "Mother", RelationshipGroup = "Parents" },
        new RelationshipStatus { RelationshipStatusId = 3, RelationshipStatusDesc = "Spouse", RelationshipGroup = "Spouse/Partner" },
        new RelationshipStatus { RelationshipStatusId = 4, RelationshipStatusDesc = "Daughter", RelationshipGroup = "Children" },
        new RelationshipStatus { RelationshipStatusId = 5, RelationshipStatusDesc = "Unsorted" });
      db.CivilStatuses.Add(new CivilStatus { CivilStatusId = 1, CivilStatusCode = "S", CivilStatusDesc = "Single" });
      await db.SaveChangesAsync();
    }

    var list = await admin.GetFromJsonAsync<List<LookupItem>>("/api/lookups/relationship-statuses");
    Assert.Equal(["Spouse", "Son", "Daughter", "Mother", "Unsorted"], list!.Select(r => r.Name));
    Assert.Equal("Children", list![1].Group);

    var civil = await admin.GetStringAsync("/api/lookups/civil-statuses");
    Assert.DoesNotContain("group", JsonDocument.Parse(civil).RootElement[0].EnumerateObject().Select(p => p.Name));
  }
}
