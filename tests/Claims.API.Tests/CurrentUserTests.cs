using System.Globalization;
using System.Security.Claims;
using Claims.API.Auth;
using Claims.API.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class CurrentUserTests
{
  private const string Pw = "Str0ng-Passw0rd!x";

  // Same claims the token check puts on every authenticated request.
  private static ClaimsPrincipal Principal(int id, string username, params string[] roles)
  {
    var identity = new ClaimsIdentity("Bearer", "name", ClaimTypes.Role);
    identity.AddClaim(new Claim("sub", id.ToString(CultureInfo.InvariantCulture)));
    identity.AddClaim(new Claim("name", username));
    foreach (var role in roles)
      identity.AddClaim(new Claim(ClaimTypes.Role, role));
    return new ClaimsPrincipal(identity);
  }

  private static CurrentUser For(IServiceScope scope, ClaimsPrincipal user) =>
    new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } },
      scope.ServiceProvider.GetRequiredService<AuthService>());

  [Fact]
  public async Task ReadsIdUsernameAndRoles_FromTheRequest()
  {
    await using var app = new ClaimsApiFactory();
    using var scope = app.Services.CreateScope();

    var me = For(scope, Principal(7, "maria", RoleNames.Frontliner, RoleNames.SocialWorker));

    Assert.True(me.IsAuthenticated);
    Assert.Equal(7, me.Id);
    Assert.Equal("maria", me.Username);
    Assert.Equal([RoleNames.Frontliner, RoleNames.SocialWorker], me.Roles);
    Assert.True(me.IsInRole(RoleNames.SocialWorker));
    Assert.False(me.IsInRole(RoleNames.Administrator));
  }

  [Fact]
  public async Task GetProfileAsync_LoadsNames_OncePerRequest()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var id = await CreateUserAsync(app, "maria", Pw, "Frontliner");
    await using (var db = app.CreateDbContext())
    {
      var user = await db.Users.FindAsync(id);
      user!.Firstname = "Maria";
      user.Lastname = "Santos";
      await db.SaveChangesAsync();
    }
    using var scope = app.Services.CreateScope();
    var me = For(scope, Principal(id, "maria", "Frontliner"));

    var first = await me.GetProfileAsync();
    var second = await me.GetProfileAsync();

    Assert.NotNull(first);
    Assert.Equal("Maria", first.Firstname);
    Assert.Equal("Santos", first.Lastname);
    Assert.Equal(["Frontliner"], first.Roles);
    Assert.Same(first, second);
  }

  [Fact]
  public async Task Id_WithoutLogin_Throws()
  {
    await using var app = new ClaimsApiFactory();
    using var scope = app.Services.CreateScope();

    var me = For(scope, new ClaimsPrincipal(new ClaimsIdentity()));

    Assert.False(me.IsAuthenticated);
    Assert.Throws<InvalidOperationException>(() => me.Id);
  }

  [Fact]
  public async Task IsRegistered_ForInjection()
  {
    await using var app = new ClaimsApiFactory();
    using var scope = app.Services.CreateScope();

    Assert.IsType<CurrentUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
  }
}
