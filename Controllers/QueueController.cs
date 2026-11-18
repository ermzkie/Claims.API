using Claims.API.DTOs.Queue;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/queue")]
[ApiController]
public class QueueController(QueueService queue) : ControllerBase
{
  [HttpGet("today")]
  public async Task<ActionResult<QueueTodayDto>> Today(CancellationToken cancellationToken) =>
    await queue.TodayAsync(cancellationToken);
}
