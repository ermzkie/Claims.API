using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.DTOs.Persons;
using Claims.API.Models;
using Claims.API.Services;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class PersonMatchingTests
{
  private const string Pw = "Str0ng-Passw0rd!x";
  private static readonly DateOnly Born = new(1980, 2, 3);

  private static NameParts P(string last, string first, string? middle = null, string? suffix = null) =>
    PersonMatcher.Parse(last, first, middle, suffix);

  [Theory]
  [InlineData("De La-Cruz", "DELACRUZ")]
  [InlineData("CAÑETE", "CANETE")]
  [InlineData("MA. NILDA", "MANILDA")]
  [InlineData("  ", "")]
  public void Letters_IgnoresSpacingPunctuationAndTilde(string value, string expected) =>
    Assert.Equal(expected, PersonMatcher.Letters(value));

  [Fact]
  public void Parse_ReadsTheSuffixOutOfTheName()
  {
    Assert.Equal(new NameParts("ADANIEL", "JOSE", "MAMON", "JR"), P("ADANIEL", "JOSE, JR.", "MAMON"));
    Assert.Equal(new NameParts("ADANIEL", "JOSE", "MAMON", "JR"), P("ADANIEL", "JOSE", "MAMON", "Jr."));
    Assert.Equal(new NameParts("REYES", "PEDRO", "", "III"), P("REYES III", "PEDRO"));
    // a trailing V is usually a middle initial, and a lone JR is a name
    Assert.Equal(new NameParts("CRUZ", "MARIAV", "", null), P("CRUZ", "MARIA V"));
    Assert.Equal(new NameParts("JR", "ANA", "", null), P("JR", "ANA"));
  }

  [Fact]
  public void Compare_SameAndSimilarAndNot()
  {
    var juan = P("DELA CRUZ", "JUAN", "SANTOS");

    Assert.Equal(new NameMatch(MatchLevel.Same, "Same name"), PersonMatcher.Compare(juan, null, P("DE LA CRUZ", "JUAN", "SANTOS"), null));
    Assert.Equal(new NameMatch(MatchLevel.Same, "Same name and birthdate"), PersonMatcher.Compare(juan, Born, juan, Born));
    // a missing birthdate matches any
    Assert.Equal(MatchLevel.Same, PersonMatcher.Compare(juan, null, juan, Born).Level);

    Assert.Equal(new NameMatch(MatchLevel.Similar, "Middle name is only an initial"), PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUAN", "S."), null));
    Assert.Equal(new NameMatch(MatchLevel.Similar, "Only one has a middle name"), PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUAN"), null));
    Assert.Equal(new NameMatch(MatchLevel.Similar, "First and last name swapped"), PersonMatcher.Compare(juan, null, P("JUAN", "DELA CRUZ", "SANTOS"), null));
    Assert.Equal(new NameMatch(MatchLevel.Similar, "Only one has the suffix SR"), PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUAN, SR.", "SANTOS"), null));

    Assert.Equal(MatchLevel.None, PersonMatcher.Compare(juan, Born, juan, Born.AddDays(1)).Level);
    Assert.Equal(MatchLevel.None, PersonMatcher.Compare(P("DELA CRUZ", "JUAN JR"), null, P("DELA CRUZ", "JUAN SR"), null).Level);
    Assert.Equal(MatchLevel.None, PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUAN", "REYES"), null).Level);
    Assert.Equal(MatchLevel.None, PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUANA", "SANTOS"), null).Level);
    Assert.Equal(MatchLevel.None, PersonMatcher.Compare(juan, null, P("DELA CRUZ", "JUAN", "R"), null).Level);
  }

  private static async Task<(ClaimsApiFactory App, HttpClient Client)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "admin", Pw, "Administrator");
    var created = new DateTime(2025, 1, 1);
    Person Make(int id, string last, string first, string? middle, bool active = true) =>
      new() { PersonId = id, LastName = last, FirstName = first, MiddleName = middle, Status = active, DateCreated = created };
    await using (var db = app.CreateDbContext())
    {
      var busiest = Make(50, "DE LA CRUZ", "JUAN", "SANTOS");
      busiest.Barangay = "ZONE I";
      busiest.Municipality = "KORONADAL";
      db.People.AddRange(
        busiest,
        Make(58, "DELA CRUZ", "JUAN", "SANTOS"),
        Make(51, "DELA CRUZ", "JUAN", "S."),
        Make(52, "JUAN", "DELA CRUZ", "SANTOS"),
        Make(53, "DELA CRUZ", "JUAN, SR.", "SANTOS"),
        Make(54, "DELA CRUZ", "JUAN", "SANTOS", active: false),
        Make(55, "DELA CRUZ", "JUAN", "REYES"),
        Make(56, "LOPEZ", "ANA", null),
        Make(57, "LOPEZ", "ANA", null));
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, Status = true, Date = new DateTime(2026, 1, 1), PatientPersonId = 50 },
        new Transaction { TransactionId = 2, Status = true, Date = new DateTime(2026, 2, 1), RepresentativePersonId = 50 });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "admin", Pw));
  }

  private static SavePersonDto Juan(string? middle = "SANTOS") => new()
  {
    LastName = "dela cruz",
    FirstName = "juan",
    MiddleName = middle,
  };

  [Fact]
  public async Task Matches_SameFirstThenSimilar()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var matches = await client.GetFromJsonAsync<List<PersonMatchDto>>(
      "/api/persons/matches?lastName=Dela%20Cruz&firstName=juan&middleName=santos");

    Assert.Equal([50, 58, 51, 52, 53], matches!.Select(m => m.PersonId));
    Assert.Equal(["same", "same", "similar", "similar", "similar"], matches.Select(m => m.Level));
    var first = matches[0];
    Assert.Equal(("DE LA CRUZ, JUAN SANTOS", "ZONE I, KORONADAL", 2, "Same name"), (first.Name, first.Address, first.Claims, first.Reason));

    var none = await client.GetFromJsonAsync<List<PersonMatchDto>>("/api/persons/matches?lastName=Dela%20Cruz");
    Assert.Empty(none!);
    var except = await client.GetFromJsonAsync<List<PersonMatchDto>>("/api/persons/matches?lastName=lopez&firstName=ana&exceptPersonId=56");
    Assert.Equal([57], except!.Select(m => m.PersonId));
  }

  [Fact]
  public async Task Create_SameNameNeedsAReason()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var blocked = await client.PostAsJsonAsync("/api/persons", Juan());
    Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
    Assert.StartsWith("2 records look like the same person.", body.GetProperty("message").GetString());
    Assert.Equal(5, body.GetProperty("matches").GetArrayLength());

    var noReason = Juan();
    noReason.ConfirmDuplicate = true;
    var missing = await client.PostAsJsonAsync("/api/persons", noReason);
    Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

    var confirmed = Juan();
    confirmed.ConfirmDuplicate = true;
    confirmed.DuplicateReason = " different mother, checked ID ";
    var saved = await client.PostAsJsonAsync("/api/persons", confirmed);
    Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
    var person = await saved.Content.ReadFromJsonAsync<PersonDto>();

    await using var db = app.CreateDbContext();
    Assert.Equal(10, await db.People.CountAsync());
    var log = await db.AuditLogs.SingleAsync();
    Assert.Equal(("Person", person!.PersonId, "SavedAsDuplicate", "different mother, checked ID"), (log.Entity, log.EntityId, log.Action, log.Remark));
    Assert.Contains("[50,58]", log.NewValues);
  }

  [Fact]
  public async Task Create_SimilarOnlyIsNotBlocked_AndSpacesAreCollapsed()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    // SANTIAGO only matches 51 (initial S), which is similar, not same
    var similar = await client.PostAsJsonAsync("/api/persons", Juan("SANTIAGO"));
    Assert.Equal(HttpStatusCode.Created, similar.StatusCode);

    var spaced = await client.PostAsJsonAsync("/api/persons", new SavePersonDto { LastName = "  dela   cruz ", FirstName = "pedro  jose" });
    var person = await spaced.Content.ReadFromJsonAsync<PersonDto>();
    Assert.Equal(("DELA CRUZ", "PEDRO JOSE"), (person!.LastName, person.FirstName));
  }

  [Fact]
  public async Task Update_OnlyARenameIsChecked()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    // 56 and 57 are already the same name; other fields can still be fixed
    var contact = await client.PutAsJsonAsync("/api/persons/56", new SavePersonDto { LastName = "LOPEZ", FirstName = "ANA", ContactNo = "09171234567" });
    Assert.Equal(HttpStatusCode.OK, contact.StatusCode);

    var renamed = await client.PutAsJsonAsync("/api/persons/55", Juan());
    Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
    await using (var db = app.CreateDbContext())
    {
      Assert.Equal("REYES", (await db.People.FindAsync(55))!.MiddleName);
    }

    // re-spelling a record is a rename, but it never matches itself
    var self = await client.PutAsJsonAsync("/api/persons/53", new SavePersonDto { LastName = "DELA CRUZ", FirstName = "JUAN SR.", MiddleName = "SANTOS" });
    Assert.Equal(HttpStatusCode.OK, self.StatusCode);
  }

  [Fact]
  public async Task Report_GroupsSpellingVariants()
  {
    var (app, client) = await StartAsync();
    await using var _ = app;

    var page = await client.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates");

    Assert.Equal(2, page!.Total);
    Assert.Equal([50, 58], page.Items[0].Persons.Select(p => p.PersonId));
    Assert.Equal("DE LA CRUZ", page.Items[0].LastName);
    Assert.Equal([56, 57], page.Items[1].Persons.Select(p => p.PersonId));
  }
}
