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

  public CreditorController(ApplicationDbContext context)
  {
    _context = context;
  }

  // GET: api/Creditor
  [HttpGet]
  public async Task<ActionResult<IEnumerable<CreditorResponseDto>>> GetAll()
  {
    var records = await _context.Creditors
        .AsNoTracking()
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
        .Where(x => x.CreditorId == id)
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

    var record = new Creditor
    {
      CreditorName = dto.CreditorName.Trim()
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
}