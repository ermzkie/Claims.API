using System.Data;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/transactions")]
[ApiController]
public class TransactionsController(
  ApplicationDbContext context,
  ICurrentUser currentUser,
  TimeProvider time) : ControllerBase
{
  [HttpGet]
  public async Task<ActionResult<PagedResult<TransactionResponseDto>>> GetAll(
          [FromQuery] TransactionQueryParameters parameters)
  {
    const int pageSize = 10;

    // Validate pagination
    if (parameters.Page < 1)
      return BadRequest("Page must be greater than 0.");

    // Validate date range
    if (parameters.StartDate.HasValue &&
        parameters.EndDate.HasValue &&
        parameters.StartDate.Value.Date > parameters.EndDate.Value.Date)
    {
      return BadRequest("Start date cannot be later than end date.");
    }

    // Base query
    var query = context.Transactions
        .AsNoTracking()
        .AsQueryable();

    // 1. Date filtering
    if (parameters.StartDate.HasValue)
    {
      query = query.Where(x =>
          x.Date >= parameters.StartDate.Value.Date);
    }

    if (parameters.EndDate.HasValue)
    {
      var exclusiveEndDate =
          parameters.EndDate.Value.Date.AddDays(1);

      query = query.Where(x =>
          x.Date < exclusiveEndDate);
    }

    // 2. Search by transaction number
    if (!string.IsNullOrWhiteSpace(parameters.Search))
    {
      var search = parameters.Search.Trim();

      query = query.Where(x =>
          !string.IsNullOrEmpty(x.RefNo) &&
          x.RefNo.Contains(search));
    }

    // Count filtered records before pagination
    var totalRecords = await query.CountAsync();

    // 3. Sorting
    var sortBy = parameters.SortBy?.Trim().ToLowerInvariant();
    var sortDirection = parameters.SortDirection?
        .Trim().ToLowerInvariant();

    bool descending = sortDirection != "asc";

    IOrderedQueryable<Transaction> orderedQuery;

    orderedQuery = sortBy switch
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

      "date" or null or "" => descending
          ? query.OrderByDescending(x => x.Date)
          : query.OrderBy(x => x.Date),

      _ => descending
          ? query.OrderByDescending(x => x.Date)
          : query.OrderBy(x => x.Date)
    };

    // Ensure consistent ordering when multiple records
    // have the same sorting value.
    orderedQuery = orderedQuery.ThenBy(x => x.TransactionId);

    // 4. Pagination
    var transactions = await orderedQuery
        .Include(x => x.TransactionServices)
        .Include(x => x.PatientPerson)
        .Include(x => x.RepresentativePerson)
        .Include(x => x.SocialWorkerUser)
        .Skip((parameters.Page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    // 5. Build response
    var response = new PagedResult<TransactionResponseDto>(
      transactions.Select(TransactionResponseMapper.ToResponse).ToList(),
      parameters.Page,
      pageSize,
      totalRecords,
      (int)Math.Ceiling((double)totalRecords / pageSize));

    return Ok(response);
  }

  [HttpGet("{id:int}")]
  public async Task<ActionResult<TransactionResponseDto>> GetById(int id)
  {
    var transaction = await context.Transactions
      .AsNoTracking()
      .Include(x => x.TransactionServices)
      .Include(x => x.PatientPerson)
      .Include(x => x.RepresentativePerson)
      .Include(x => x.SocialWorkerUser)
      .Where(x => x.TransactionId == id)
      .FirstOrDefaultAsync();

    return transaction is null
      ? NotFound(new { message = "Transaction not found." })
      : Ok(TransactionResponseMapper.ToResponse(transaction));
  }

  [HttpPost("save")]
  public async Task<ActionResult<TransactionResponseDto>> SaveTransaction(
    SaveTransactionDto dto,
    CancellationToken cancellationToken)
  {
    var now = time.GetUtcNow().UtcDateTime.AddHours(8);
    var transaction = dto.TransactionId.HasValue
      ? await context.Transactions
        .Include(x => x.TransactionServices)
        .Include(x => x.SocialWorkerUser)
        .FirstOrDefaultAsync(x => x.TransactionId == dto.TransactionId.Value, cancellationToken)
      : null;
    var isNew = transaction is null;

    await using var dbTransaction = isNew || dto.NatureIds is not null
      ? await context.Database.BeginTransactionAsync(
        isNew ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
        cancellationToken)
      : null;

    if (transaction is null)
    {
      var queueDate = now.Date;
      var lastQueueNo = await context.Transactions
        .Where(x => x.DateCreated >= queueDate && x.DateCreated < queueDate.AddDays(1))
        .MaxAsync(x => x.QueueNo, cancellationToken);

      transaction = new Transaction
      {
        IsReleased = false,
        Status = true,
        QueueNo = checked((short)((lastQueueNo ?? 0) + 1)),
        CreatedBy = currentUser.Id,
        DateCreated = now,
      };
      context.Transactions.Add(transaction);
    }
    else
    {
      transaction.LastUpdatedBy = currentUser.Id;
      transaction.DateLastUpdated = now;
      if (dto.IsReleased.HasValue)
        transaction.IsReleased = dto.IsReleased.Value;
    }

    transaction.Date = dto.Date;
    transaction.PatientPersonId = dto.PatientPersonId;
    transaction.RepresentativePersonId = dto.RepresentativePersonId;
    transaction.FundSourceId = dto.FundSourceId;
    transaction.CreditorId = dto.CreditorId;
    transaction.ModeId = dto.ModeId;
    transaction.Amount = dto.Amount;
    transaction.SocialWorkerAssessment = dto.SocialWorkerAssessment;
    transaction.Remarks = dto.Remarks;

    if (dto.NatureIds is null)
    {
      await context.SaveChangesAsync(cancellationToken);
    }
    else
    {
      await context.SaveChangesAsync(cancellationToken);

      var natureIds = dto.NatureIds.Distinct().ToHashSet();
      foreach (var service in transaction.TransactionServices.ToList())
      {
        if (service.NatureId.HasValue && natureIds.Remove(service.NatureId.Value))
          continue;

        context.TransactionServices.Remove(service);
      }

      foreach (var natureId in natureIds)
      {
        transaction.TransactionServices.Add(new TransactionService
        {
          NatureId = natureId,
        });
      }

      await context.SaveChangesAsync(cancellationToken);
    }

    if (dbTransaction is not null)
      await dbTransaction.CommitAsync(cancellationToken);

    return isNew
      ? CreatedAtAction(nameof(GetById), new { id = transaction.TransactionId }, TransactionResponseMapper.ToResponse(transaction))
      : NoContent();
  }

  [HttpPost("{id:int}/checkout-social-worker")]
  public async Task<ActionResult<TransactionResponseDto>> CheckoutTransaction(int id, CancellationToken cancellationToken)
  {
    var transaction = await context.Transactions
      .Include(x => x.TransactionServices)
      .Include(x => x.PatientPerson)
      .Include(x => x.RepresentativePerson)
      .Include(x => x.SocialWorkerUser)
      .FirstOrDefaultAsync(x => x.TransactionId == id, cancellationToken);

    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });

    transaction.SocialWorkerUserId = currentUser.Id;
    transaction.DateLastUpdated = time.GetUtcNow().UtcDateTime.AddHours(8);

    await context.SaveChangesAsync(cancellationToken);
    await context.Entry(transaction)
      .Reference(x => x.SocialWorkerUser)
      .LoadAsync(cancellationToken);

    return Ok(TransactionResponseMapper.ToResponse(transaction));
  }

  [HttpDelete("{id:int}")]
  public async Task<IActionResult> Delete(int id)
  {
    var transaction = await context.Transactions
      .FirstOrDefaultAsync(x => x.TransactionId == id);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });

    transaction.Status = false;
    transaction.LastUpdatedBy = currentUser.Id;
    transaction.DateLastUpdated = DateTime.UtcNow.AddHours(8);
    await context.SaveChangesAsync();

    return NoContent();
  }
}
