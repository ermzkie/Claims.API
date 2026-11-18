using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class TransactionSaveTests
{
  private const string Password = "Str0ng-Passw0rd!x";

  private static async Task<(ClaimsApiFactory App, HttpClient Client, short LaneId)> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "worker", Password, "Frontliner");
    var client = await LoginAsAsync(app, "worker", Password);
    await using var db = app.CreateDbContext();
    var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
    db.LaneTypes.Add(lane);
    await db.SaveChangesAsync();
    return (app, client, lane.LaneTypeId);
  }

  [Fact]
  public async Task SaveTransaction_UtcDateIsSavedAsPhilippineTime()
  {
    var (app, client, laneId) = await SetupAsync();
    await using var _ = app;

    // 7:30 am in the office, as toISOString() sends it
    var response = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      date = "2026-11-16T23:30:00.000Z",
      laneTypeId = laneId,
    });
    var saved = await response.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(new DateTime(2026, 11, 17, 7, 30, 0), saved!.Date);

    var plain = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      date = "2026-11-17T09:00",
      laneTypeId = laneId,
    });
    var plainSaved = await plain.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(new DateTime(2026, 11, 17, 9, 0, 0), plainSaved!.Date);
    // same day, so the same queue
    Assert.Equal((short)2, plainSaved.QueueNo);
  }

  [Fact]
  public async Task SaveTransaction_UnknownIdIsNotFoundAndCreatesNothing()
  {
    var (app, client, laneId) = await SetupAsync();
    await using var _ = app;

    var response = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId = 999999,
      date = "2026-11-17T09:00",
      laneTypeId = laneId,
    });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    await using var db = app.CreateDbContext();
    Assert.Equal(0, await db.Transactions.CountAsync());
  }
}
