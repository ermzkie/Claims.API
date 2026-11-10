using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Auth;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class PasswordChangeTests
{
    private const string Pw = "Str0ng-Passw0rd!x";
    private const string NewPw = "An0ther-Passw0rd!y";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task PendingChange_BlocksOtherEndpoints_WithCode()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "fl", Pw, ["Frontliner"], mustChangePassword: true);
        var client = await LoginAsAsync(app, "fl", Pw);

        var response = await client.GetAsync("/api/NatureOfService");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("password_change_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PendingChange_BlocksAdminEndpoints()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "adm", Pw, ["Administrator"], mustChangePassword: true);
        var client = await LoginAsAsync(app, "adm", Pw);

        var response = await client.PostAsJsonAsync("/api/NatureOfService", new { natureDesc = "Anything" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("password_change_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PendingChange_AllowsMe()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "fl", Pw, ["Frontliner"], mustChangePassword: true);
        var client = await LoginAsAsync(app, "fl", Pw);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task ChangePassword_ClearsFlag_SameTokenNowWorks()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        var id = await CreateUserAsync(app, "fl", Pw, ["Frontliner"], mustChangePassword: true);
        var client = await LoginAsAsync(app, "fl", Pw);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Pw, newPassword = NewPw });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/NatureOfService")).StatusCode);

        await using var db = app.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == id);
        Assert.False(user.MustChangePassword);
        Assert.Equal(id, user.LastUpdatedBy);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordLogsIn_OldDoesNot()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "fl", Pw, ["Frontliner"], mustChangePassword: true);
        var client = await LoginAsAsync(app, "fl", Pw);
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Pw, newPassword = NewPw });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var anon = app.CreateClient();
        var oldLogin = await anon.PostAsJsonAsync("/api/auth/login", new { username = "fl", password = Pw });
        var newLogin = await anon.PostAsJsonAsync("/api/auth/login", new { username = "fl", password = NewPw });

        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns400_AndCountsTowardLockout()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        var id = await CreateUserAsync(app, "fl", Pw, "Frontliner");
        var client = await LoginAsAsync(app, "fl", Pw);

        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = "Wrong-Passw0rd!z", newPassword = NewPw });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            var body = await ReadJsonAsync(wrong);
            Assert.Equal("Current password is incorrect.", body.GetProperty("message").GetString());
        }

        await using (var db = app.CreateDbContext())
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == id);
            Assert.NotNull(user.LockoutEnd);
        }

        // Locked: even the correct current password is not verified.
        var locked = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Pw, newPassword = NewPw });
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Equal("locked_out", (await ReadJsonAsync(locked)).GetProperty("code").GetString());

        var login = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "fl", password = Pw });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("locked_out", (await ReadJsonAsync(login)).GetProperty("code").GetString());

        // Lockout does not end sessions that are already open.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_Returns400()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "fl", Pw, "Frontliner");
        var client = await LoginAsAsync(app, "fl", Pw);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Pw, newPassword = Pw });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("different", (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChangePassword_TooShort_Returns400()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "fl", Pw, "Frontliner");
        var client = await LoginAsAsync(app, "fl", Pw);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Pw, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at least", (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Me_ReturnsActiveRolesOnly_AndNoHash()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        var id = await CreateUserAsync(app, "sw", Pw, "Frontliner", "Social Worker");
        await using (var db = app.CreateDbContext())
        {
            var swRoleId = await db.Roles.Where(r => r.RoleName == "Social Worker").Select(r => r.RoleId).SingleAsync();
            var ur = await db.UserRoles.SingleAsync(x => x.UserId == id && x.RoleId == swRoleId);
            ur.Status = false;
            await db.SaveChangesAsync();
        }
        var client = await LoginAsAsync(app, "sw", Pw);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(raw);
        var roles = doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(["Frontliner"], roles);
        Assert.Equal("sw", doc.RootElement.GetProperty("username").GetString());
    }
}
