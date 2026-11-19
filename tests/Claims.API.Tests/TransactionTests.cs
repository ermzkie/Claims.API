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
    var frontlinerId = await CreateUserAsync(app, "front", Password, "Frontliner");
    var workerId = await CreateUserAsync(app, "worker", Password, "Social Worker");
    await CreateUserAsync(app, "admin", Password, "Administrator");
    using var front = await LoginAsAsync(app, "front", Password);
    using var worker = await LoginAsAsync(app, "worker", Password);
    using var admin = await LoginAsAsync(app, "admin", Password);

    short laneTypeId;
    int patientPersonId;
    await using (var db = app.CreateDbContext())
    {
      var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular" };
      var patient = new Person { FirstName = "Test", LastName = "Patient", Status = true };
      db.LaneTypes.Add(lane);
      db.People.Add(patient);
      await db.SaveChangesAsync();
      laneTypeId = lane.LaneTypeId;
      patientPersonId = patient.PersonId;
    }

    var create = await front.PostAsJsonAsync("/api/transactions/save", new
    {
      date = app.Clock.GetUtcNow().UtcDateTime.AddHours(8),
      laneTypeId,
      patientPersonId,
    });

    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await create.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(created);
    Assert.True(created.TransactionId > 0);
    Assert.Equal(frontlinerId, created.CreatedBy);
    Assert.True(created.Status);
    var transactionId = created.TransactionId;

    var update = await worker.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId,
      amount = 200m,
      isReleased = true,
    });
    Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

    var updated = await worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{transactionId}");
    Assert.Equal(200m, updated!.Amount);
    Assert.True(updated.IsReleased);
    Assert.Equal(workerId, updated.LastUpdatedBy);

    var whileReleased = await admin.DeleteAsync($"/api/transactions/{transactionId}?reason=Duplicate");
    Assert.Equal(HttpStatusCode.Conflict, whileReleased.StatusCode);

    var reopen = await admin.PostAsJsonAsync("/api/transactions/save", new { transactionId, isReleased = false });
    Assert.Equal(HttpStatusCode.NoContent, reopen.StatusCode);

    var noReason = await admin.DeleteAsync($"/api/transactions/{transactionId}");
    Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

    var delete = await admin.DeleteAsync($"/api/transactions/{transactionId}?reason=Duplicate");
    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

    var deleted = await admin.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{transactionId}");
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
    short laneTypeId;
    int patientPersonId;
    await using (var db = app.CreateDbContext())
    {
      var firstNature = new NatureOfService { NatureDesc = "Medical", Status = true };
      var secondNature = new NatureOfService { NatureDesc = "Transportation", Status = true };
      var laneType = new LaneType { LaneTypeCode = "NATURE", LaneTypeDesc = "Nature lane" };
      db.NatureOfServices.AddRange(firstNature, secondNature);
      db.LaneTypes.Add(laneType);
      var patient = new Person { FirstName = "Test", LastName = "Patient", Status = true };
      db.People.Add(patient);
      await db.SaveChangesAsync();
      patientPersonId = patient.PersonId;
      firstNatureId = firstNature.NatureId;
      secondNatureId = secondNature.NatureId;
      laneTypeId = laneType.LaneTypeId;
    }

    var create = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      refNo = "REF-NATURE",
      date = app.Clock.GetUtcNow().UtcDateTime.AddHours(8).Date,
      laneTypeId,
      patientPersonId,
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
      laneTypeId,
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
  public async Task TransactionResponses_IncludeRelatedDescriptions()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var userId = await CreateUserAsync(app, "worker", Password, "Social Worker");
    using var client = await LoginAsAsync(app, "worker", Password);

    short laneTypeId;
    short modeId = 71;
    short sourceId = 72;
    const int creditorId = 73;
    const int fundSourceId = 74;
    int patientPersonId;
    var transactionDate = app.Clock.GetUtcNow().UtcDateTime.AddHours(8).Date;
    await using (var db = app.CreateDbContext())
    {
      var laneType = new LaneType { LaneTypeCode = "FAST", LaneTypeDesc = "Fast lane" };
      var source = new SourceOfAssistance { SourceId = sourceId, SourceDesc = "Source description" };
      db.LaneTypes.Add(laneType);
      db.ModeOfAssistances.Add(new ModeOfAssistance { ModeId = modeId, ModeDesc = "Mode description" });
      db.SourceOfAssistances.Add(source);
      db.FundSources.Add(new FundSource
      {
        FundSourceId = fundSourceId,
        SourceId = sourceId,
        Year = (short)transactionDate.Year,
        Status = true,
      });
      db.Creditors.Add(new Creditor
      {
        CreditorId = creditorId,
        CreditorName = "Creditor name",
        Status = true,
      });
      var patient = new Person { FirstName = "Test", LastName = "Patient", Status = true };
      db.People.Add(patient);
      await db.SaveChangesAsync();
      laneTypeId = laneType.LaneTypeId;
      patientPersonId = patient.PersonId;
    }

    var response = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      date = transactionDate,
      laneTypeId,
      patientPersonId,
      modeId,
      fundSourceId,
      creditorId,
    });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var created = await response.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(created);
    AssertDescriptions(created);

    var fetched = await client.GetFromJsonAsync<TransactionResponseDto>(
      $"/api/transactions/{created.TransactionId}");
    AssertDescriptions(fetched);

    var all = await client.GetFromJsonAsync<PagedResult<TransactionResponseDto>>("/api/transactions");
    AssertDescriptions(Assert.Single(all!.Items));

    var checkedOutResponse = await client.PostAsync(
      $"/api/transactions/{created.TransactionId}/checkout-social-worker",
      content: null);
    Assert.Equal(HttpStatusCode.OK, checkedOutResponse.StatusCode);
    AssertDescriptions(await checkedOutResponse.Content.ReadFromJsonAsync<TransactionResponseDto>());

    var history = await client.GetFromJsonAsync<PagedResult<TransactionResponseDto>>(
      $"/api/persons/{patientPersonId}/transactions");
    AssertDescriptions(Assert.Single(history!.Items));

    await using var verify = app.CreateDbContext();
    var saved = await verify.Transactions.SingleAsync(
      transaction => transaction.TransactionId == created.TransactionId);
    Assert.Equal(laneTypeId, saved.LaneTypeId);
    Assert.Equal(userId, saved.SocialWorkerUserId);

    void AssertDescriptions(TransactionResponseDto? transaction)
    {
      Assert.NotNull(transaction);
      Assert.Equal(laneTypeId, transaction.LaneTypeId);
      Assert.Equal("FAST1", transaction.QueueNoCode);
      Assert.Equal("Fast lane", transaction.LaneTypeDesc);
      Assert.Equal("Creditor name", transaction.CreditorName);
      Assert.Equal("Mode description", transaction.ModeDesc);
      Assert.Equal("Source description", transaction.SourceDesc);
    }
  }

  [Fact]
  public async Task SaveTransaction_AssignsQueueNumbersAndResetsTheNextDay()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "worker", Password, "Frontliner");
    using var client = await LoginAsAsync(app, "worker", Password);

    var today = app.Clock.GetUtcNow().UtcDateTime.AddHours(8).Date;
    short firstLaneId;
    short secondLaneId;
    int patientPersonId;
    await using (var db = app.CreateDbContext())
    {
      var firstLane = new LaneType { LaneTypeCode = "A", LaneTypeDesc = "Lane A" };
      var secondLane = new LaneType { LaneTypeCode = "B", LaneTypeDesc = "Lane B" };
      db.LaneTypes.AddRange(firstLane, secondLane);
      await db.SaveChangesAsync();
      firstLaneId = firstLane.LaneTypeId;
      secondLaneId = secondLane.LaneTypeId;
      var patient = new Person { FirstName = "Test", LastName = "Patient", Status = true };
      db.People.Add(patient);
      await db.SaveChangesAsync();
      patientPersonId = patient.PersonId;

      db.Transactions.AddRange(new Transaction
      {
        QueueNo = 25,
        Date = today.AddHours(9),
        LaneTypeId = firstLaneId,
        DateCreated = today.AddDays(-1).AddHours(12),
        Status = true,
      }, new Transaction
      {
        QueueNo = 40,
        Date = today.AddHours(10),
        LaneTypeId = secondLaneId,
        DateCreated = today.AddDays(-2).AddHours(12),
        Status = true,
      });
      await db.SaveChangesAsync();
    }

    var firstResponse = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      refNo = "QUEUE-1",
      patientPersonId,
      date = today.AddHours(11),
      laneTypeId = firstLaneId,
    });
    var first = await firstResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();
    var secondResponse = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      refNo = "QUEUE-2",
      patientPersonId,
      date = today.AddHours(12),
      laneTypeId = firstLaneId,
    });
    var second = await secondResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();
    var otherLaneResponse = await client.PostAsJsonAsync("/api/transactions/save", new
    {
      refNo = "QUEUE-OTHER-LANE",
      patientPersonId,
      date = today.AddHours(13),
      laneTypeId = secondLaneId,
    });
    var otherLane = await otherLaneResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Created, otherLaneResponse.StatusCode);
    Assert.Equal((short)26, first!.QueueNo);
    Assert.Equal((short)27, second!.QueueNo);
    Assert.Equal((short)41, otherLane!.QueueNo);

    var missingDateResponse = await client.PostAsJsonAsync(
      "/api/transactions/save",
      new { laneTypeId = firstLaneId });
    Assert.Equal(HttpStatusCode.BadRequest, missingDateResponse.StatusCode);

    var missingLaneResponse = await client.PostAsJsonAsync(
      "/api/transactions/save",
      new { date = today });
    Assert.Equal(HttpStatusCode.BadRequest, missingLaneResponse.StatusCode);

    var invalidLaneResponse = await client.PostAsJsonAsync(
      "/api/transactions/save",
      new { date = today, laneTypeId = short.MaxValue });
    Assert.Equal(HttpStatusCode.BadRequest, invalidLaneResponse.StatusCode);

    app.Clock.Advance(TimeSpan.FromDays(1));
    using var nextDayClient = await LoginAsAsync(app, "worker", Password);
    var nextDayResponse = await nextDayClient.PostAsJsonAsync(
      "/api/transactions/save",
      new
      {
        refNo = "QUEUE-NEXT-DAY",
      patientPersonId,
        date = today.AddDays(1).AddHours(1),
        laneTypeId = firstLaneId,
      });
    Assert.Equal(HttpStatusCode.Created, nextDayResponse.StatusCode);
    var nextDay = await nextDayResponse.Content.ReadFromJsonAsync<TransactionResponseDto>();

    Assert.Equal((short)1, nextDay!.QueueNo);

    var updateResponse = await nextDayClient.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId = first.TransactionId,
      laneTypeId = firstLaneId,
      refNo = "QUEUE-1-UPDATED",
    });
    var updated = await nextDayClient.GetFromJsonAsync<TransactionResponseDto>(
      $"/api/transactions/{first.TransactionId}");

    Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
    Assert.Equal((short)26, updated!.QueueNo);
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
    var userId = await CreateUserAsync(app, "worker", Password, "Social Worker");
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

    var response = await client.PostAsync(
      $"/api/transactions/{transactionId}/checkout-social-worker", content: null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<TransactionResponseDto>();
    Assert.NotNull(result);
    Assert.Equal(userId, result.SocialWorkerUserId);
    Assert.Equal("Ana Maria Santos", result.SocialWorkerUserFullName);

    await using var verify = app.CreateDbContext();
    var saved = await verify.Transactions.SingleAsync(item => item.TransactionId == transactionId);
    Assert.Equal(userId, saved.SocialWorkerUserId);
    Assert.Equal(app.Clock.GetUtcNow().UtcDateTime.AddHours(8), saved.DateLastUpdated);

    var missing = await client.PostAsync(
      $"/api/transactions/{int.MaxValue}/checkout-social-worker", content: null);
    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
  }

  [Fact]
  public async Task SubmitTransactionForApproval_RequiresSocialWorkerOrAdminAndUpdatesApprovalStatus()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var workerId = await CreateUserAsync(app, "approval-worker", Password, "Social Worker");
    var existingWorkerId = await CreateUserAsync(app, "existing-worker", Password, "Social Worker");
    await CreateUserAsync(app, "approval-admin", Password, "Administrator");
    await CreateUserAsync(app, "approval-frontliner", Password, "Frontliner");
    using var worker = await LoginAsAsync(app, "approval-worker", Password);
    using var admin = await LoginAsAsync(app, "approval-admin", Password);
    using var frontliner = await LoginAsAsync(app, "approval-frontliner", Password);

    int workerTransactionId;
    int adminTransactionId;
    int forbiddenTransactionId;
    await using (var db = app.CreateDbContext())
    {
      db.ApprovalStatuses.AddRange(
        new ApprovalStatus { ApprovalStatusId = 1 },
        new ApprovalStatus { ApprovalStatusId = 2 });
      var workerTransaction = new Transaction
        { Status = true, ApprovalStatusId = 1, SocialWorkerUserId = existingWorkerId };
      var adminTransaction = new Transaction
        { Status = true, ApprovalStatusId = 1, SocialWorkerUserId = existingWorkerId };
      var forbiddenTransaction = new Transaction
        { Status = true, ApprovalStatusId = 1, SocialWorkerUserId = existingWorkerId };
      db.Transactions.AddRange(workerTransaction, adminTransaction, forbiddenTransaction);
      await db.SaveChangesAsync();
      workerTransactionId = workerTransaction.TransactionId;
      adminTransactionId = adminTransaction.TransactionId;
      forbiddenTransactionId = forbiddenTransaction.TransactionId;
    }

    var workerResponse = await worker.PostAsync(
      $"/api/transactions/{workerTransactionId}/submit-for-approval", content: null);
    Assert.Equal(HttpStatusCode.OK, workerResponse.StatusCode);

    var adminResponse = await admin.PostAsync(
      $"/api/transactions/{adminTransactionId}/submit-for-approval", content: null);
    Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

    var forbiddenResponse = await frontliner.PostAsync(
      $"/api/transactions/{forbiddenTransactionId}/submit-for-approval", content: null);
    Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

    await using var verify = app.CreateDbContext();
    var submitted = await verify.Transactions
      .Where(item => item.TransactionId == workerTransactionId || item.TransactionId == adminTransactionId)
      .ToListAsync();
    Assert.All(submitted, transaction =>
    {
      Assert.Equal((short)2, transaction.ApprovalStatusId);
      Assert.Equal(existingWorkerId, transaction.SocialWorkerUserId);
    });

    var notSubmitted = await verify.Transactions.SingleAsync(item => item.TransactionId == forbiddenTransactionId);
    Assert.Equal((short)1, notSubmitted.ApprovalStatusId);
    Assert.Equal(existingWorkerId, notSubmitted.SocialWorkerUserId);
    Assert.Equal(workerId, (await worker.GetFromJsonAsync<TransactionResponseDto>(
      $"/api/transactions/{workerTransactionId}"))!.LastUpdatedBy);
  }

  [Fact]
  public async Task ApproveTransaction_RequiresApproverOrAdministratorAndSetsApprovalFields()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var approverId = await CreateUserAsync(app, "approver", Password, "Approver");
    var adminId = await CreateUserAsync(app, "approval-admin", Password, "Administrator");
    await CreateUserAsync(app, "approval-superadmin", Password, "Super Administrator");
    await CreateUserAsync(app, "approval-worker", Password, "Social Worker");
    using var approver = await LoginAsAsync(app, "approver", Password);
    using var admin = await LoginAsAsync(app, "approval-admin", Password);
    using var superAdmin = await LoginAsAsync(app, "approval-superadmin", Password);
    using var worker = await LoginAsAsync(app, "approval-worker", Password);

    int approverTransactionId;
    int adminTransactionId;
    int superAdminTransactionId;
    int workerTransactionId;
    await using (var db = app.CreateDbContext())
    {
      db.ApprovalStatuses.AddRange(
        new ApprovalStatus { ApprovalStatusId = 2 },
        new ApprovalStatus { ApprovalStatusId = 3 });
      var approverTransaction = new Transaction { Status = true, ApprovalStatusId = 2 };
      var adminTransaction = new Transaction { Status = true, ApprovalStatusId = 2 };
      var superAdminTransaction = new Transaction { Status = true, ApprovalStatusId = 2 };
      var workerTransaction = new Transaction { Status = true, ApprovalStatusId = 2 };
      db.Transactions.AddRange(
        approverTransaction, adminTransaction, superAdminTransaction, workerTransaction);
      await db.SaveChangesAsync();
      approverTransactionId = approverTransaction.TransactionId;
      adminTransactionId = adminTransaction.TransactionId;
      superAdminTransactionId = superAdminTransaction.TransactionId;
      workerTransactionId = workerTransaction.TransactionId;
    }

    Assert.Equal(HttpStatusCode.OK, (await approver.PostAsync(
      $"/api/transactions/{approverTransactionId}/approve", content: null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(
      $"/api/transactions/{adminTransactionId}/approve", content: null)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.PostAsync(
      $"/api/transactions/{superAdminTransactionId}/approve", content: null)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await worker.PostAsync(
      $"/api/transactions/{workerTransactionId}/approve", content: null)).StatusCode);

    await using var verify = app.CreateDbContext();
    var approvedByApprover = await verify.Transactions
      .SingleAsync(transaction => transaction.TransactionId == approverTransactionId);
    Assert.Equal((short)3, approvedByApprover.ApprovalStatusId);
    Assert.Equal(approverId, approvedByApprover.ApprovedBy);
    Assert.Equal(app.Clock.GetUtcNow().UtcDateTime.AddHours(8), approvedByApprover.DateApproved);

    var approvedByAdmin = await verify.Transactions
      .SingleAsync(transaction => transaction.TransactionId == adminTransactionId);
    Assert.Equal((short)3, approvedByAdmin.ApprovalStatusId);
    Assert.Equal(adminId, approvedByAdmin.ApprovedBy);
    Assert.Equal(app.Clock.GetUtcNow().UtcDateTime.AddHours(8), approvedByAdmin.DateApproved);

    var unchangedTransactions = await verify.Transactions
      .Where(transaction =>
        transaction.TransactionId == superAdminTransactionId ||
        transaction.TransactionId == workerTransactionId)
      .ToListAsync();
    Assert.All(unchangedTransactions, transaction =>
    {
      Assert.Equal((short)2, transaction.ApprovalStatusId);
      Assert.Null(transaction.ApprovedBy);
      Assert.Null(transaction.DateApproved);
    });
  }

  [Fact]
  public async Task GetAllCurrentTransactionsOfSocialWorker_ReturnsOnlyTransactionsAssignedToCurrentUser()
  {
    await using var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    var currentWorkerId = await CreateUserAsync(app, "current-worker", Password, "Social Worker");
    var otherWorkerId = await CreateUserAsync(app, "other-worker", Password, "Social Worker");
    using var client = await LoginAsAsync(app, "current-worker", Password);

    int currentTransactionId;
    await using (var db = app.CreateDbContext())
    {
      var currentTransaction = new Transaction
      {
        Status = true,
        SocialWorkerUserId = currentWorkerId,
      };
      db.Transactions.AddRange(
        currentTransaction,
        new Transaction { Status = true, SocialWorkerUserId = otherWorkerId },
        new Transaction { Status = true });
      await db.SaveChangesAsync();
      currentTransactionId = currentTransaction.TransactionId;
    }

    var transactions = await client.GetFromJsonAsync<List<TransactionResponseDto>>(
      "/api/transactions/current-social-worker");

    var transaction = Assert.Single(transactions!);
    Assert.Equal(currentTransactionId, transaction.TransactionId);
    Assert.Equal(currentWorkerId, transaction.SocialWorkerUserId);
  }
}
