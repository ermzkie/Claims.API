using System.ComponentModel.DataAnnotations;
using System.Text;
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

    [Range(1, 1440)]
    public int LifetimeMinutes { get; set; } = 480;
}

public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrEmpty(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            return ValidateOptionsResult.Fail("Jwt:SigningKey must be configured and at least 32 bytes long.");
        }
        return ValidateOptionsResult.Success;
    }
}
