using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
      refNo = "REF-41",
      amount = 125.50m,
    });

    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await create.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(created);
    Assert.True(created.TransactionId > 0);
    Assert.Equal(userId, created.SocialWorkerUserId);
    Assert.Equal(userId, created.CreatedBy);
    Assert.True(created.Status);
    var transactionId = created.TransactionId;

    var update = await client.PostAsJsonAsync("/api/transactions", new
    {
      transactionId,
      refNo = "REF-41-UPDATED",
      amount = 200m,
      isReleased = true,
    });
    Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

    var updated = await client.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{transactionId}");
    Assert.Equal("REF-41-UPDATED", updated!.RefNo);
    Assert.Equal(200m, updated.Amount);
    Assert.True(updated.IsReleased);
    Assert.Equal(userId, updated.LastUpdatedBy);

    var delete = await client.DeleteAsync($"/api/transactions/{transactionId}");
    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

    var deleted = await client.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{transactionId}");
    Assert.False(deleted!.Status);
  }

  [Fact]
  public async Task SaveTransaction_SavesAndUpdatesNatureIds()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    short firstNatureId;
    short secondNatureId;
    await using (var db = app.CreateDbContext())
    {
      var firstNature = new NatureOfService { NatureDesc = "Medical", Status = true };
      var secondNature = new NatureOfService { NatureDesc = "Transportation", Status = true };
      db.NatureOfServices.AddRange(firstNature, secondNature);
      await db.SaveChangesAsync();
      firstNatureId = firstNature.NatureId;
      secondNatureId = secondNature.NatureId;
    }

    var create = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      refNo = "REF-NATURE",
      natureIds = new[] { firstNatureId, secondNatureId },
    });
    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await create.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(created);
    Assert.Equal(new[] { firstNatureId, secondNatureId }, created.NatureIds.OrderBy(x => x));

    await using (var db = app.CreateDbContext())
    {
      var savedNatureIds = await db.TransactionServices
        .Where(x => x.TransactionId == created.TransactionId)
        .Select(x => x.NatureId)
        .ToListAsync();
      Assert.Equal(new short?[] { firstNatureId, secondNatureId }, savedNatureIds.OrderBy(x => x));
    }

    var update = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId = created.TransactionId,
      natureIds = new[] { secondNatureId },
    });
    Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

    var fetched = await client.GetFromJsonAsync<TransactionResponseDto>(
      $"/api/transactions/{created.TransactionId}");
    Assert.Equal(new[] { secondNatureId }, fetched!.NatureIds);

    await using var updatedDb = app.CreateDbContext();
    var updatedNatureIds = await updatedDb.TransactionServices
      .Where(x => x.TransactionId == created.TransactionId)
      .Select(x => x.NatureId)
      .ToListAsync();
    Assert.Equal(new short?[] { secondNatureId }, updatedNatureIds);
  }
}
