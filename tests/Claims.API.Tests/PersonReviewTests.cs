using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Persons;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class PersonReviewTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Admin)> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "admin", Pw, "Administrator");
    await CreateUserAsync(app, "fl", Pw, "Frontliner");
    var created = new DateTime(2025, 1, 1);
    var born = new DateOnly(1960, 5, 1);
    await using (var db = app.CreateDbContext())
    {
      db.People.AddRange(
        new Person { PersonId = 20, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Birthdate = born, Status = true, DateCreated = created, ContactNo = " 0917 " },
        new Person { PersonId = 21, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Birthdate = born, Status = true, DateCreated = created, Barangay = "ZONE I", Municipality = "KORONADAL" },
        // different birthdate, and removed: not duplicates
        new Person { PersonId = 22, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Birthdate = new DateOnly(1961, 1, 1), Status = true, DateCreated = created },
        new Person { PersonId = 23, LastName = "SANTOS", FirstName = "MARIA", MiddleName = "CRUZ", Birthdate = born, Status = false, DateCreated = created },
        // no middle name or birthdate on either still counts as the same
        new Person { PersonId = 30, LastName = "REYES", FirstName = "ANA", Status = true, DateCreated = created },
        new Person { PersonId = 31, LastName = "REYES", FirstName = "ANA", Status = true, DateCreated = created },
        new Person { PersonId = 40, Status = true, DateCreated = created },
        new Person { PersonId = 41, LastName = "LOPEZ", Status = true, DateCreated = created },
        new Person { PersonId = 42, Status = false, DateCreated = created });
      db.Transactions.AddRange(
        new Transaction { TransactionId = 1, Status = true, Date = new DateTime(2026, 1, 15), PatientPersonId = 20 },
        new Transaction { TransactionId = 2, Status = false, Date = new DateTime(2022, 3, 1), PatientPersonId = 20 },
        new Transaction { TransactionId = 3, Status = false, Date = new DateTime(2026, 2, 1), PatientPersonId = 20, CreatedBy = userId },
        new Transaction { TransactionId = 4, Status = true, Date = new DateTime(2026, 3, 1), PatientPersonId = 30, RepresentativePersonId = 21 },
        new Transaction { TransactionId = 5, Status = true, Date = new DateTime(2026, 4, 1), PatientPersonId = 40 },
        new Transaction { TransactionId = 6, Status = true, Date = new DateTime(2026, 4, 2), PatientPersonId = 40 },
        new Transaction { TransactionId = 7, Status = true, Date = new DateTime(2026, 4, 3), PatientPersonId = 40 });
      db.FamilyCompositions.Add(new FamilyComposition { PersonId = 20, FamilyPersonId = 21, Status = true, CreatedBy = userId, DateCreated = created });
      await db.SaveChangesAsync();
    }
    return (app, await LoginAsAsync(app, "admin", Pw));
  }

  [Fact]
  public async Task Duplicates_SameNameAndBirthdate_BusiestFirst()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var page = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates");

    Assert.Equal(2, page!.Total);
    var santos = page.Items[0];
    Assert.Equal(("SANTOS", "MARIA", "CRUZ", new DateOnly(1960, 5, 1), 2), (santos.LastName, santos.FirstName, santos.MiddleName, santos.Birthdate, santos.Claims));
    Assert.Equal([20, 21], santos.Persons.Select(p => p.PersonId));

    var first = santos.Persons[0];
    Assert.Equal("SANTOS, MARIA CRUZ", first.Name);
    Assert.Equal(2, first.Claims);
    Assert.Equal(new DateTime(2026, 1, 15), first.LastClaimDate);
    Assert.Equal(1, first.FamilyLinks);
    Assert.Equal("0917", first.ContactNo);

    var second = santos.Persons[1];
    Assert.Equal((0, 1, 1), (second.Claims, second.AsRepresentative, second.FamilyLinks));
    Assert.Equal("ZONE I, KORONADAL", second.Address);
    Assert.Equal(new DateTime(2026, 3, 1), second.LastClaimDate);

    Assert.Equal([30, 31], page.Items[1].Persons.Select(p => p.PersonId));
    Assert.Null(page.Items[1].Birthdate);
  }

  [Fact]
  public async Task Duplicates_SearchAndPaging()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var reyes = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?search=ana%20reyes");
    Assert.Equal("REYES", Assert.Single(reyes!.Items).LastName);

    var second = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?page=2&pageSize=1");
    Assert.Equal((2, 2, 1), (second!.Total, second.Page, second.PageSize));
    Assert.Equal("REYES", Assert.Single(second.Items).LastName);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/person-review/duplicates?pageSize=0")).StatusCode);
  }

  [Fact]
  public async Task Nameless_MostClaimsFirst()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var page = await admin.GetFromJsonAsync<PersonReviewPageDto<PersonReviewRowDto>>("/api/person-review/nameless");

    Assert.Equal(2, page!.Total);
    Assert.Equal([40, 41], page.Items.Select(p => p.PersonId));
    Assert.Null(page.Items[0].Name);
    Assert.Equal(3, page.Items[0].Claims);
    Assert.Equal("LOPEZ", page.Items[1].Name);
  }

  [Fact]
  public async Task AdminsOnly()
  {
    var (app, _) = await StartAsync();
    await using var __ = app;

    var frontliner = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.GetAsync("/api/person-review/duplicates")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.GetAsync("/api/person-review/nameless")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/person-review/nameless")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.PostAsJsonAsync("/api/person-review/duplicates/not-same", new { personIds = new[] { 30, 31 } })).StatusCode);
  }

  [Fact]
  public async Task NotSamePerson_HidesTheGroup_UntilReopened()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    var mark = await admin.PostAsJsonAsync("/api/person-review/duplicates/not-same", new { personIds = new[] { 31, 30 }, remark = " Mother and daughter " });
    Assert.Equal(HttpStatusCode.NoContent, mark.StatusCode);

    var open = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates");
    Assert.Equal("SANTOS", Assert.Single(open!.Items).LastName);

    var reviewed = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?decision=different");
    var group = Assert.Single(reviewed!.Items);
    Assert.Equal("REYES", group.LastName);
    Assert.Equal(("different", "Mother and daughter"), (group.Review!.Decision, group.Review.Remark));
    Assert.False(string.IsNullOrEmpty(group.Review.By));

    await using (var db = app.CreateDbContext())
    {
      var log = Assert.Single(db.AuditLogs.Where(a => a.Entity == "DuplicateGroup"));
      Assert.Equal(("NotSamePerson", 30), (log.Action, log.EntityId));
    }

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/person-review/duplicates/reopen", new { personIds = new[] { 30, 31 } })).StatusCode);
    var again = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates");
    Assert.Equal(2, again!.Total);
  }

  [Fact]
  public async Task SamePerson_WaitsForMerge_AndCanChangeToDifferent()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/person-review/duplicates/same", new { personIds = new[] { 20, 21 }, remark = "Same contact and mother", keepPersonId = 20 })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/person-review/duplicates/same", new { personIds = new[] { 20, 21 }, keepPersonId = 20 })).StatusCode);

    var toReview = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates");
    Assert.Equal("REYES", Assert.Single(toReview!.Items).LastName);
    var same = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?decision=same");
    var group = Assert.Single(same!.Items);
    Assert.Equal(("SANTOS", "same", "Same contact and mother"), (group.LastName, group.Review!.Decision, group.Review.Remark));

    // persons and claims are left alone until merging exists
    await using (var db = app.CreateDbContext())
    {
      Assert.True(db.People.Where(p => p.PersonId == 20 || p.PersonId == 21).All(p => p.Status));
      Assert.Equal(3, db.Transactions.Count(t => t.PatientPersonId == 20));
    }

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/person-review/duplicates/not-same", new { personIds = new[] { 20, 21 } })).StatusCode);
    Assert.Empty((await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?decision=same"))!.Items);
    Assert.Equal("different", Assert.Single((await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?decision=different"))!.Items).Review!.Decision);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/person-review/duplicates?decision=maybe")).StatusCode);
  }

  [Fact]
  public async Task PersonClaims_NewestFirst_WithTheOtherPersonOnTheClaim()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;
    await using (var db = app.CreateDbContext())
    {
      db.Officers.Add(new Officer { Idnumber = 7, Name = " OLD WORKER ", Password = "not read" });
      db.Creditors.Add(new Creditor { CreditorId = 1, CreditorName = "HOSPITAL A", Status = true });
      var claim = db.Transactions.Single(t => t.TransactionId == 7);
      (claim.SocialWorkerId, claim.CreditorId, claim.Amount, claim.Representative) = ("7", 1, 1500m, " LOPEZ, BEN ");
      await db.SaveChangesAsync();
    }

    var nameless = await admin.GetFromJsonAsync<List<PersonReviewClaimDto>>("/api/person-review/persons/40/claims");
    Assert.Equal([7, 6, 5], nameless!.Select(c => c.TransactionId));
    var latest = nameless[0];
    Assert.Equal(("patient", "LOPEZ, BEN", "OLD WORKER", "HOSPITAL A", 1500m), (latest.Role, latest.OtherPerson, latest.SocialWorker, latest.CreditorName, latest.Amount));

    var representative = Assert.Single((await admin.GetFromJsonAsync<List<PersonReviewClaimDto>>("/api/person-review/persons/21/claims"))!);
    Assert.Equal(("representative", "REYES, ANA", 30), (representative.Role, representative.OtherPerson, representative.OtherPersonId));

    // the claim deleted in this app is left out; the old-system one counts
    Assert.Equal([1, 2], (await admin.GetFromJsonAsync<List<PersonReviewClaimDto>>("/api/person-review/persons/20/claims"))!.Select(c => c.TransactionId));
    Assert.Single((await admin.GetFromJsonAsync<List<PersonReviewClaimDto>>("/api/person-review/persons/40/claims?take=1"))!);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/person-review/persons/40/claims?take=0")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/person-review/persons/999/claims")).StatusCode);
    var frontliner = await LoginAsAsync(app, "fl", Pw);
    Assert.Equal(HttpStatusCode.Forbidden, (await frontliner.GetAsync("/api/person-review/persons/40/claims")).StatusCode);
  }

  [Fact]
  public async Task SamePerson_NeedsTheRecordToKeep_AndItCanBeChanged()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    async Task<HttpStatusCode> Same(object body) =>
      (await admin.PostAsJsonAsync("/api/person-review/duplicates/same", body)).StatusCode;
    async Task<DuplicateReviewDto> Review() =>
      Assert.Single((await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?decision=same"))!.Items).Review!;

    Assert.Equal(HttpStatusCode.BadRequest, await Same(new { personIds = new[] { 20, 21 } }));
    Assert.Equal(HttpStatusCode.BadRequest, await Same(new { personIds = new[] { 20, 21 }, keepPersonId = 30 }));

    Assert.Equal(HttpStatusCode.NoContent, await Same(new { personIds = new[] { 20, 21 }, keepPersonId = 21 }));
    Assert.Equal(21, (await Review()).KeepPersonId);

    // same record again is refused, another one changes it
    Assert.Equal(HttpStatusCode.BadRequest, await Same(new { personIds = new[] { 20, 21 }, keepPersonId = 21 }));
    Assert.Equal(HttpStatusCode.NoContent, await Same(new { personIds = new[] { 20, 21 }, keepPersonId = 20 }));
    Assert.Equal(20, (await Review()).KeepPersonId);

    await using var db = app.CreateDbContext();
    var last = db.AuditLogs.Where(a => a.Entity == "DuplicateGroup").OrderBy(a => a.AuditLogId).ToList().Last();
    Assert.Contains("\"keepPersonId\":20", last.NewValues);
    Assert.True(db.People.Where(p => p.PersonId == 20 || p.PersonId == 21).All(p => p.Status));
  }

  [Fact]
  public async Task NotSamePerson_ComesBackWhenSomeoneNewJoins()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    await admin.PostAsJsonAsync("/api/person-review/duplicates/not-same", new { personIds = new[] { 30, 31 } });
    await using (var db = app.CreateDbContext())
    {
      db.People.Add(new Person { PersonId = 32, LastName = "REYES", FirstName = "ANA", Status = true, DateCreated = new DateTime(2026, 1, 1) });
      await db.SaveChangesAsync();
    }

    var open = await admin.GetFromJsonAsync<PersonReviewPageDto<DuplicateGroupDto>>("/api/person-review/duplicates?search=reyes");
    Assert.Equal([30, 31, 32], Assert.Single(open!.Items).Persons.Select(p => p.PersonId).Order());
  }

  [Fact]
  public async Task NotSamePerson_Checks()
  {
    var (app, admin) = await StartAsync();
    await using var _ = app;

    async Task<HttpStatusCode> Post(string what, object body) =>
      (await admin.PostAsJsonAsync($"/api/person-review/duplicates/{what}", body)).StatusCode;

    Assert.Equal(HttpStatusCode.BadRequest, await Post("not-same", new { personIds = new[] { 30 } }));
    // not a whole group, or persons from different groups
    Assert.Equal(HttpStatusCode.BadRequest, await Post("not-same", new { personIds = new[] { 20, 30 } }));
    Assert.Equal(HttpStatusCode.BadRequest, await Post("not-same", new { personIds = new[] { 20, 22 } }));
    Assert.Equal(HttpStatusCode.BadRequest, await Post("not-same", new { personIds = new[] { 30, 31 }, remark = new string('x', 501) }));
    Assert.Equal(HttpStatusCode.BadRequest, await Post("reopen", new { personIds = new[] { 30, 31 } }));
    Assert.Equal(HttpStatusCode.NoContent, await Post("not-same", new { personIds = new[] { 30, 31 } }));
    Assert.Equal(HttpStatusCode.BadRequest, await Post("not-same", new { personIds = new[] { 30, 31 } }));
  }
}
