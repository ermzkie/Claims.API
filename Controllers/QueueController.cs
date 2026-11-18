using Claims.API.Auth;
using Claims.API.DTOs.Queue;
using Claims.API.DTOs.Transactions;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/queue")]
[ApiController]
public class QueueController(QueueService queue, ICurrentUser currentUser) : ControllerBase
{
  private bool IsAdmin =>
    currentUser.IsInRole(RoleNames.SuperAdministrator) || currentUser.IsInRole(RoleNames.Administrator);

  private bool CanServe => IsAdmin || currentUser.IsInRole(RoleNames.SocialWorker);

  [HttpGet("today")]
  public async Task<ActionResult<QueueTodayDto>> Today(CancellationToken cancellationToken) =>
    await queue.TodayAsync(cancellationToken);

  [HttpPost("next")]
  public async Task<ActionResult<TransactionResponseDto>> Next(CancellationToken cancellationToken)
  {
    if (!CanServe)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only social workers and admins can take clients from the queue." });

    // one client at a time keeps the line honest
    if (await queue.OpenClaimOfAsync(currentUser.Id, cancellationToken) is { } open)
    {
      return Conflict(new
      {
        message = $"You're still serving {open.Code}. Release it, return it, or mark it not served first.",
        transactionId = open.Id,
      });
    }

    var id = await queue.TakeNextAsync(currentUser.Id, cancellationToken);
    if (id is null)
      return NoContent();

    var claim = await queue.LoadAsync(id.Value, cancellationToken);
    return Ok(TransactionResponseMapper.ToResponse(claim!));
  }
}
