using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/transactions")]
[ApiController]
public class TransactionsController(ApplicationDbContext context, ICurrentUser currentUser) : ControllerBase
{
  [HttpGet]
  public async Task<ActionResult<PagedResponseDto<TransactionResponseDto>>> GetAll(
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
        .Skip((parameters.Page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    // 5. Build response
    var response = new PagedResponseDto<TransactionResponseDto>
    {
      Page = parameters.Page,
      PageSize = pageSize,
      TotalRecords = totalRecords,
      TotalPages = (int)Math.Ceiling(
            (double)totalRecords / pageSize),

      Data = transactions.Select(ToResponse).ToList()
    };

    return Ok(response);
  }

  [HttpGet("{id:int}")]
  public async Task<ActionResult<TransactionResponseDto>> GetById(int id)
  {
    var transaction = await context.Transactions
      .AsNoTracking()
      .Where(x => x.TransactionId == id)
      .FirstOrDefaultAsync();

    return transaction is null
      ? NotFound(new { message = "Transaction not found." })
      : Ok(ToResponse(transaction));
  }

  [HttpPost("save")]
  public async Task<ActionResult<TransactionResponseDto>> SaveTransaction(SaveTransactionDto dto)
  {
    var now = DateTime.UtcNow.AddHours(8);
    var transaction = dto.TransactionId.HasValue
      ? await context.Transactions.FirstOrDefaultAsync(x => x.TransactionId == dto.TransactionId.Value)
      : null;
    var isNew = transaction is null;

    if (transaction is null)
    {
      transaction = new Transaction
      {
        IsReleased = false,
        Status = true,
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

    transaction.AssistanceCode = dto.AssistanceCode;
    transaction.RefNo = dto.RefNo;
    transaction.SocialWorkerId = dto.SocialWorkerId;
    transaction.Date = dto.Date;
    transaction.PatientPersonId = dto.PatientPersonId;
    transaction.RepresentativePersonId = dto.RepresentativePersonId;
    transaction.Representative = dto.Representative;
    transaction.FundSourceId = dto.FundSourceId;
    transaction.CreditorId = dto.CreditorId;
    transaction.ModeId = dto.ModeId;
    transaction.Amount = dto.Amount;
    transaction.SocialWorkerAssessment = dto.SocialWorkerAssessment;
    transaction.Remarks = dto.Remarks;

    await context.SaveChangesAsync();

    return isNew
      ? CreatedAtAction(nameof(GetById), new { id = transaction.TransactionId }, ToResponse(transaction))
      : NoContent();
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

  private static TransactionResponseDto ToResponse(Transaction transaction) => new()
  {
    TransactionId = transaction.TransactionId,
    AssistanceCode = transaction.AssistanceCode,
    RefNo = transaction.RefNo,
    SocialWorkerId = transaction.SocialWorkerId,
    Date = transaction.Date,
    PatientPersonId = transaction.PatientPersonId,
    RepresentativePersonId = transaction.RepresentativePersonId,
    Representative = transaction.Representative,
    FundSourceId = transaction.FundSourceId,
    CreditorId = transaction.CreditorId,
    ModeId = transaction.ModeId,
    Amount = transaction.Amount,
    SocialWorkerAssessment = transaction.SocialWorkerAssessment,
    Remarks = transaction.Remarks,
    IsReleased = transaction.IsReleased,
    Status = transaction.Status,
    CreatedBy = transaction.CreatedBy,
    DateCreated = transaction.DateCreated,
    LastUpdatedBy = transaction.LastUpdatedBy,
    DateLastUpdated = transaction.DateLastUpdated,
    SocialWorkerUserId = transaction.SocialWorkerUserId,
  };
}
