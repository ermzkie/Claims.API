using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class LoginWarningTests
{
  private const string Pw = "Str0ng-Passw0rd!x";
  private const string Plain = "Invalid username or password.";

  private static async Task<(HttpStatusCode Status, string Message, string? Code)> Login(
    HttpClient client, string username, string password)
  {
    var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var code = json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    return (response.StatusCode, json.RootElement.GetProperty("message").GetString()!, code);
  }

  private static async Task<ClaimsApiFactory> StartAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "maria", Pw, "Frontliner");
    await CreateUserAsync(app, "jose", Pw, ["Frontliner"], active: false);
    return app;
  }

  [Fact]
  public async Task FirstSixFailures_PlainMessage_ThenCountdown()
  {
    await using var app = await StartAsync();
    var client = app.CreateClient();

    for (var i = 0; i < 6; i++)
    {
      Assert.Equal(Plain, (await Login(client, "maria", "wrong-password")).Message);
    }

    Assert.Equal(
      "Invalid username or password. 3 attempts left before the account is locked for 5 minutes.",
      (await Login(client, "maria", "wrong-password")).Message);
    Assert.Contains("2 attempts left", (await Login(client, "maria", "wrong-password")).Message);
    Assert.Contains("1 attempt left", (await Login(client, "maria", "wrong-password")).Message);
  }

  [Fact]
  public async Task TenthFailure_SaysLocked_WithMinutesLeft()
  {
    await using var app = await StartAsync();
    var client = app.CreateClient();
    for (var i = 0; i < 9; i++)
    {
      await Login(client, "maria", "wrong-password");
    }

    var tenth = await Login(client, "maria", "wrong-password");
    Assert.Equal(HttpStatusCode.Unauthorized, tenth.Status);
    Assert.Equal("locked_out", tenth.Code);
    Assert.Equal("Too many failed attempts. Try again in 5 minutes.", tenth.Message);

    app.Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));
    Assert.Equal("Too many failed attempts. Try again in 2 minutes.", (await Login(client, "maria", Pw)).Message);

    app.Clock.Advance(TimeSpan.FromMinutes(1));
    Assert.Equal("Too many failed attempts. Try again in 1 minute.", (await Login(client, "maria", Pw)).Message);
  }

  [Fact]
  public async Task UnknownUser_And_Deactivated_GetTheSameSequenceAsRealUser()
  {
    await using var app = await StartAsync();
    var client = app.CreateClient();

    for (var i = 0; i < 12; i++)
    {
      var real = await Login(client, "maria", "wrong-password");
      var unknown = await Login(client, "nobody", "wrong-password");
      var deactivated = await Login(client, "jose", Pw);

      Assert.Equal(real, unknown);
      Assert.Equal(real, deactivated);
    }
  }

  [Fact]
  public async Task Counter_DoesNotResetWithTime_ForRealOrUnknownUsers()
  {
    await using var app = await StartAsync();
    var client = app.CreateClient();
    for (var i = 0; i < 6; i++)
    {
      await Login(client, "maria", "wrong-password");
      await Login(client, "nobody", "wrong-password");
    }

    app.Clock.Advance(TimeSpan.FromHours(3));

    var real = await Login(client, "maria", "wrong-password");
    Assert.Contains("3 attempts left", real.Message);
    Assert.Equal(real, await Login(client, "nobody", "wrong-password"));
  }

  [Fact]
  public async Task UnknownUser_UnlocksAfterLockout()
  {
    await using var app = await StartAsync();
    var client = app.CreateClient();
    for (var i = 0; i < 10; i++)
    {
      await Login(client, "Nobody ", "wrong-password");
    }
    Assert.Equal("locked_out", (await Login(client, "nobody", "wrong-password")).Code);

    app.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

    Assert.Equal(Plain, (await Login(client, "nobody", "wrong-password")).Message);
  }
}
