using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class UserAdminTests
{
    private const string Pw = "Str0ng-Passw0rd!x";
    private const string Temp = "Temp-Passw0rd!z";

    private static async Task<ClaimsApiFactory> SeedAsync()
    {
        var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "sa", Pw, "Super Administrator");
        await CreateUserAsync(app, "admin", Pw, "Administrator");
        await CreateUserAsync(app, "admin2", Pw, "Administrator");
        await CreateUserAsync(app, "front", Pw, "Frontliner");
        await CreateUserAsync(app, "sw", Pw, "Social Worker");
        return app;
    }

    private static async Task<int> IdOf(ClaimsApiFactory app, string username)
    {
        await using var db = app.CreateDbContext();
        return await db.Users.Where(u => u.Username == username).Select(u => u.UserId).SingleAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static object NewUser(string username, short[] roles, string? password = null) => new
    {
        username,
        firstname = "New",
        lastname = "Person",
        temporaryPassword = password ?? Temp,
        roleIds = roles,
    };

    [Fact]
    public async Task Frontliner_CannotAccessUsers()
    {
        await using var app = await SeedAsync();
        var client = await LoginAsAsync(app, "front", Pw);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ByAdmin_SocialWorker_Returns201_MustChange()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("newsw", [4]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var newId = body.GetProperty("userId").GetInt32();
        Assert.EndsWith($"/api/users/{newId}", response.Headers.Location!.ToString());
        Assert.DoesNotContain("/api/api/", response.Headers.Location.ToString());
        Assert.True(body.GetProperty("status").GetBoolean());

        var fresh = await LoginAsAsync(app, "newsw", Temp);
        var me = await ReadJsonAsync(await fresh.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());

        await using var db = app.CreateDbContext();
        var row = await db.Users.Include(u => u.UserRoles).SingleAsync(u => u.Username == "newsw");
        Assert.Equal(await IdOf(app, "admin"), row.CreatedBy);
        Assert.True(row.Status);
        Assert.All(row.UserRoles, ur => Assert.True(ur.Status));
        Assert.True(row.DateCreated.Year >= 2026);
    }

    [Fact]
    public async Task Create_DuplicateUsername_CaseInsensitive_Returns409()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("SW", [4]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("maría", HttpStatusCode.BadRequest)]
    [InlineData("a b", HttpStatusCode.BadRequest)]
    [InlineData("j.dela-cruz_2", HttpStatusCode.Created)]
    public async Task Create_UsernameCharset(string username, HttpStatusCode expected)
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser(username, [4]));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownRoleId_Returns400()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("x1", [99]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoRoles_Returns400()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("x2", []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WeakPassword_Returns400()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PostAsJsonAsync("/api/users", NewUser("x3", [4], "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotGrantAdministratorOrSuperAdministrator()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var swId = await IdOf(app, "sw");

        var create = await admin.PostAsJsonAsync("/api/users", NewUser("x4", [2]));
        var createSa = await admin.PostAsJsonAsync("/api/users", NewUser("x5", [1]));
        var replace = await admin.PutAsJsonAsync($"/api/users/{swId}/roles", new { roleIds = new[] { 2 } });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createSa.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, replace.StatusCode);
    }

    private static async Task AssertAllForbiddenAsync(HttpClient client, int targetId)
    {
        var reset = await client.PostAsJsonAsync($"/api/users/{targetId}/reset-password", new { temporaryPassword = Temp });
        var deactivate = await client.PostAsync($"/api/users/{targetId}/deactivate", null);
        var roles = await client.PutAsJsonAsync($"/api/users/{targetId}/roles", new { roleIds = new[] { 4 } });
        var update = await client.PutAsJsonAsync($"/api/users/{targetId}", new { firstname = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotTouchOtherAdmin()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        await AssertAllForbiddenAsync(admin, await IdOf(app, "admin2"));
    }

    [Fact]
    public async Task Admin_CannotTouchSuperAdministrator()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        await AssertAllForbiddenAsync(admin, await IdOf(app, "sa"));
    }

    [Fact]
    public async Task SuperAdmin_CanManageAdministrator()
    {
        await using var app = await SeedAsync();
        var sa = await LoginAsAsync(app, "sa", Pw);

        var response = await sa.PostAsJsonAsync($"/api/users/{await IdOf(app, "admin2")}/reset-password", new { temporaryPassword = Temp });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fresh = await LoginAsAsync(app, "admin2", Temp);
        var me = await ReadJsonAsync(await fresh.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Self_CannotDeactivate_ChangeRoles_OrReset()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var id = await IdOf(app, "admin");

        var deactivate = await admin.PostAsync($"/api/users/{id}/deactivate", null);
        var roles = await admin.PutAsJsonAsync($"/api/users/{id}/roles", new { roleIds = new[] { 4 } });
        var reset = await admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { temporaryPassword = Temp });

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
    }

    [Fact]
    public async Task Self_CanEditOwnNames()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var response = await admin.PutAsJsonAsync($"/api/users/{await IdOf(app, "admin")}", new { firstname = "Ada", lastname = "Lovelace" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ada", (await ReadJsonAsync(response)).GetProperty("firstname").GetString());
    }

    [Fact]
    public async Task Deactivate_ThenLoginFails_ThenActivate_LoginWorks()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var id = await IdOf(app, "sw");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/deactivate", null)).StatusCode);
        var failed = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "sw", password = Pw });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/activate", null)).StatusCode);
        await LoginAsAsync(app, "sw", Pw);
    }

    [Fact]
    public async Task Reset_ClearsLockout()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var anon = app.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await anon.PostAsJsonAsync("/api/auth/login", new { username = "sw", password = "wrong-password" });
        }
        var locked = await anon.PostAsJsonAsync("/api/auth/login", new { username = "sw", password = Pw });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        var reset = await admin.PostAsJsonAsync($"/api/users/{await IdOf(app, "sw")}/reset-password", new { temporaryPassword = Temp });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await LoginAsAsync(app, "sw", Temp);
    }

    [Fact]
    public async Task ReplaceRoles_RegrantRemovedRole_ReactivatesRow()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var id = await IdOf(app, "sw");

        foreach (var roles in new[] { new[] { 3 }, new[] { 4 }, new[] { 3, 4 } })
        {
            var response = await admin.PutAsJsonAsync($"/api/users/{id}/roles", new { roleIds = roles });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var db = app.CreateDbContext();
        var rows = await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Status));
    }

    [Fact]
    public async Task ReplaceRoles_DuplicateIds_Collapsed()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var id = await IdOf(app, "front");

        var response = await admin.PutAsJsonAsync($"/api/users/{id}/roles", new { roleIds = new[] { 4, 4 } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = app.CreateDbContext();
        Assert.Equal(1, await db.UserRoles.CountAsync(ur => ur.UserId == id && ur.Status));
    }

    [Fact]
    public async Task List_NeverContainsHashes()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);

        var text = await (await admin.GetAsync("/api/users")).Content.ReadAsStringAsync();

        Assert.Contains("\"sw\"", text);
        Assert.DoesNotContain("AQAAAA", text);
        Assert.DoesNotContain("\"password\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writes_SetLastUpdatedByAndDate()
    {
        await using var app = await SeedAsync();
        var admin = await LoginAsAsync(app, "admin", Pw);
        var id = await IdOf(app, "sw");
        app.Clock.Advance(TimeSpan.FromHours(2));

        var response = await admin.PutAsJsonAsync($"/api/users/{id}", new { firstname = "Sam" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = app.CreateDbContext();
        var row = await db.Users.SingleAsync(u => u.UserId == id);
        Assert.Equal(await IdOf(app, "admin"), row.LastUpdatedBy);
        Assert.Equal(app.Clock.GetLocalNow().DateTime, row.DateLastUpdated);
    }
}
