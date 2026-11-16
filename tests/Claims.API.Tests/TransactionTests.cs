using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Common;
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

  [Fact]
  public async Task SaveTransaction_AssignsQueueNumbersAndResetsTheNextDay()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    var today = app.Clock.GetUtcNow().UtcDateTime.AddHours(8).Date;
    await using (var db = app.CreateDbContext())
    {
      db.Transactions.Add(new Transaction
      {
        QueueNo = 25,
        DateCreated = today.AddDays(-1).AddHours(12),
        Status = true,
      });
      await db.SaveChangesAsync();
    }

    var firstResponse = await client.PostAsJsonAsync("/api/transactions/save", new { refNo = "QUEUE-1" });
    var first = await firstResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();
    var secondResponse = await client.PostAsJsonAsync("/api/transactions/save", new { refNo = "QUEUE-2" });
    var second = await secondResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    Assert.Equal((short)1, first!.QueueNo);
    Assert.Equal((short)2, second!.QueueNo);

    app.Clock.Advance(TimeSpan.FromDays(1));
    using var nextDayClient = await LoginAsAsync(app, "worker", Password);
    var nextDayResponse = await nextDayClient.PostAsJsonAsync(
      "/api/transactions/save",
      new { refNo = "QUEUE-NEXT-DAY" });
    Assert.Equal(HttpStatusCode.Created, nextDayResponse.StatusCode);
    var nextDay = await nextDayResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal((short)1, nextDay!.QueueNo);

    var updateResponse = await nextDayClient.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId = first.TransactionId,
      refNo = "QUEUE-1-UPDATED",
    });
    var updated = await nextDayClient.GetFromJsonAsync<TransactionResponseDto>(
      $"/api/transactions/{first.TransactionId}");

    Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
    Assert.Equal((short)1, updated!.QueueNo);
  }

  [Fact]
  public async Task GetAll_IncludesLinkedPatientAndRepresentativeNames()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    int transactionId;
    await using (var db = app.CreateDbContext())
    {
      var worker = await db.Users.SingleAsync(user => user.UserId == userId);
      worker.Firstname = "Ana";
      worker.Middlename = "Maria";
      worker.Lastname = "Santos";

      var patient = new Person { FirstName = "Maria", LastName = "Santos", Status = true };
      var representative = new Person { FirstName = "Juan", MiddleName = "Dela", LastName = "Cruz", Status = true };
      db.People.AddRange(patient, representative);
      await db.SaveChangesAsync();
      var newTransaction = new Transaction
      {
        PatientPersonId = patient.PersonId,
        RepresentativePersonId = representative.PersonId,
        SocialWorkerUserId = userId,
        Status = true,
      };
      db.Transactions.Add(newTransaction);
      await db.SaveChangesAsync();
      transactionId = newTransaction.TransactionId;
    }

    var result = await client.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions");

    Assert.NotNull(result);
    var transaction = Assert.Single(result.Items);
    Assert.Equal("Maria Santos", transaction.PatientPersonName);
    Assert.Equal("Juan Dela Cruz", transaction.RepresentativePersonName);
    Assert.Equal("Ana Maria Santos", transaction.SocialWorkerUserFullName);

    var detail = await client.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{transactionId}");
    Assert.Equal("Ana Maria Santos", detail!.SocialWorkerUserFullName);

    var patientSearch = Uri.EscapeDataString("Maria Santos");
    var patientMatches = await client.GetFromJsonAsync<PagedResult<TransactionResponseDto>>(
      $"/api/transactions?search={patientSearch}");
    var representativeSearch = Uri.EscapeDataString("Juan Dela Cruz");
    var representativeMatches = await client.GetFromJsonAsync<PagedResult<TransactionResponseDto>>(
      $"/api/transactions?search={representativeSearch}");

    Assert.Single(patientMatches!.Items);
    Assert.Single(representativeMatches!.Items);
  }

  [Fact]
  public async Task CheckoutTransaction_AssignsCurrentUserAndReturnsTransaction()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    int transactionId;
    await using (var db = app.CreateDbContext())
    {
      var worker = await db.Users.SingleAsync(user => user.UserId == userId);
      worker.Firstname = "Ana";
      worker.Middlename = "Maria";
      worker.Lastname = "Santos";

      var transaction = new Transaction { Status = true };
      db.Transactions.Add(transaction);
      await db.SaveChangesAsync();
      transactionId = transaction.TransactionId;
    }

    var response = await client.PostAsJsonAsync(
      "/api/transactions/checkout-social-worker",
      new { transactionId });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(result);
    Assert.Equal(userId, result.SocialWorkerUserId);
    Assert.Equal("Ana Maria Santos", result.SocialWorkerUserFullName);

    await using var verify = app.CreateDbContext();
    var saved = await verify.Transactions.SingleAsync(item => item.TransactionId == transactionId);
    Assert.Equal(userId, saved.SocialWorkerUserId);
    Assert.Equal(app.Clock.GetUtcNow().UtcDateTime.AddHours(8), saved.DateLastUpdated);

    var missing = await client.PostAsJsonAsync(
      "/api/transactions/checkout-social-worker",
      new { transactionId = int.MaxValue });
    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
  }
}
