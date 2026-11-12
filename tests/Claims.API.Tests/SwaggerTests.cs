using System.Net;
using Claims.API.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Claims.API.Tests;

public class SwaggerTests
{
  [Fact]
  public async Task Development_SwaggerPage_IsAvailableWithoutLogin()
  {
    await using var app = new ClaimsApiFactory();
    var client = app.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    var page = await client.GetAsync("/swagger/index.html");
    Assert.Equal(HttpStatusCode.OK, page.StatusCode);

    var document = await client.GetAsync("/openapi/v1.json");
    Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    var json = await document.Content.ReadAsStringAsync();
    Assert.Contains("\"bearer\"", json);
    Assert.Contains("/api/auth/login", json);
  }

  [Fact]
  public async Task Development_ApiEndpoints_StillNeedToken()
  {
    await using var app = new ClaimsApiFactory();
    var client = app.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    var response = await client.GetAsync("/api/auth/me");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Theory]
  [InlineData("/swagger/index.html")]
  [InlineData("/openapi/v1.json")]
  public async Task OutsideDevelopment_DocsAreNotServed(string path)
  {
    await using var app = new ClaimsApiFactory();
    var response = await app.CreateClient().GetAsync(path);
    Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
  }
}
