using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class FamilyTests
{
  private const string Pw = "Str0ng-Passw0rd!x";
  private const int Maria = 10, Juan = 11, Ana = 12, Gone = 13;

  private static async Task<(ClaimsApiFactory App, HttpClient Client, int UserId)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "fl", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      db.Sexes.Add(new Sex { SexId = 1, SexCode = "M", SexDesc = "Male" });
      db.CivilStatuses.Add(new CivilStatus { CivilStatusId = 1, CivilStatusCode = "S", CivilStatusDesc = "Single" });
      db.RelationshipStatuses.AddRange(
        new RelationshipStatus { RelationshipStatusId = 1, RelationshipStatusDesc = "Son", RelationshipGroup = "Children" },
        new RelationshipStatus { RelationshipStatusId = 2, RelationshipStatusDesc = "Mother", RelationshipGroup = "Parents" });
      db.People.AddRange(
        new Person { PersonId = Maria, FirstName = "MARIA", LastName = "SANTOS", Status = true },
        new Person
        {
          PersonId = Juan, FirstName = "JUAN", LastName = "SANTOS", Status = true, SexId = 1,
          Birthdate = new DateOnly(2001, 4, 2), CivilStatusId = 1, EstimatedMonthlyIncome = 8000m, ContactNo = "09171234567",
        },
        new Person { PersonId = Ana, FirstName = "ANA", LastName = "SANTOS", Status = true },
        new Person { PersonId = Gone, FirstName = "OLD", LastName = "RECORD", Status = false });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "fl", Pw), userId);
  }

  private static async Task<string> Message(HttpResponseMessage r) =>
    JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

  private static Task<HttpResponseMessage> Add(HttpClient client, int personId, int familyPersonId, short? relationship) =>
    client.PostAsJsonAsync($"/api/persons/{personId}/family", new { familyPersonId, relationshipStatusId = relationship });

  [Fact]
  public async Task Add_ChecksRelationshipSelfDuplicatesAndInactivePersons()
  {
    var (app, client, _) = await StartAsync();
    await using var _app = app;

    var noRelationship = await Add(client, Maria, Juan, null);
    var self = await Add(client, Maria, Maria, 1);
    var inactive = await Add(client, Maria, Gone, 1);
    var ok = await Add(client, Maria, Juan, 1);
    var duplicate = await Add(client, Maria, Juan, 2);
    var onInactive = await Add(client, Gone, Juan, 1);

    Assert.Equal("Choose the relationship.", await Message(noRelationship));
    Assert.Equal("A person can't be listed as their own family member.", await Message(self));
    Assert.Equal("Family person not found.", await Message(inactive));
    Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    Assert.Equal("This person is already listed as a family member.", await Message(duplicate));
    Assert.Equal(HttpStatusCode.NotFound, onInactive.StatusCode);
  }

  [Fact]
  public async Task Person_ListsFamilyWithDetails_AndWhoListsThemAsFamily()
  {
    var (app, client, _) = await StartAsync();
    await using var _app = app;
    await Add(client, Maria, Juan, 1);
    await Add(client, Ana, Juan, 1);

    var maria = JsonDocument.Parse(await client.GetStringAsync($"/api/persons/{Maria}")).RootElement;
    var member = maria.GetProperty("family")[0];
    Assert.Equal(Juan, member.GetProperty("personId").GetInt32());
    Assert.Equal("Son", member.GetProperty("relationship").GetString());
    Assert.Equal("Children", member.GetProperty("relationshipGroup").GetString());
    Assert.Equal("Male", member.GetProperty("sex").GetString());
    Assert.Equal("2001-04-02", member.GetProperty("birthdate").GetString());
    Assert.Equal("Single", member.GetProperty("civilStatus").GetString());
    Assert.Equal(8000m, member.GetProperty("estimatedMonthlyIncome").GetDecimal());
    Assert.Equal("09171234567", member.GetProperty("contactNo").GetString());

    var juan = JsonDocument.Parse(await client.GetStringAsync($"/api/persons/{Juan}")).RootElement;
    Assert.Equal(0, juan.GetProperty("family").GetArrayLength());
    var listedBy = juan.GetProperty("familyOf").EnumerateArray().ToList();
    Assert.Equal([Maria, Ana], listedBy.Select(f => f.GetProperty("personId").GetInt32()));
    Assert.Equal("MARIA", listedBy[0].GetProperty("firstName").GetString());
    Assert.Equal("Son", listedBy[0].GetProperty("relationship").GetString());
  }

  [Fact]
  public async Task Remove_HidesTheLink_AndKeepsItWithAudit()
  {
    var (app, client, userId) = await StartAsync();
    await using var _app = app;
    var added = JsonDocument.Parse(await (await Add(client, Maria, Juan, 1)).Content.ReadAsStringAsync()).RootElement;
    var familyCompId = added.GetProperty("familyCompId").GetInt32();

    Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/persons/{Ana}/family/{familyCompId}")).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/persons/{Maria}/family/{familyCompId}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/persons/{Maria}/family/{familyCompId}")).StatusCode);

    var maria = JsonDocument.Parse(await client.GetStringAsync($"/api/persons/{Maria}")).RootElement;
    Assert.Equal(0, maria.GetProperty("family").GetArrayLength());
    await using var db = app.CreateDbContext();
    var link = await db.FamilyCompositions.AsNoTracking().SingleAsync();
    Assert.False(link.Status);
    Assert.Equal(userId, link.LastUpdatedBy);

    Assert.Equal(HttpStatusCode.Created, (await Add(client, Maria, Juan, 1)).StatusCode);
  }

  [Fact]
  public async Task Update_CanKeepTheSameMember_ButNotDuplicateAnother()
  {
    var (app, client, _) = await StartAsync();
    await using var _app = app;
    var juanLink = JsonDocument.Parse(await (await Add(client, Maria, Juan, 1)).Content.ReadAsStringAsync()).RootElement
      .GetProperty("familyCompId").GetInt32();
    await Add(client, Maria, Ana, 2);

    var sameMember = await client.PutAsJsonAsync($"/api/persons/{Maria}/family/{juanLink}", new { familyPersonId = Juan, relationshipStatusId = 2 });
    var toAna = await client.PutAsJsonAsync($"/api/persons/{Maria}/family/{juanLink}", new { familyPersonId = Ana, relationshipStatusId = 2 });

    Assert.Equal(HttpStatusCode.OK, sameMember.StatusCode);
    Assert.Equal("Mother", JsonDocument.Parse(await sameMember.Content.ReadAsStringAsync()).RootElement.GetProperty("relationship").GetString());
    Assert.Equal("This person is already listed as a family member.", await Message(toAna));
  }

  [Fact]
  public async Task OldRoutes_StillWork()
  {
    var (app, client, _) = await StartAsync();
    await using var _app = app;

    var added = await client.PostAsJsonAsync($"/api/persons/{Maria}/family/add", new { familyPersonId = Juan, relationshipStatusId = 1 });
    var id = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement.GetProperty("familyCompId").GetInt32();
    var updated = await client.PutAsJsonAsync($"/api/persons/{Maria}/family/{id}/update", new { familyPersonId = Juan, relationshipStatusId = 2 });

    Assert.Equal(HttpStatusCode.Created, added.StatusCode);
    Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
  }

  [Fact]
  public async Task EveryChange_IsLogged_WithOldAndNewValuesWhoAndRemark()
  {
    var (app, client, userId) = await StartAsync();
    await using var _app = app;

    var added = await client.PostAsJsonAsync($"/api/persons/{Maria}/family", new { familyPersonId = Juan, relationshipStatusId = 1, remark = "per barangay cert" });
    var id = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement.GetProperty("familyCompId").GetInt32();
    await client.PutAsJsonAsync($"/api/persons/{Maria}/family/{id}", new { familyPersonId = Juan, relationshipStatusId = 1 });
    await client.PutAsJsonAsync($"/api/persons/{Maria}/family/{id}", new { familyPersonId = Juan, relationshipStatusId = 2 });
    await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/persons/{Maria}/family/{id}")
    {
      Content = JsonContent.Create(new { remark = "entered by mistake" }),
    });

    var history = JsonDocument.Parse(await client.GetStringAsync($"/api/persons/{Maria}/family/history")).RootElement.EnumerateArray().ToList();
    Assert.Equal(["Removed", "Changed", "Added"], history.Select(h => h.GetProperty("action").GetString()));

    var addedLog = history[2];
    Assert.Equal(JsonValueKind.Null, addedLog.GetProperty("old").ValueKind);
    Assert.Equal("SANTOS, JUAN", addedLog.GetProperty("new").GetProperty("member").GetString());
    Assert.Equal("Son", addedLog.GetProperty("new").GetProperty("relationship").GetString());
    Assert.Equal("per barangay cert", addedLog.GetProperty("remark").GetString());
    Assert.Equal("fl", addedLog.GetProperty("changedBy").GetString());

    var changedLog = history[1];
    Assert.Equal("Son", changedLog.GetProperty("old").GetProperty("relationship").GetString());
    Assert.Equal("Mother", changedLog.GetProperty("new").GetProperty("relationship").GetString());

    var removedLog = history[0];
    Assert.Equal("Mother", removedLog.GetProperty("old").GetProperty("relationship").GetString());
    Assert.Equal("entered by mistake", removedLog.GetProperty("remark").GetString());
    Assert.Equal(id, removedLog.GetProperty("familyCompId").GetInt32());

    await using var db = app.CreateDbContext();
    Assert.All(await db.AuditLogs.AsNoTracking().ToListAsync(), a => Assert.Equal(userId, a.UserId));
  }

  [Fact]
  public async Task TooLongRemark_SavesNothing_AndHistoryNeedsAPerson()
  {
    var (app, client, _) = await StartAsync();
    await using var _app = app;

    var response = await client.PostAsJsonAsync($"/api/persons/{Maria}/family", new { familyPersonId = Juan, relationshipStatusId = 1, remark = new string('x', 501) });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    await using (var db = app.CreateDbContext())
    {
      Assert.Empty(await db.FamilyCompositions.ToListAsync());
      Assert.Empty(await db.AuditLogs.ToListAsync());
    }
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/persons/999/family/history")).StatusCode);
  }
}
