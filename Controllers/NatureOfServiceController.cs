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
  private const int MaxDesc = 50;

  private readonly ApplicationDbContext _context;
  private readonly ICurrentUser _currentUser;
  private readonly TimeProvider _time;
  public NatureOfServiceController(ApplicationDbContext context, ICurrentUser currentUser, TimeProvider time)
  {
    _context = context;
    _currentUser = currentUser;
    _time = time;
  }

  private bool IsAdmin =>
    _currentUser.IsInRole(RoleNames.SuperAdministrator) || _currentUser.IsInRole(RoleNames.Administrator);

  private DateTime Now() => _time.GetLocalNow().DateTime;

  // GET: api/NatureOfService (admins can add ?includeInactive=true to find turned-off ones)
  [HttpGet]
  public async Task<ActionResult<IEnumerable<NatureOfServiceResponseDto>>> GetAll([FromQuery] bool includeInactive = false)
  {
    var query = _context.NatureOfServices.AsNoTracking();
    if (!includeInactive || !IsAdmin)
    {
      query = query.Where(x => x.Status);
    }

    var records = await query
        .OrderBy(x => x.NatureDesc)
        .Select(x => new NatureOfServiceResponseDto
        {
          NatureId = x.NatureId,
          NatureDesc = x.NatureDesc,
          Status = x.Status
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
          NatureDesc = x.NatureDesc,
          Status = x.Status
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
    var desc = Clean(dto.NatureDesc);
    if (DescError(desc) is { } error)
    {
      return BadRequest(new { message = error });
    }
    if (await SameNameAsync(desc!, exceptId: null) is { } same)
    {
      return Conflict(new { message = SameNameMessage(same) });
    }

    var record = new NatureOfService
    {
      NatureDesc = desc,
      Status = true,
      CreatedBy = _currentUser.Id,
      DateCreated = Now()
    };

    _context.NatureOfServices.Add(record);

    await _context.SaveChangesAsync();

    var response = new NatureOfServiceResponseDto
    {
      NatureId = record.NatureId,
      NatureDesc = record.NatureDesc,
      Status = record.Status
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
    var desc = Clean(dto.NatureDesc);
    if (DescError(desc) is { } error)
    {
      return BadRequest(new { message = error });
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
    if (await SameNameAsync(desc!, exceptId: id) is { } same)
    {
      return Conflict(new { message = SameNameMessage(same) });
    }

    record.NatureDesc = desc;
    // left out = keep it as is; a rename used to turn it off
    if (dto.Status is { } status)
    {
      record.Status = status;
    }
    record.LastUpdatedBy = _currentUser.Id;
    record.DateLastUpdated = Now();

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

    // claims point at it, so only unused ones can really go
    var claims = await _context.TransactionServices.CountAsync(x => x.NatureId == id);
    if (claims > 0)
    {
      return Conflict(new
      {
        message = $"Can't delete \"{record.NatureDesc}\": {claims:N0} claim(s) use it. Turn it off instead."
      });
    }

    _context.NatureOfServices.Remove(record);

    await _context.SaveChangesAsync();

    return NoContent();
  }

  private static string? Clean(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

  private static string? DescError(string? desc) =>
    desc is null ? "Nature description cannot be empty."
    : desc.Length > MaxDesc ? $"Nature description can be at most {MaxDesc} characters."
    : null;

  private Task<NatureOfService?> SameNameAsync(string desc, short? exceptId)
  {
    var upper = desc.ToUpper();
    return _context.NatureOfServices.AsNoTracking()
        .Where(x => x.NatureDesc != null && x.NatureDesc.Trim().ToUpper() == upper && x.NatureId != exceptId)
        .FirstOrDefaultAsync();
  }

  private static string SameNameMessage(NatureOfService same) =>
    same.Status
      ? $"\"{same.NatureDesc}\" already exists."
      : $"\"{same.NatureDesc}\" already exists but is turned off. Turn it back on instead.";
}
