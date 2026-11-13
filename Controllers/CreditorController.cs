using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Claims.API.Auth;
using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Models;
using Claims.API.DTOs.Creditor;

namespace Claims.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CreditorController : ControllerBase
{
  private readonly ApplicationDbContext _context;
  private readonly ICurrentUser _currentUser;

  public CreditorController(ApplicationDbContext context, ICurrentUser currentUser)
  {
    _context = context;
    _currentUser = currentUser;
  }

  // GET: api/Creditor
  [HttpGet]
  public async Task<ActionResult<IEnumerable<CreditorResponseDto>>> GetAll()
  {
    var records = await _context.Creditors
        .AsNoTracking()
        .Where(x => x.Status)
        .Select(x => new CreditorResponseDto
        {
          CreditorId = x.CreditorId,
          CreditorName = x.CreditorName
        })
        .ToListAsync();

    return Ok(records);
  }

  // GET: api/Creditor/1
  [HttpGet("{id}")]
  public async Task<ActionResult<CreditorResponseDto>> GetById(int id)
  {
    var record = await _context.Creditors
        .AsNoTracking()
        .Where(x => x.CreditorId == id && x.Status)
        .Select(x => new CreditorResponseDto
        {
          CreditorId = x.CreditorId,
          CreditorName = x.CreditorName
        })
        .FirstOrDefaultAsync();

    if (record == null)
      return NotFound(new { message = "Creditor not found." });

    return Ok(record);
  }

  // POST: api/Creditor
  [HttpPost]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<ActionResult<CreditorResponseDto>> Create(
      CreateCreditorDto dto)
  {
    if (string.IsNullOrWhiteSpace(dto.CreditorName))
    {
      return BadRequest(new
      {
        message = "Creditor name cannot be empty."
      });
    }

    var name = dto.CreditorName.Trim();
    if (name.Length > 50)
    {
      return BadRequest(new { message = "Creditor name cannot exceed 50 characters." });
    }

    var record = new Creditor
    {
      CreditorName = name,
      Status = true,
      CreatedBy = _currentUser.Id,
      DateCreated = DateTime.UtcNow.AddHours(8),
    };

    _context.Creditors.Add(record);

    await _context.SaveChangesAsync();

    var response = new CreditorResponseDto
    {
      CreditorId = record.CreditorId,
      CreditorName = record.CreditorName
    };

    return CreatedAtAction(
        nameof(GetById),
        new { id = record.CreditorId },
        response
    );
  }

  // PUT: api/Creditor/1
  [HttpPut("{id}")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Update(int id, UpdateCreditorDto dto)
  {
    if (string.IsNullOrWhiteSpace(dto.CreditorName))
    {
      return BadRequest(new { message = "Creditor name cannot be empty." });
    }

    var name = dto.CreditorName.Trim();
    if (name.Length > 50)
    {
      return BadRequest(new { message = "Creditor name cannot exceed 50 characters." });
    }

    var record = await _context.Creditors
        .FirstOrDefaultAsync(x => x.CreditorId == id && x.Status);
    if (record is null)
    {
      return NotFound(new { message = "Creditor not found." });
    }

    record.CreditorName = name;
    record.LastUpdatedBy = _currentUser.Id;
    record.DateLastUpdated = DateTime.UtcNow.AddHours(8);
    await _context.SaveChangesAsync();

    return NoContent();
  }

  // DELETE: api/Creditor/1
  [HttpDelete("{id}")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Delete(int id)
  {
    var record = await _context.Creditors
        .FirstOrDefaultAsync(x => x.CreditorId == id && x.Status);
    if (record is null)
    {
      return NotFound(new { message = "Creditor not found." });
    }

    record.Status = false;
    record.LastUpdatedBy = _currentUser.Id;
    record.DateLastUpdated = DateTime.UtcNow.AddHours(8);
    await _context.SaveChangesAsync();

    return NoContent();
  }
}
