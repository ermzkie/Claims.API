using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class LoginTests
{
    private const string Pw = "Str0ng-Passw0rd!x";

    private static Task<HttpResponseMessage> PostLogin(HttpClient c, string? username, string? password) =>
        c.PostAsJsonAsync("/api/auth/login", new { username, password });

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndUser()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var client = app.CreateClient();

        var response = await PostLogin(client, "maria", Pw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("token").GetString()));
        var roles = root.GetProperty("user").GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["Frontliner"], roles);
        var expiresAt = root.GetProperty("expiresAt").GetDateTime();
        var expected = app.Clock.GetLocalNow().DateTime.AddMinutes(480);
        Assert.True(Math.Abs((expiresAt - expected).TotalMinutes) <= 1);
        // "mustChangePassword" legitimately contains the word, so look for password/hash properties specifically.
        Assert.DoesNotContain("\"password\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", body);
    }

    [Fact]
    public async Task Login_SetsLastLoginDate_AndResetsFailures()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var client = app.CreateClient();
        await PostLogin(client, "maria", "wrong");

        var response = await PostLogin(client, "maria", Pw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = app.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "maria");
        Assert.NotNull(user.LastLoginDate);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public async Task Login_UsernameIsCaseAndSpaceInsensitive()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");

        var response = await PostLogin(app.CreateClient(), " MARIA ", Pw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyFields_Returns400()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        var client = app.CreateClient();

        var r1 = await PostLogin(client, "", "x");
        var r2 = await client.PostAsJsonAsync("/api/auth/login", new { username = "maria" });

        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownUser_GiveIdenticalResponse()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var client = app.CreateClient();

        var wrong = await PostLogin(client, "maria", "wrong-password");
        var unknown = await PostLogin(client, "nobody", "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsByteArrayAsync(), await unknown.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Login_Deactivated_CorrectPassword_GivesSameResponseAsWrongPassword()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, ["Frontliner"], active: false);
        await CreateUserAsync(app, "jose", Pw, "Frontliner");
        var client = app.CreateClient();

        var deactivated = await PostLogin(client, "maria", Pw);
        var wrong = await PostLogin(client, "jose", "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, deactivated.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsByteArrayAsync(), await deactivated.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Login_FiveFailures_LocksFor15Minutes()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var client = app.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var r = await PostLogin(client, "maria", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        var locked = await PostLogin(client, "maria", Pw);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        using (var json = JsonDocument.Parse(await locked.Content.ReadAsStringAsync()))
        {
            Assert.Equal("locked_out", json.RootElement.GetProperty("code").GetString());
        }

        app.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var ok = await PostLogin(client, "maria", Pw);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Login_FourFailuresThenSuccess_ResetsCounter()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var client = app.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            await PostLogin(client, "maria", "wrong-password");
        }
        var ok = await PostLogin(client, "maria", Pw);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await using var db = app.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "maria");
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task Login_Token_SubClaimIsUserId()
    {
        await using var app = new ClaimsApiFactory();
        await SeedRolesAsync(app);
        var userId = await CreateUserAsync(app, "maria", Pw, "Frontliner");
        var response = await PostLogin(app.CreateClient(), "maria", Pw);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString()!;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.DoesNotContain(jwt.Claims, c => c.Type is "role" or "roles" || c.Type.EndsWith("/role"));
    }
}
