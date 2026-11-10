using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Claims.API.Auth;

public class TokenService
{
  private readonly JwtOptions _options;
  private readonly TimeProvider _timeProvider;
  private readonly SigningCredentials _credentials;
  private readonly JsonWebTokenHandler _handler = new();

  public TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
  {
    _options = options.Value;
    _timeProvider = timeProvider;
    _credentials = new SigningCredentials(
      new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
      SecurityAlgorithms.HmacSha256);
  }

  public (string Token, DateTime ExpiresAt) CreateToken(int userId)
  {
    var now = _timeProvider.GetUtcNow();
    var lifetime = TimeSpan.FromMinutes(_options.LifetimeMinutes);

    var descriptor = new SecurityTokenDescriptor
    {
      Issuer = _options.Issuer,
      Audience = _options.Audience,
      Subject = new ClaimsIdentity(
      [
        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
      ]),
      IssuedAt = now.UtcDateTime,
      NotBefore = now.UtcDateTime,
      Expires = (now + lifetime).UtcDateTime,
      SigningCredentials = _credentials,
    };

    var token = _handler.CreateToken(descriptor);
    return (token, _timeProvider.GetLocalNow().DateTime + lifetime);
  }
}
