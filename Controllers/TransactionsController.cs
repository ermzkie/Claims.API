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
    DateTime? startDate,
    DateTime? endDate,
    int page = 1)
  {
    const int pageSize = 10;

    if (page < 1)
      page = 1;

    var query = context.Transactions
        .AsNoTracking()
        .AsQueryable();

    // Filter by start date
    if (startDate.HasValue)
    {
      query = query.Where(x => x.Date >= startDate.Value);
    }

    // Filter by end date (inclusive)
    if (endDate.HasValue)
    {
      var exclusiveEndDate = endDate.Value.Date.AddDays(1);

      query = query.Where(x => x.Date < exclusiveEndDate);
    }

    var totalRecords = await query.CountAsync();

    var transactions = await query
        .OrderByDescending(x => x.Date)
        .ThenBy(x => x.TransactionId)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var response = new PagedResponseDto<TransactionResponseDto>
    {
      Page = page,
      PageSize = pageSize,
      TotalRecords = totalRecords,
      TotalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
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

  [HttpPost]
  public async Task<ActionResult<TransactionResponseDto>> Create(CreateTransactionDto dto)
  {
    if (await context.Transactions.AnyAsync(x => x.TransactionId == dto.TransactionId))
      return Conflict(new { message = "A transaction with this ID already exists." });

    var now = DateTime.UtcNow.AddHours(8);
    var transaction = new Transaction
    {
      TransactionId = dto.TransactionId,
      AssistanceCode = dto.AssistanceCode,
      RefNo = dto.RefNo,
      SocialWorkerId = dto.SocialWorkerId,
      Date = dto.Date,
      PatientPersonId = dto.PatientPersonId,
      RepresentativePersonId = dto.RepresentativePersonId,
      Representative = dto.Representative,
      FundSourceId = dto.FundSourceId,
      CreditorId = dto.CreditorId,
      ModeId = dto.ModeId,
      Amount = dto.Amount,
      SocialWorkerAssessment = dto.SocialWorkerAssessment,
      Remarks = dto.Remarks,
      IsReleased = false,
      Status = true,
      CreatedBy = currentUser.Id,
      DateCreated = now,
      SocialWorkerUserId = currentUser.Id,
    };

    context.Transactions.Add(transaction);
    await context.SaveChangesAsync();

    var response = ToResponse(transaction);
    return CreatedAtAction(nameof(GetById), new { id = transaction.TransactionId }, response);
  }

  [HttpPut("{id:int}")]
  public async Task<IActionResult> Update(int id, UpdateTransactionDto dto)
  {
    var transaction = await context.Transactions
      .FirstOrDefaultAsync(x => x.TransactionId == id);
    if (transaction is null)
      return NotFound(new { message = "Transaction not found." });

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
    if (dto.IsReleased.HasValue)
      transaction.IsReleased = dto.IsReleased.Value;
    transaction.LastUpdatedBy = currentUser.Id;
    transaction.DateLastUpdated = DateTime.UtcNow.AddHours(8);

    await context.SaveChangesAsync();
    return NoContent();
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
