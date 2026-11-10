using System.Globalization;
using System.Security.Claims;
using Claims.API.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Auth;

/// <summary>
/// Runs on every authenticated request: the token only proves identity; the user's
/// current status and roles always come from the database.
/// </summary>
public static class CurrentUserValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            context.Fail("invalid subject");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var ct = context.HttpContext.RequestAborted;

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.Username, u.Status, u.MustChangePassword })
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.Status)
        {
            context.Fail("inactive");
            return;
        }

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Status)
            .Join(db.Roles.Where(r => r.RoleName != null), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.RoleName!)
            .ToListAsync(ct);

        var identity = new ClaimsIdentity("Bearer", "name", ClaimTypes.Role);
        identity.AddClaim(new Claim("sub", userId.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim("name", user.Username));
        foreach (var role in roles.Distinct())
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        identity.AddClaim(new Claim("must_change_password", user.MustChangePassword ? "true" : "false"));

        context.Principal = new ClaimsPrincipal(identity);
    }
}
