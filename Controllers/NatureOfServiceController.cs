using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Claims.API.Auth;
using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Models;
using Claims.API.DTOs.NatureOfService;

namespace Claims.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class NatureOfServiceController : ControllerBase
{
  private readonly ApplicationDbContext _context;
  private readonly ICurrentUser _currentUser;
  public NatureOfServiceController(ApplicationDbContext context, ICurrentUser currentUser)
  {
    _context = context;
    _currentUser = currentUser;
  }

  // GET: api/NatureOfService
  [HttpGet]
  public async Task<ActionResult<IEnumerable<NatureOfServiceResponseDto>>> GetAll()
  {
    var records = await _context.NatureOfServices
        .AsNoTracking()
        .Select(x => new NatureOfServiceResponseDto
        {
          NatureId = x.NatureId,
          NatureDesc = x.NatureDesc
        })
        .ToListAsync();

    return Ok(records);
  }

  // GET: api/NatureOfService/1
  [HttpGet("{id}")]
  public async Task<ActionResult<NatureOfServiceResponseDto>> GetById(short id)
  {
    var record = await _context.NatureOfServices
        .AsNoTracking()
        .Where(x => x.NatureId == id)
        .Select(x => new NatureOfServiceResponseDto
        {
          NatureId = x.NatureId,
          NatureDesc = x.NatureDesc
        })
        .FirstOrDefaultAsync();

    if (record == null)
      return NotFound(new { message = "Nature of service not found." });

    return Ok(record);
  }

  // POST: api/NatureOfService
  [HttpPost]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<ActionResult<NatureOfServiceResponseDto>> Create(
      CreateNatureOfServiceDto dto)
  {
    if (string.IsNullOrWhiteSpace(dto.NatureDesc))
    {
      return BadRequest(new
      {
        message = "Nature description cannot be empty."
      });
    }

    var record = new NatureOfService
    {
      NatureDesc = dto.NatureDesc.Trim(),
      CreatedBy = _currentUser.Id,
      DateCreated = DateTime.UtcNow.AddHours(8)
    };

    _context.NatureOfServices.Add(record);

    await _context.SaveChangesAsync();

    var response = new NatureOfServiceResponseDto
    {
      NatureId = record.NatureId,
      NatureDesc = record.NatureDesc
    };

    return CreatedAtAction(
        nameof(GetById),
        new { id = record.NatureId },
        response
    );
  }

  // PUT: api/NatureOfService/1
  [HttpPut("{id}")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Update(
      short id,
      UpdateNatureOfServiceDto dto)
  {
    if (string.IsNullOrWhiteSpace(dto.NatureDesc))
    {
      return BadRequest(new
      {
        message = "Nature description cannot be empty."
      });
    }

    var record = await _context.NatureOfServices
        .FirstOrDefaultAsync(x => x.NatureId == id);

    if (record == null)
    {
      return NotFound(new
      {
        message = "Nature of service not found."
      });
    }

    // Update only the permitted property.
    record.NatureDesc = dto.NatureDesc.Trim();
    record.Status = dto.Status;
    record.LastUpdatedBy = _currentUser.Id;
    record.DateLastUpdated = DateTime.UtcNow.AddHours(8);

    await _context.SaveChangesAsync();

    return NoContent();
  }

  // DELETE: api/NatureOfService/1
  [HttpDelete("{id}")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Delete(short id)
  {
    var record = await _context.NatureOfServices
        .FirstOrDefaultAsync(x => x.NatureId == id);

    if (record == null)
    {
      return NotFound(new
      {
        message = "Nature of service not found."
      });
    }

    _context.NatureOfServices.Remove(record);

    await _context.SaveChangesAsync();

    return NoContent();
  }
}