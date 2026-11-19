using Claims.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssistanceController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AssistanceController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetAssistanceTypes()
    {
        var assistanceTypes = await _context.SourceOfAssistances
            .AsNoTracking()
            .Where(x =>
                x.SourceDesc != null &&
                x.SourceDesc.Trim() != "")
            .Select(x => new
            {
                SourceID = x.SourceId,
                SourceDesc = x.SourceDesc!.Trim()
            })
            .OrderBy(x => x.SourceDesc)
            .ToListAsync();

        return Ok(assistanceTypes);
    }
}