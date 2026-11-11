using Microsoft.OpenApi;

namespace Claims.API.Auth;

// API documentation page (Swagger UI), for development only.
public static class OpenApiSetup
{
  public static IServiceCollection AddClaimsOpenApi(this IServiceCollection services)
  {
    services.AddOpenApi(options =>
    {
      options.AddDocumentTransformer((document, context, cancellationToken) =>
      {
        document.Info.Title = "Claims.API";
        document.Info.Description =
          "Log in with POST /api/auth/login, copy the token, then click Authorize and paste it.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
          Type = SecuritySchemeType.Http,
          Scheme = "bearer",
          BearerFormat = "JWT",
          Description = "Token from POST /api/auth/login (without the word Bearer).",
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
          [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
        });

        return Task.CompletedTask;
      });
    });

    return services;
  }

  // Only call this in Development. The page and the document are reachable without logging in;
  // every API endpoint still needs the token.
  public static WebApplication UseClaimsSwaggerUi(this WebApplication app)
  {
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
      options.SwaggerEndpoint("/openapi/v1.json", "Claims.API");
      options.DocumentTitle = "Claims.API";
    });

    return app;
  }
}
