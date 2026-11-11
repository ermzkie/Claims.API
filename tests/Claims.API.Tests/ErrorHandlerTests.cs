using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.API.Infrastructure;
using Claims.API.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class ErrorHandlerTests
{
  private const string Pw = "Str0ng-Passw0rd!x";
  private const string SecretDetail = "internal-detail-that-must-not-leak";

  // Makes the login endpoint crash with an unexpected exception.
  private sealed class BrokenClock : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException(SecretDetail);
  }

  private static async Task<HttpResponseMessage> CrashLoginAsync(string environment)
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "fl", Pw, "Frontliner");

    var broken = app.WithWebHostBuilder(b =>
    {
      b.UseEnvironment(environment);
      b.ConfigureTestServices(services =>
      {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new BrokenClock());
      });
    });

    return await broken.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "fl", password = Pw });
  }

  [Theory]
  [InlineData("Testing")]
  [InlineData("Development")]
  public async Task UnexpectedError_Returns500Json_WithReference_AndNoDetails(string environment)
  {
    var response = await CrashLoginAsync(environment);

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

    var text = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain(SecretDetail, text);
    Assert.DoesNotContain("InvalidOperationException", text);
    Assert.DoesNotContain(" at ", text); // no stack trace lines

    var body = JsonDocument.Parse(text).RootElement;
    Assert.Equal("server_error", body.GetProperty("code").GetString());
    Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
  }

  [Fact]
  public void Describe_MapsKnownFailures()
  {
    Assert.Equal(StatusCodes.Status409Conflict,
      GlobalExceptionHandler.Describe(new DbUpdateConcurrencyException("x")).Status);

    Assert.Equal(StatusCodes.Status503ServiceUnavailable,
      GlobalExceptionHandler.Describe(new DbUpdateException("x", new TimeoutException())).Status);

    Assert.Equal(StatusCodes.Status413PayloadTooLarge,
      GlobalExceptionHandler.Describe(new BadHttpRequestException("x", StatusCodes.Status413PayloadTooLarge)).Status);

    var other = GlobalExceptionHandler.Describe(new NullReferenceException());
    Assert.Equal(StatusCodes.Status500InternalServerError, other.Status);
    Assert.Equal("server_error", other.Code);
  }

  [Fact]
  public async Task NormalErrors_AreUnchanged()
  {
    await using var app = new ClaimsApiFactory();
    var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "whatever123" });
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("Invalid username or password.", body.GetProperty("message").GetString());
  }
}
