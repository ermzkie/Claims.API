using Claims.API.Auth;
using Claims.API.DTOs.Queue;
using Claims.API.DTOs.Transactions;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Claims.API.Controllers;

[Route("api/queue")]
[ApiController]
public class QueueController(QueueService queue, ICurrentUser currentUser) : ControllerBase
{
  private const int MaxReason = 500;

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

  [HttpPost("{id:int}/return")]
  public Task<IActionResult> Return(
    int id,
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueueReasonRequest? request,
    CancellationToken cancellationToken) =>
    LetGoAsync(id, QueueService.ReturnedToQueue, request?.Reason, reasonRequired: false, cancellationToken);

  [HttpPost("{id:int}/not-served")]
  public Task<IActionResult> NotServed(int id, QueueReasonRequest request, CancellationToken cancellationToken) =>
    LetGoAsync(id, QueueService.NotServed, request.Reason, reasonRequired: true, cancellationToken);

  private async Task<IActionResult> LetGoAsync(
    int id, string action, string? reason, bool reasonRequired, CancellationToken cancellationToken)
  {
    if (!CanServe)
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only social workers and admins can do this." });

    reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    if (reasonRequired && reason is null)
      return BadRequest(new { message = "Say why the client wasn't served." });
    if (reason?.Length > MaxReason)
      return BadRequest(new { message = $"Keep the reason under {MaxReason} characters." });

    return await queue.LetGoAsync(id, currentUser.Id, IsAdmin, action, reason, cancellationToken) switch
    {
      QueueActionOutcome.Ok => NoContent(),
      QueueActionOutcome.NotFound => NotFound(new { message = "Transaction not found." }),
      QueueActionOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
        new { message = "Only the social worker serving this client, or an admin, can do this." }),
      QueueActionOutcome.NotServing => Conflict(new { message = "This claim isn't being served." }),
      QueueActionOutcome.InApproval => Conflict(new { message = "This claim was sent for approval, it's out of the queue." }),
      _ => Conflict(new { message = "This claim is already released or cancelled." }),
    };
  }
}
