
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
        var assistanceTypes = await _context.Assistances
            .AsNoTracking()
            .Where(a =>
                a.IsActive != false &&
                a.AssistanceCode != null &&
                a.AssistanceType != null &&
                a.AssistanceCode.Trim() != "" &&
                a.AssistanceType.Trim() != "")
            .Select(a => new
            {
                AssistanceCode = a.AssistanceCode!.Trim(),
                AssistanceName = a.AssistanceType!.Trim()
            })
            .Distinct()
            .OrderBy(a => a.AssistanceName)
            .ToListAsync();

        return Ok(assistanceTypes);
    }
}
