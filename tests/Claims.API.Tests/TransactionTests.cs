using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Transactions;
using Claims.API.Tests.Infrastructure;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class TransactionTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  [Fact]
  public async Task Transactions_CanBeCreatedReadUpdatedAndSoftDeleted()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    var create = await client.PostAsJsonAsync("/api/transactions", new
    {
      transactionId = 41,
      refNo = "REF-41",
      amount = 125.50m,
    });

    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await create.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(created);
    Assert.Equal(userId, created.SocialWorkerUserId);
    Assert.Equal(userId, created.CreatedBy);
    Assert.True(created.Status);

    var duplicate = await client.PostAsJsonAsync("/api/transactions", new { transactionId = 41 });
    Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

    var update = await client.PutAsJsonAsync("/api/transactions/41", new
    {
      refNo = "REF-41-UPDATED",
      amount = 200m,
      isReleased = true,
    });
    Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

    var updated = await client.GetFromJsonAsync<TransactionResponseDto>("/api/transactions/41");
    Assert.Equal("REF-41-UPDATED", updated!.RefNo);
    Assert.Equal(200m, updated.Amount);
    Assert.True(updated.IsReleased);
    Assert.Equal(userId, updated.LastUpdatedBy);

    var delete = await client.DeleteAsync("/api/transactions/41");
    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

    var deleted = await client.GetFromJsonAsync<TransactionResponseDto>("/api/transactions/41");
    Assert.False(deleted!.Status);
  }
}
