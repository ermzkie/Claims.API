using Microsoft.AspNetCore.Authorization;
using System.Data;
using System.Text.Json;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/transactions")]
[ApiController]
public class TransactionsController(
  ApplicationDbContext context,
  ICurrentUser currentUser,
  TimeProvider time) : ControllerBase
{
  private const string Entity = "Transaction";
  private const int MaxPageSize = 100;
  private const int MaxReason = 500;
  private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);
  private static readonly string[] AllocationFields = ["fundSourceId", "creditorId", "modeId", "amount"];

  // fields only social workers and admins may change
  private static readonly string[] MoneyFields =
    [.. AllocationFields, "socialWorkerAssessment", "isReleased", "assistanceCode"];
    

  private bool IsAdmin =>
    currentUser.IsInRole(RoleNames.SuperAdministrator) || currentUser.IsInRole(RoleNames.Administrator);

  private bool CanHandleMoney => IsAdmin || currentUser.IsInRole(RoleNames.SocialWorker);
  private bool CanApprove => IsAdmin || currentUser.IsInRole(RoleNames.Approver);

  private DateTime Now() => time.GetUtcNow().UtcDateTime.AddHours(8);

  [HttpGet("current-social-worker")]
  [Authorize(Policy = AuthPolicies.ClaimsViewer)]
  public async Task<ActionResult<List<TransactionResponseDto>>> GetAllCurrentTransactionsOfSocialWorker(
    CancellationToken cancellationToken)
  {
    if(!CanHandleMoney)
      return Forbid();
    var transactions = await WithDetails(context.Transactions
    .AsNoTracking()
    .Where(x => x.SocialWorkerUserId == currentUser.Id)
    //Pending (1) and returned (4) only
    //submitted (2) and Approved (3) are no longer in this list.
    .Where(x => x.ApprovalStatusId == 1 ||
                x.ApprovalStatusId == 4)
                .Where(ClaimRules.Counted))
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.TransactionId)
                .ToListAsync(cancellationToken);              
    return Ok(transactions.Select(TransactionResponseMapper.ToResponse).ToList());
  }

  //GET PENDING APPROVAL FOR APPROVER
  [HttpGet("pending-approval")]
  public async Task<ActionResult<List<TransactionResponseDto>>> GetPendingApprovalTransactions(CancellationToken cancellationToken)
  {
    if (!CanApprove)
    {
      return Forbid();
    }

    var transactions = await WithDetails(
      context.Transactions
      .AsNoTracking()
      .Where(ClaimRules.Counted)
      .Where(x => x.ApprovalStatusId == 2))
      .OrderByDescending(x=> x.DateCreated)
      .ThenByDescending(x => x.TransactionId)
      .ToListAsync(cancellationToken);
    return Ok(transactions.Select(
      TransactionResponseMapper.ToResponse
    ).ToList());
  }

  //SOCIAL WORKER MY FOR RELEASE TRANSACTION
  [HttpGet("my-for-release")]
  public async Task<ActionResult<List<TransactionResponseDto>>> GetMyClaimsForRelease(
    CancellationToken cancellationToken)
  {
    if(!CanHandleMoney)
    return Forbid();

    var transactions = await WithDetails(
      context.Transactions
      .AsNoTracking()
      .Where(ClaimRules.Counted)
      .Where(x => x.SocialWorkerUserId == currentUser.Id)
      .Where(x => x.ApprovalStatusId == 3)
      .Where(x => x.IsReleased != true)
    )
    .OrderByDescending(x => x.DateApproved)
    .ThenByDescending(x => x.TransactionId)
    .ToListAsync(cancellationToken);

   return Ok(transactions.Select(TransactionResponseMapper.ToResponse).ToList());
  }

  [HttpGet]
  [Authorize(Policy = AuthPolicies.ClaimsViewer)]
  public async Task<ActionResult<PagedResult<TransactionResponseDto>>> GetAll(
          [FromQuery] TransactionQueryParameters parameters)
  {
    if (parameters.Page < 1)
      return BadRequest(new { message = "Page must be greater than 0." });

    if (parameters.PageSize is < 1 or > MaxPageSize)
      return BadRequest(new { message = $"Page size must be from 1 to {MaxPageSize}." });

    if ((long)(parameters.Page - 1) * parameters.PageSize > int.MaxValue)
      return BadRequest(new { message = "Page is too high." });

    if (parameters.StartDate.HasValue &&
        parameters.EndDate.HasValue &&
        parameters.StartDate.Value.Date > parameters.EndDate.Value.Date)
    {
      return BadRequest(new { message = "Start date cannot be later than end date." });
    }

    var query = context.Transactions.AsNoTracking();

    switch (parameters.Status?.Trim().ToLowerInvariant())
    {
      case null or "" or "active" or "true":
        query = query.Where(ClaimRules.Counted);
        break;
      case "cancelled" or "false":
        query = query.Where(x => !x.Status && (x.CreatedBy != null || x.LastUpdatedBy != null));
        break;
      case "all":
        break;
      default:
        return BadRequest(new { message = "Status must be active, cancelled or all." });
    }

    //TAB FILTERING FOR TRANSACTIONS
    switch (parameters.Tab?.Trim().ToLowerInvariant())
    {
      case null:
      case "":
      case "all":
        //show all active transactions.
        query = query.Where(ClaimRules.Counted);
        break;

      case "waiting":
      query = query
      .Where(ClaimRules.Counted)
      .Where(x => x.ApprovalStatusId == 1 && x.IsReleased != true);
      break;

      case "for-approval":
        query = query
        .Where(ClaimRules.Counted)
        .Where(x => x.ApprovalStatusId == 2);
      break;

     case "approved":
     query = query
     .Where(ClaimRules.Counted)
     .Where(x => x.ApprovalStatusId == 3);
     break;

     case "returned":
     query = query
     .Where(ClaimRules.Counted)
     .Where(x => x.ApprovalStatusId == 4);
     break;

    case "released":
    query = query
    .Where(ClaimRules.Counted)
    .Where(x => x.IsReleased == true );
    break;

    case "history":
    break;

     default:
        return BadRequest(new
        {
            message = "Invalid tab. Valid tabs: all, waiting, for-approval, approved, returned, released, history."
        });

    }


    if (parameters.StartDate.HasValue)
    {
      query = query.Where(x => x.Date >= parameters.StartDate.Value.Date);
    }

    if (parameters.EndDate.HasValue)
    {
      var exclusiveEndDate = parameters.EndDate.Value.Date.AddDays(1);
      query = query.Where(x => x.Date < exclusiveEndDate);
    }

    if (parameters.SocialWorkerId.HasValue)
    {
      query = query.Where(x => x.SocialWorkerUserId == parameters.SocialWorkerId);
    }

    if (!string.IsNullOrWhiteSpace(parameters.AssistanceCode))
    {
      var code = parameters.AssistanceCode.Trim();
      query = query.Where(x => x.AssistanceCode == code);
    }

    if (!string.IsNullOrWhiteSpace(parameters.Search))
    {
      var search = parameters.Search.Trim();
      var namePattern = $"%{string.Join("%", search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(EscapeLike))}%";

      query = query.Where(x =>
          (!string.IsNullOrEmpty(x.RefNo) && x.RefNo.Contains(search))
          || (x.PatientPerson != null && EF.Functions.Like(
            (x.PatientPerson.FirstName ?? "") + " " +
            (x.PatientPerson.MiddleName ?? "") + " " +
            (x.PatientPerson.LastName ?? ""),
            namePattern,
            "\\"))
          || (x.RepresentativePerson != null && EF.Functions.Like(
            (x.RepresentativePerson.FirstName ?? "") + " " +
            (x.RepresentativePerson.MiddleName ?? "") + " " +
            (x.RepresentativePerson.LastName ?? ""),
            namePattern,
            "\\")));
    }

    var totalRecords = await query.CountAsync();

    var sortBy = parameters.SortBy?.Trim().ToLowerInvariant();
    var descending = parameters.SortDirection?.Trim().ToLowerInvariant() != "asc";

    var orderedQuery = sortBy switch
    {
      "transactionid" => descending
          ? query.OrderByDescending(x => x.TransactionId)
          : query.OrderBy(x => x.TransactionId),

      "transactionnumber" => descending
          ? query.OrderByDescending(x => x.RefNo)
          : query.OrderBy(x => x.RefNo),

      "status" => descending
          ? query.OrderByDescending(x => x.Status)
          : query.OrderBy(x => x.Status),

      _ => descending
          ? query.OrderByDescending(x => x.Date)
          : query.OrderBy(x => x.Date)
    };

    orderedQuery = descending
      ? orderedQuery.ThenByDescending(x => x.TransactionId)
      : orderedQuery.ThenBy(x => x.TransactionId);

    var transactions = await WithDetails(orderedQuery)
        .Skip((parameters.Page - 1) * parameters.PageSize)
        .Take(parameters.PageSize)
        .ToListAsync();

    return Ok(new PagedResult<TransactionResponseDto>(
      transactions.Select(TransactionResponseMapper.ToResponse).ToList(),
      totalRecords,
      parameters.Page,
      parameters.PageSize,
      (int)Math.Ceiling((double)totalRecords / parameters.PageSize)));
  }

  [HttpGet("{id:int}")]
  [Authorize(Policy = AuthPolicies.ClaimsViewer)]
  public async Task<ActionResult<TransactionResponseDto>> GetById(int id)
  {
    var transaction = await LoadAsync(id, CancellationToken.None);

    return transaction is null
      ? NotFound(new { message = "Transaction not found." })
      : Ok(TransactionResponseMapper.ToResponse(transaction));
  }

  [HttpGet("{id:int}/history")]
  [Authorize(Policy = AuthPolicies.ClaimsViewer)]
  public async Task<ActionResult<List<TransactionHistoryDto>>> History(int id, CancellationToken cancellationToken)
  {
    if (!await context.Transactions.AnyAsync(x => x.TransactionId == id, cancellationToken))
      return NotFound(new { message = "Transaction not found." });

    var rows = await context.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == Entity && a.EntityId == id)
      .OrderBy(a => a.DateCreated).ThenBy(a => a.AuditLogId)
      .Select(a => new TransactionHistoryDto(
        a.AuditLogId, a.Action, a.OldValues, a.NewValues, a.Remark, a.UserId,
        a.User.Lastname + ", " + a.User.Firstname, a.DateCreated))
      .ToListAsync(cancellationToken);

    return Ok(rows);
  }

  // edits: a field left out (null) keeps its value
  [HttpPost("save")]
  public async Task<ActionResult<TransactionResponseDto>> SaveTransaction(
    SaveTransactionDto dto,
    CancellationToken cancellationToken)
  {
    var transaction = dto.TransactionId.HasValue
      ? await context.Transactions
        .Include(x => x.TransactionServices)
          .ThenInclude(service => service.ServiceFundAllocations)
        .FirstOrDefaultAsync(x => x.TransactionId == dto.TransactionId.Value, cancellationToken)
      : null;
    if (dto.TransactionId.HasValue && transaction is null)
      return NotFound(new { message = "Transaction not found." });

    var isNew = transaction is null;
    // the desk can still fix a claim nobody has picked up; after that it's the social worker's
    if (!isNew && !CanHandleMoney && transaction!.SocialWorkerUserId is not null)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "A social worker has this claim now. Ask them to change it." });
    if (transaction is not null && ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim was cancelled. An admin can restore it." });

    var currentAllocation = transaction?.TransactionServices
      .SelectMany(service => service.ServiceFundAllocations)
      .Where(allocation => allocation.Status)
      .OrderBy(allocation => allocation.ServiceFundAllocationId)
      .FirstOrDefault();
    var before = transaction is null
      ? new Dictionary<string, object?>()
      : Snapshot(transaction, currentAllocation);
    var wasReleased = transaction?.IsReleased == true;

    // dates are PH time; a page using toISOString() sends UTC
    var date = dto.Date is { Kind: not DateTimeKind.Unspecified } d
      ? DateTime.SpecifyKind(d.ToUniversalTime().AddHours(8), DateTimeKind.Unspecified)
      : dto.Date;

    var next = new Transaction
    {
      Date = date ?? transaction?.Date,
      LaneTypeId = dto.LaneTypeId ?? transaction?.LaneTypeId,
      PatientPersonId = dto.PatientPersonId ?? transaction?.PatientPersonId,
      RepresentativePersonId = dto.RepresentativePersonId ?? transaction?.RepresentativePersonId,
      SocialWorkerAssessment = Text(dto.SocialWorkerAssessment, transaction?.SocialWorkerAssessment),
      Remarks = Text(dto.Remarks, transaction?.Remarks),
      IsReleased = dto.IsReleased ?? transaction?.IsReleased ?? false,
      AssistanceCode = Text(dto.AssistanceCode, transaction?.AssistanceCode)?.ToUpperInvariant(),
    };
    var nextAllocation = new ServiceFundAllocation
    {
      FundSourceId = dto.FundSourceId ?? currentAllocation?.FundSourceId,
      CreditorId = dto.CreditorId ?? currentAllocation?.CreditorId,
      ModeId = dto.ModeId ?? currentAllocation?.ModeId,
      Amount = dto.Amount ?? currentAllocation?.Amount,
    };
    var natureIds = dto.NatureIds?.Distinct().OrderBy(n => n).ToList()
      ?? transaction?.TransactionServices.Where(s => s.NatureId.HasValue)
        .Select(s => s.NatureId!.Value).OrderBy(n => n).ToList()
      ?? [];
    foreach (var nature in natureIds)
      next.TransactionServices.Add(new TransactionService { NatureId = nature });

    var after = Snapshot(next, nextAllocation);
    var changed = after.Keys
      .Where(k => !Equals(before.GetValueOrDefault(k), after[k]))
      .ToList();

    if (!isNew && changed.Count == 0)
      return NoContent();

    if (wasReleased)
    {
      if (!IsAdmin)
        return Conflict(new { message = "Released claims can't be changed. An admin can reopen it." });
      if (next.IsReleased != false || changed.Any(k => k != "isReleased"))
        return Conflict(new { message = "Reopen the claim first, then make the changes." });
    }

    if (!CanHandleMoney && changed.Any(k => MoneyFields.Contains(k)
        && (isNew ? after[k] is not null and not false : true)))
    {
      return StatusCode(StatusCodes.Status403Forbidden, new
      {
        message = "Only social workers and admins can set the amount, creditor, fund source, mode, assessment, program or release.",
      });
    }

    // the worker serving the claim finishes it; admins can always step in
    if (!isNew && !IsAdmin && transaction!.SocialWorkerUserId != currentUser.Id && changed.Any(k => MoneyFields.Contains(k)))
    {
      if (transaction.SocialWorkerUserId is null)
        return StatusCode(StatusCodes.Status403Forbidden, new { message = "Check out the claim first." });
      var holder = await context.Users.AsNoTracking()
        .Where(u => u.UserId == transaction.SocialWorkerUserId)
        .FirstOrDefaultAsync(cancellationToken);
      return StatusCode(StatusCodes.Status403Forbidden, new
      {
        message = $"{FullName(holder) ?? "Another social worker"} is handling this claim.",
      });
    }

    var problem = await ValidateAsync(next, nextAllocation, natureIds, transaction, changed, cancellationToken);
    if (problem is not null)
      return BadRequest(new { message = problem });

    if (isNew && !dto.ConfirmSameDay)
    {
      var day = next.Date!.Value.Date;
      var nextDay = day.AddDays(1);
      var open = await context.Transactions.AsNoTracking()
        .Where(ClaimRules.Counted)
        .Where(x => x.PatientPersonId == next.PatientPersonId && x.IsReleased != true && x.Date >= day && x.Date < nextDay)
        .Include(x => x.LaneType)
        .Include(x => x.PatientPerson)
        .OrderByDescending(x => x.TransactionId)
        .FirstOrDefaultAsync(cancellationToken);
      if (open is not null)
      {
        var code = $"{open.LaneType?.LaneTypeCode}{open.QueueNo}";
        var name = string.Join(", ", new[] { open.PatientPerson?.LastName, open.PatientPerson?.FirstName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        return Conflict(new
        {
          message = $"{name} is already in today's queue as {code}.",
          transactionId = open.TransactionId,
          queueNoCode = code,
          sameDay = true,
        });
      }
    }

    // queue numbers are per day and lane, so a claim moved to another one gets a new number
    var newQueue = isNew
      || changed.Contains("laneTypeId")
      || (changed.Contains("date") && transaction!.Date?.Date != next.Date!.Value.Date);

    await using var dbTransaction = await context.Database.BeginTransactionAsync(
      newQueue ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
      cancellationToken);

    var now = Now();
    if (transaction is null)
    {
      transaction = new Transaction
      {
        Status = true,
        CreatedBy = currentUser.Id,
        DateCreated = now,
      };
      context.Transactions.Add(transaction);
    }
    else
    {
      Touch(transaction, now);
    }

    if (newQueue)
    {
      var queueDate = next.Date!.Value.Date;
      var nextDay = queueDate.AddDays(1);
      var selfId = transaction.TransactionId;
      var lastQueueNo = await context.Transactions
        .Where(x => x.Date >= queueDate && x.Date < nextDay)
        .Where(x => x.LaneTypeId == next.LaneTypeId)
        .Where(x => x.TransactionId != selfId)
        .MaxAsync(x => x.QueueNo, cancellationToken);
      transaction.QueueNo = checked((short)((lastQueueNo ?? 0) + 1));
    }

    transaction.Date = next.Date;
    transaction.LaneTypeId = next.LaneTypeId;
    transaction.PatientPersonId = next.PatientPersonId;
    transaction.RepresentativePersonId = next.RepresentativePersonId;
    transaction.SocialWorkerAssessment = next.SocialWorkerAssessment;
    transaction.Remarks = next.Remarks;
    transaction.IsReleased = next.IsReleased;
    transaction.AssistanceCode = next.AssistanceCode;

    if (changed.Contains("natureIds"))
    {
      var wanted = natureIds.ToHashSet();
      foreach (var service in transaction.TransactionServices.ToList())
      {
        if (service.NatureId.HasValue && wanted.Remove(service.NatureId.Value))
          continue;
        if (service.ServiceFundAllocations.Count > 0)
        {
          service.NatureId = null;
          continue;
        }
        context.TransactionServices.Remove(service);
      }
      foreach (var natureId in wanted)
        transaction.TransactionServices.Add(new TransactionService { NatureId = natureId });
    }

    if (changed.Any(AllocationFields.Contains))
    {
      var allocation = currentAllocation;
      if (allocation is null && HasAllocationValues(nextAllocation))
      {
        var service = transaction.TransactionServices.FirstOrDefault();
        if (service is null)
        {
          service = new TransactionService();
          transaction.TransactionServices.Add(service);
        }

        allocation = new ServiceFundAllocation
        {
          Status = true,
          CreatedBy = currentUser.Id,
          DateCreated = now,
        };
        service.ServiceFundAllocations.Add(allocation);
      }

      if (allocation is not null)
      {
        allocation.FundSourceId = nextAllocation.FundSourceId;
        allocation.CreditorId = nextAllocation.CreditorId;
        allocation.ModeId = nextAllocation.ModeId;
        allocation.Amount = nextAllocation.Amount;
        if (currentAllocation is not null)
        {
          allocation.LastUpdatedBy = currentUser.Id;
          allocation.DateLastUpdated = now;
        }
      }
    }

    if (!await TrySaveAsync(cancellationToken))
      return Changed();

    var action = isNew ? "Created"
      : !wasReleased && next.IsReleased == true ? "Released"
      : wasReleased ? "Reopened"
      : "Updated";
    Log(transaction, action,
      isNew ? null : changed.ToDictionary(k => k, k => before.GetValueOrDefault(k)),
      changed.ToDictionary(k => k, k => after[k]),
      null, now);
    await context.SaveChangesAsync(cancellationToken);
    await dbTransaction.CommitAsync(cancellationToken);

    if (!isNew)
      return NoContent();

    var created = await LoadAsync(transaction.TransactionId, cancellationToken);
    return CreatedAtAction(
      nameof(GetById),
      new { id = transaction.TransactionId },
      TransactionResponseMapper.ToResponse(created!));
  }

  // someone else's claim is only taken over with takeOver + a reason, and that is logged
  [HttpPost("{id:int}/checkout-social-worker")]
  public async Task<ActionResult<TransactionResponseDto>> CheckoutTransaction(
    int id,
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CheckoutRequest? request,
    CancellationToken cancellationToken)
  {
    if (!CanHandleMoney)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only social workers and admins can check out claims." });

    var transaction = await LoadAsync(id, cancellationToken, tracking: true);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });
    if (ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim was cancelled." });
    if (transaction.IsReleased == true)
      return Conflict(new { message = "This claim is already released." });

    if (transaction.SocialWorkerUserId == currentUser.Id)
      return Ok(TransactionResponseMapper.ToResponse(transaction));

    var previous = transaction.SocialWorkerUserId;
    var reason = request?.Reason?.Trim();
    if (previous is not null)
    {
      if (request?.TakeOver != true)
      {
        return Conflict(new
        {
          message = $"{FullName(transaction.SocialWorkerUser) ?? "Another social worker"} already has this claim.",
          socialWorkerUserId = previous,
          socialWorkerUserFullName = FullName(transaction.SocialWorkerUser),
        });
      }
      if (string.IsNullOrEmpty(reason))
        return BadRequest(new { message = "Give a reason for taking over the claim." });
    }
    if (reason?.Length > MaxReason)
      return BadRequest(new { message = $"Keep the reason under {MaxReason} characters." });

    var now = Now();
    transaction.SocialWorkerUserId = currentUser.Id;
    Touch(transaction, now);
    Log(transaction, previous is null ? "CheckedOut" : "TakenOver",
      previous is null ? null : new Dictionary<string, object?> { ["socialWorkerUserId"] = previous },
      new Dictionary<string, object?> { ["socialWorkerUserId"] = currentUser.Id },
      // a plain checkout can carry a note too, e.g. bringing back a client marked not served
      string.IsNullOrEmpty(reason) ? null : reason, now);
    if (!await TrySaveAsync(cancellationToken))
      return Changed();
    await context.Entry(transaction).Reference(x => x.SocialWorkerUser).LoadAsync(cancellationToken);

    return Ok(TransactionResponseMapper.ToResponse(transaction));
  }

  [HttpPost("{id:int}/submit-for-approval")]
  public async Task<ActionResult<TransactionResponseDto>> SubmitTransactionForApproval(
    int id,
    CancellationToken cancellationToken)
  {
    if (!CanHandleMoney)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only social workers and admins can submit claims for approval." });

    var transaction = await LoadAsync(id, cancellationToken, tracking: true);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });
    if (ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim was cancelled." });
    if (transaction.IsReleased == true)
      return Conflict(new { message = "This claim is already released." });

    if (!IsAdmin && transaction.SocialWorkerUserId != currentUser.Id)
    {
      return Forbid();
    }


    if (transaction.ApprovalStatusId == 2)
      return Ok(TransactionResponseMapper.ToResponse(transaction));

    var previous = transaction.ApprovalStatusId;
    var now = Now();
    transaction.ApprovalStatusId = 2;
    Touch(transaction, now);
    Log(transaction, "SubmittedForApproval",
      new Dictionary<string, object?> { ["approvalStatusId"] = previous },
      new Dictionary<string, object?> { ["approvalStatusId"] = transaction.ApprovalStatusId },
      null, now);
    if (!await TrySaveAsync(cancellationToken))
      return Changed();

    return Ok(TransactionResponseMapper.ToResponse(transaction));
  }

