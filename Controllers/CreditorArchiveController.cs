using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Creditor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

// removed creditors (status false): list them and bring one back
[Route("api/creditor")]
[ApiController]
public class CreditorArchiveController(ApplicationDbContext context, ICurrentUser currentUser, TimeProvider time)
  : ControllerBase
{
  [HttpGet("removed")]
  public async Task<ActionResult<List<CreditorResponseDto>>> Removed() =>
    (await context.Creditors.AsNoTracking()
      .Where(c => !c.Status)
      .Select(c => new CreditorResponseDto { CreditorId = c.CreditorId, CreditorName = c.CreditorName })
      .ToListAsync())
      .OrderBy(c => c.CreditorName?.Trim(), StringComparer.OrdinalIgnoreCase)
      .ToList();

  [HttpPost("{id:int}/restore")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Restore(int id)
  {
    var creditor = await context.Creditors.FirstOrDefaultAsync(c => c.CreditorId == id);
    if (creditor is null)
    {
      return NotFound(new { message = "Creditor not found." });
    }
    if (creditor.Status)
    {
      return BadRequest(new { message = "This creditor is not removed." });
    }

    var name = creditor.CreditorName?.Trim().ToUpperInvariant();
    if (name is not null && await context.Creditors.AnyAsync(c =>
          c.Status && c.CreditorId != id && c.CreditorName != null && c.CreditorName.Trim().ToUpper() == name))
    {
      return Conflict(new { message = $"An active creditor is already named \"{creditor.CreditorName}\"." });
    }

    creditor.Status = true;
    creditor.LastUpdatedBy = currentUser.Id;
    creditor.DateLastUpdated = time.GetLocalNow().DateTime;
    await context.SaveChangesAsync();
    return NoContent();
  }
}
