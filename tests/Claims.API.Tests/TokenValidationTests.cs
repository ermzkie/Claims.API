using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class TokenValidationTests
{
    private const string Pw = "Str0ng-Passw0rd!x";

    private static string Sign(
        int userId,
        DateTime? notBefore,
        DateTime? expires,
        string key = ClaimsApiFactory.SigningKey)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "Claims.API",
            Audience = "Claims.Web",
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task<HttpStatusCode> ProbeAsync(ClaimsApiFactory app, string token)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/auth/me")).StatusCode;
    }

    private static async Task<int> ArrangeUserAsync(ClaimsApiFactory app)
    {
        await SeedRolesAsync(app);
        return await CreateUserAsync(app, "fl", Pw, "Frontliner");
    }

    [Fact]
    public async Task ValidHandSignedToken_Returns200()
    {
        await using var app = new ClaimsApiFactory();
        var id = await ArrangeUserAsync(app);
        var now = app.Clock.GetUtcNow().UtcDateTime;

        var token = Sign(id, now, now.AddMinutes(10));

        Assert.Equal(HttpStatusCode.OK, await ProbeAsync(app, token));
    }

    [Fact]
    public async Task Token_WithoutExp_Returns401()
    {
        await using var app = new ClaimsApiFactory();
        var id = await ArrangeUserAsync(app);
        var now = app.Clock.GetUtcNow().UtcDateTime;

        var token = Sign(id, now, null);

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(app, token));
    }

    [Fact]
    public async Task Token_WithFutureNbf_Returns401()
    {
        await using var app = new ClaimsApiFactory();
        var id = await ArrangeUserAsync(app);
        var now = app.Clock.GetUtcNow().UtcDateTime;

        var token = Sign(id, now.AddMinutes(10), now.AddMinutes(60));

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(app, token));
    }

    [Fact]
    public async Task Token_ForDeletedUser_Returns401()
    {
        await using var app = new ClaimsApiFactory();
        var id = await ArrangeUserAsync(app);
        var now = app.Clock.GetUtcNow().UtcDateTime;
        var token = Sign(id, now, now.AddMinutes(10));
        Assert.Equal(HttpStatusCode.OK, await ProbeAsync(app, token));

        await using (var db = app.CreateDbContext())
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync());
            db.Users.Remove(await db.Users.SingleAsync(u => u.UserId == id));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(app, token));
    }

    [Fact]
    public async Task Token_SignedWithOtherKey_Returns401()
    {
        await using var app = new ClaimsApiFactory();
        var id = await ArrangeUserAsync(app);
        var now = app.Clock.GetUtcNow().UtcDateTime;

        var token = Sign(id, now, now.AddMinutes(10), key: "a-completely-different-signing-key-0123456789");

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(app, token));
    }
}