[HttpPost("{id:int}/approve")]
public async Task<ActionResult<TransactionResponseDto>> ApproveTransaction(
    int id,
    CancellationToken cancellationToken)
{
    if (!CanApprove)
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Only approvers and admins can approve claims."
        });
    }

    var transaction = await LoadAsync(
        id,
        cancellationToken,
        tracking: true);

    if (transaction is null)
        return NotFound(new { message = "Transaction not found." });

    if (ClaimRules.IsCancelled(transaction))
        return Conflict(new { message = "This claim was cancelled." });

    if (transaction.IsReleased == true)
        return Conflict(new { message = "This claim has already been released." });

    // Idempotent response: do not create another approval audit entry.
    if (transaction.ApprovalStatusId == 3)
        return Ok(TransactionResponseMapper.ToResponse(transaction));

    // Only submitted claims can be approved.
    if (transaction.ApprovalStatusId != 2)
    {
        return Conflict(new
        {
            message = "Only claims submitted for approval can be approved."
        });
    }

    var previousStatus = transaction.ApprovalStatusId;
    var previousApprovedBy = transaction.ApprovedBy;
    var previousDateApproved = transaction.DateApproved;

    var now = Now();

    transaction.ApprovalStatusId = 3;
    transaction.ApprovedBy = currentUser.Id;
    transaction.DateApproved = now;

    // IMPORTANT:
    // Do not change SocialWorkerUserId.
    // The original Social Worker retains ownership of the claim.

    Touch(transaction, now);

    Log(
        transaction,
        "Approved",
        new Dictionary<string, object?>
        {
            ["approvalStatusId"] = previousStatus,
            ["approvedBy"] = previousApprovedBy,
            ["dateApproved"] = previousDateApproved
        },
        new Dictionary<string, object?>
        {
            ["approvalStatusId"] = transaction.ApprovalStatusId,
            ["approvedBy"] = transaction.ApprovedBy,
            ["dateApproved"] = transaction.DateApproved
        },
        null,
        now);

    if (!await TrySaveAsync(cancellationToken))
        return Changed();

    return Ok(TransactionResponseMapper.ToResponse(transaction));
}

  [HttpPost("{id:int}/release")]
  public async Task<ActionResult<TransactionResponseDto>> ReleaseTransaction(
      int id,
      CancellationToken cancellationToken)
  {
    if (!CanHandleMoney)
    {
      return StatusCode(StatusCodes.Status403Forbidden, new
      {
        message = "Only Social Workers and admins can release claims."
      });
    }

    var transaction = await LoadAsync(
        id,
        cancellationToken,
        tracking: true);

    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });

    if (ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim was cancelled." });

    if (transaction.ApprovalStatusId != 3)
    {
      return Conflict(new
      {
        message = "Only approved claims can be released."
      });
    }

    // Only the original assigned Social Worker or an admin may release.
    if (!IsAdmin && transaction.SocialWorkerUserId != currentUser.Id)
      return Forbid();

    if (transaction.IsReleased == true)
    {
      return Conflict(new
      {
        message = "This claim has already been released."
      });
    }

    var previousIsReleased = transaction.IsReleased;
    var previousReleasedBy = transaction.ReleasedBy;
    var previousDateReleased = transaction.DateReleased;

    var now = Now();

    transaction.IsReleased = true;
    transaction.ReleasedBy = currentUser.Id;
    transaction.DateReleased = now;

    Touch(transaction, now);

    Log(
        transaction,
        "Released",
        new Dictionary<string, object?>
        {
          ["isReleased"] = previousIsReleased,
          ["releasedBy"] = previousReleasedBy,
          ["dateReleased"] = previousDateReleased
        },
        new Dictionary<string, object?>
        {
          ["isReleased"] = transaction.IsReleased,
          ["releasedBy"] = transaction.ReleasedBy,
          ["dateReleased"] = transaction.DateReleased
        },
        null,
        now);

    if (!await TrySaveAsync(cancellationToken))
      return Changed();

    return Ok(TransactionResponseMapper.ToResponse(transaction));
  }


  [HttpPost("{id:int}/cancel")]
  public Task<IActionResult> Cancel(int id, ReasonRequest request, CancellationToken cancellationToken) =>
    CancelAsync(id, request.Reason, cancellationToken);

  [HttpDelete("{id:int}")]
  public Task<IActionResult> Delete(int id, [FromQuery] string? reason, CancellationToken cancellationToken) =>
    CancelAsync(id, reason, cancellationToken);

  [HttpPost("{id:int}/restore")]
  public async Task<IActionResult> Restore(
    int id,
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request,
    CancellationToken cancellationToken)
  {
    if (!IsAdmin)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only admins can restore claims." });

    var transaction = await context.Transactions.FirstOrDefaultAsync(x => x.TransactionId == id, cancellationToken);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });
    if (!ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim isn't cancelled." });

    var reason = request?.Reason?.Trim();
    if (reason?.Length > MaxReason)
      return BadRequest(new { message = $"Keep the reason under {MaxReason} characters." });

    var now = Now();
    transaction.Status = true;
    Touch(transaction, now);
    Log(transaction, "Restored", null, null, string.IsNullOrEmpty(reason) ? null : reason, now);
    if (!await TrySaveAsync(cancellationToken))
      return Changed();

    return NoContent();
  }

  private async Task<IActionResult> CancelAsync(int id, string? reason, CancellationToken cancellationToken)
  {
    if (!IsAdmin && !currentUser.IsInRole(RoleNames.Frontliner))
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only frontliners and admins can cancel claims." });

    var transaction = await context.Transactions.FirstOrDefaultAsync(x => x.TransactionId == id, cancellationToken);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });
    if (ClaimRules.IsCancelled(transaction))
      return Conflict(new { message = "This claim is already cancelled." });
    if (transaction.IsReleased == true)
      return Conflict(new { message = "Released claims can't be cancelled. Reopen it first." });

    reason = reason?.Trim();
    if (string.IsNullOrEmpty(reason))
      return BadRequest(new { message = "Give a reason for cancelling the claim." });
    if (reason.Length > MaxReason)
      return BadRequest(new { message = $"Keep the reason under {MaxReason} characters." });

    var now = Now();
    transaction.Status = false;
    transaction.LastUpdatedBy = currentUser.Id;
    transaction.DateLastUpdated = now;
    Log(transaction, "Cancelled", null, null, reason, now);
    if (!await TrySaveAsync(cancellationToken))
      return Changed();

    return NoContent();
  }

  private async Task<string?> ValidateAsync(
    Transaction next,
    ServiceFundAllocation nextAllocation,
    List<short> natureIds,
    Transaction? current,
    List<string> changed,
    CancellationToken ct)
  {
    bool Changed(string key) => current is null || changed.Contains(key);

    if (next.LaneTypeId is null)
      return "Lane type is required.";
    if (Changed("laneTypeId") && !await context.LaneTypes.AnyAsync(x => x.LaneTypeId == next.LaneTypeId, ct))
      return "Lane type was not found.";

    if (next.Date is null)
      return "Date is required to assign a queue number.";
    if (current?.Date is not null && changed.Contains("date") && current.Date.Value.Date != next.Date.Value.Date && !IsAdmin)
      return "Only admins can move a claim to another day.";

    if (next.PatientPersonId is null)
      return "Patient is required.";
    if (Changed("patientPersonId") && !await context.People.AnyAsync(x => x.PersonId == next.PatientPersonId && x.Status, ct))
      return "Patient was not found.";
    if (next.RepresentativePersonId is not null && Changed("representativePersonId")
        && !await context.People.AnyAsync(x => x.PersonId == next.RepresentativePersonId && x.Status, ct))
      return "Representative was not found.";

    if (nextAllocation.CreditorId is not null && Changed("creditorId")
        && !await context.Creditors.AnyAsync(x => x.CreditorId == nextAllocation.CreditorId && x.Status, ct))
      return "Creditor was not found.";
    if (nextAllocation.ModeId is not null && Changed("modeId")
        && !await context.ModeOfAssistances.AnyAsync(x => x.ModeId == nextAllocation.ModeId, ct))
      return "Mode of assistance was not found.";

    if (nextAllocation.FundSourceId is not null && (Changed("fundSourceId") || Changed("date")))
    {
      var fund = await context.FundSources.AsNoTracking()
        .Where(x => x.FundSourceId == nextAllocation.FundSourceId && x.Status)
        .Select(x => new { x.Year })
        .FirstOrDefaultAsync(ct);
      if (fund is null)
        return "Fund source was not found.";
      if (fund.Year != next.Date.Value.Year)
        return $"That fund source is for {fund.Year}, the claim is dated {next.Date.Value.Year}.";
    }

    if (nextAllocation.Amount is not null && Changed("amount"))
    {
      if (nextAllocation.Amount <= 0)
        return "Amount must be more than zero.";
      if (decimal.Round(nextAllocation.Amount.Value, 2) != nextAllocation.Amount)
        return "Amount can only have centavos (2 decimals).";
    }

    if (next.AssistanceCode is not null && Changed("assistanceCode"))
    {
      var code = next.AssistanceCode;
      // programs are SourceOfAssistance codes now, the Assistance table is gone
      if (!await context.SourceOfAssistances.AnyAsync(x => x.SourceCode != null && x.SourceCode.Trim().ToUpper() == code, ct))
        return "Program was not found.";
    }

    if (next.IsReleased == true && current?.IsReleased != true && nextAllocation.Amount is null)
      return "Set the amount before releasing the claim.";
    if (next.IsReleased == true && current?.IsReleased != true && next.AssistanceCode is null)
      return "Set the program before releasing the claim.";

    if (Changed("natureIds") && natureIds.Count > 0)
    {
      var found = await context.NatureOfServices.CountAsync(x => natureIds.Contains(x.NatureId), ct);
      if (found != natureIds.Count)
        return "A nature of service was not found.";
    }

    return null;
  }

  private static Dictionary<string, object?> Snapshot(Transaction t, ServiceFundAllocation? allocation) => new()
  {
    ["date"] = t.Date,
    ["laneTypeId"] = t.LaneTypeId,
    ["patientPersonId"] = t.PatientPersonId,
    ["representativePersonId"] = t.RepresentativePersonId,
    ["fundSourceId"] = allocation?.FundSourceId,
    ["creditorId"] = allocation?.CreditorId,
    ["modeId"] = allocation?.ModeId,
    ["amount"] = allocation?.Amount,
    ["socialWorkerAssessment"] = Text(t.SocialWorkerAssessment, null),
    ["remarks"] = Text(t.Remarks, null),
    ["isReleased"] = t.IsReleased,
    ["assistanceCode"] = Text(t.AssistanceCode, null)?.ToUpperInvariant(),
    ["natureIds"] = string.Join(",", t.TransactionServices
      .Where(s => s.NatureId.HasValue).Select(s => s.NatureId!.Value).OrderBy(n => n)),
  };

  private static bool HasAllocationValues(ServiceFundAllocation allocation) =>
    allocation.FundSourceId is not null
    || allocation.CreditorId is not null
    || allocation.ModeId is not null
    || allocation.Amount is not null;

  private async Task<bool> TrySaveAsync(CancellationToken ct)
  {
    try
    {
      await context.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateConcurrencyException)
    {
      return false;
    }
  }

  private ConflictObjectResult Changed() =>
    Conflict(new { message = "Someone else changed this claim just now. Reload it and try again." });

  // null keeps the old text, blank clears it
  private static string? Text(string? value, string? current) =>
    value is null ? current : string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  private void Touch(Transaction transaction, DateTime now)
  {
    transaction.LastUpdatedBy = currentUser.Id;
    transaction.DateLastUpdated = now;
    // old-system Status 0 already counts as active, so keep it counted once edited here
    if (transaction.CreatedBy is null)
      transaction.Status = true;
  }

  private void Log(
    Transaction transaction, string action,
    Dictionary<string, object?>? oldValues, Dictionary<string, object?>? newValues,
    string? remark, DateTime now)
  {
    context.AuditLogs.Add(new AuditLog
    {
      Entity = Entity,
      EntityId = transaction.TransactionId,
      PersonId = transaction.PatientPersonId,
      Action = action,
      OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, LogJson),
      NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, LogJson),
      Remark = remark,
      UserId = currentUser.Id,
      DateCreated = now,
    });
  }

  private static string? FullName(User? user) =>
    user is null ? null : string.Join(" ", new[] { user.Firstname, user.Middlename, user.Lastname }
      .Where(x => !string.IsNullOrWhiteSpace(x)));

  private static IQueryable<Transaction> WithDetails(IQueryable<Transaction> query) => query
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.FundSource)
          .ThenInclude(fundSource => fundSource!.Source)
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.Creditor)
    .Include(x => x.TransactionServices)
      .ThenInclude(service => service.ServiceFundAllocations)
        .ThenInclude(allocation => allocation.Mode)
    .Include(x => x.PatientPerson)
    .Include(x => x.RepresentativePerson)
    .Include(x => x.SocialWorkerUser)
    .Include(x => x.LaneType);

  private Task<Transaction?> LoadAsync(int id, CancellationToken ct, bool tracking = false)
  {
    var query = WithDetails(context.Transactions);
    if (!tracking)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(x => x.TransactionId == id, ct);
  }

  private static string EscapeLike(string value) =>
    value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
