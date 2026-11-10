using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Claims.API.Auth;

public static class AuthPolicies
{
  public const string UserAdmin = "UserAdmin";
  public const string AllowPendingPasswordChange = "AllowPendingPasswordChange";

  public static IServiceCollection AddClaimsAuthentication(this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<JwtOptions>()
      .Bind(configuration.GetSection(JwtOptions.SectionName))
      .ValidateDataAnnotations()
      .ValidateOnStart();
    services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

    services.AddSingleton<TokenService>();
    services.AddScoped<AuthService>();

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer();

    services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
      .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
      {
        var o = jwt.Value;
        bearer.MapInboundClaims = false;
        bearer.IncludeErrorDetails = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
          ValidateIssuer = true,
          ValidIssuer = o.Issuer,
          ValidateAudience = true,
          ValidAudience = o.Audience,
          ValidateLifetime = true,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)),
          ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
          ClockSkew = TimeSpan.FromMinutes(1),
        };
      });

    services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
      .Configure<TimeProvider>((bearer, time) =>
      {
        bearer.TimeProvider = time;
        // The handler's own lifetime check ignores JwtBearerOptions.TimeProvider, so compare against it explicitly.
        var skew = bearer.TokenValidationParameters.ClockSkew;
        bearer.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
        {
          var now = time.GetUtcNow().UtcDateTime;
          return expires is not null && notBefore is not null
            ? notBefore.Value <= now + skew && expires.Value > now - skew
            : false;
        };
        bearer.Events ??= new JwtBearerEvents();
        bearer.Events.OnTokenValidated = CurrentUserValidator.ValidateAsync;
      });

    // One shared base: authenticated and not pending a password change. Everything except
    // AllowPendingPasswordChange derives from it, so the gate cannot be missed.
    var basePolicy = new AuthorizationPolicyBuilder()
      .RequireAuthenticatedUser()
      .AddRequirements(new PasswordChangeRequirement())
      .Build();

    services.AddSingleton<IAuthorizationHandler, PasswordChangeHandler>();
    services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();

    services.AddAuthorizationBuilder()
      .SetDefaultPolicy(new AuthorizationPolicyBuilder().Combine(basePolicy).Build())
      .SetFallbackPolicy(new AuthorizationPolicyBuilder().Combine(basePolicy).Build())
      .AddPolicy(UserAdmin, new AuthorizationPolicyBuilder()
        .Combine(basePolicy)
        .RequireRole(RoleNames.SuperAdministrator, RoleNames.Administrator)
        .Build())
      .AddPolicy(AllowPendingPasswordChange, policy => policy
        .RequireAuthenticatedUser());

    return services;
  }
}
