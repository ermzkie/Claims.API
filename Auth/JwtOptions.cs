using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Claims.API.Auth;

public class JwtOptions
{
  public const string SectionName = "Jwt";

  [Required]
  public string Issuer { get; set; } = string.Empty;

  [Required]
  public string Audience { get; set; } = string.Empty;

  public string SigningKey { get; set; } = string.Empty;

  public const int MaxLifetimeMinutes = 1440;
  public const int MaxDevelopmentLifetimeMinutes = 43200;

  public int LifetimeMinutes { get; set; } = 480;
}

public class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
  public ValidateOptionsResult Validate(string? name, JwtOptions options)
  {
    if (string.IsNullOrEmpty(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
    {
      return ValidateOptionsResult.Fail("Jwt:SigningKey must be configured and at least 32 bytes long.");
    }

    // Developers may keep a token for weeks on their own machine; anywhere else a leaked
    // token must stop working within a day.
    var max = environment.IsDevelopment()
      ? JwtOptions.MaxDevelopmentLifetimeMinutes
      : JwtOptions.MaxLifetimeMinutes;
    if (options.LifetimeMinutes < 1 || options.LifetimeMinutes > max)
    {
      return ValidateOptionsResult.Fail(
        $"Jwt:LifetimeMinutes must be between 1 and {max} in the {environment.EnvironmentName} environment.");
    }
    return ValidateOptionsResult.Success;
  }
}
