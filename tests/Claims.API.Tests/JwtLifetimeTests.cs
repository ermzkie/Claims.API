using Claims.API.Auth;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Claims.API.Tests;

public class JwtLifetimeTests
{
  private const string Key = "test-signing-key-0123456789-abcdefghijklmnop";

  private sealed class Env(string name) : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Claims.API";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
  }

  private static bool Valid(string environment, int minutes) =>
    new JwtOptionsValidator(new Env(environment))
      .Validate(null, new JwtOptions { Issuer = "i", Audience = "a", SigningKey = Key, LifetimeMinutes = minutes })
      .Succeeded;

  [Theory]
  [InlineData("Development", 43200, true)]   // 30 days, for long local sessions
  [InlineData("Development", 43201, false)]
  [InlineData("Production", 1440, true)]     // 24 hours, the limit everywhere else
  [InlineData("Production", 1441, false)]
  [InlineData("Production", 43200, false)]
  [InlineData("Staging", 43200, false)]
  [InlineData("Production", 0, false)]
  public void Lifetime_LongOnlyInDevelopment(string environment, int minutes, bool expected)
  {
    Assert.Equal(expected, Valid(environment, minutes));
  }
}
