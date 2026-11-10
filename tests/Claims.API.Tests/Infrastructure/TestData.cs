using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Tests.Infrastructure;

public static class TestData
{
    private static readonly (short Id, string Name)[] Roles =
    [
        (1, "Super Administrator"),
        (2, "Administrator"),
        (3, "Frontliner"),
        (4, "Social Worker"),
    ];

    public static async Task SeedRolesAsync(ClaimsApiFactory app)
    {
        await using var db = app.CreateDbContext();
        foreach (var (id, name) in Roles)
        {
            if (!await db.Roles.AnyAsync(r => r.RoleId == id))
            {
                db.Roles.Add(new Role { RoleId = id, RoleName = name });
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task<int> CreateUserAsync(
        ClaimsApiFactory app,
        string username,
        string password,
        params string[] roleNames)
        => await CreateUserAsync(app, username, password, roleNames, active: true, mustChangePassword: false);

    public static async Task<int> CreateUserAsync(
        ClaimsApiFactory app,
        string username,
        string password,
        string[] roleNames,
        bool active = true,
        bool mustChangePassword = false)
    {
        await using var db = app.CreateDbContext();
        var user = new User
        {
            Username = username,
            MustChangePassword = mustChangePassword,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);

        var roleIds = await db.Roles
            .Where(r => r.RoleName != null && roleNames.Contains(r.RoleName))
            .Select(r => r.RoleId)
            .ToListAsync();
        foreach (var roleId in roleIds)
        {
            user.UserRoles.Add(new UserRole { RoleId = roleId });
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (!active)
        {
            // EF treats false as "unset" for a property with HasDefaultValue(true), so flip it after the insert.
            user.Status = false;
            await db.SaveChangesAsync();
        }

        return user.UserId;
    }

    public static async Task<HttpClient> LoginAsAsync(ClaimsApiFactory app, string username, string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
