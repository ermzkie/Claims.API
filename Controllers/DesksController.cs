using System.Text.Json;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Desks;
using Claims.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/desks")]
[ApiController]
public class DesksController(ApplicationDbContext context, ICurrentUser currentUser, TimeProvider time) : ControllerBase
{
  private const string Entity = "Desk";
  private const int MaxName = 30;
  private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

  private DateTime Now() => DateTime.SpecifyKind(time.GetUtcNow().UtcDateTime.AddHours(8), DateTimeKind.Unspecified);

  private static DeskDto ToDto(Desk d) => new(d.DeskId, d.DeskName, d.SortOrder, d.Status);

  // the desk picker on each computer
  [HttpGet]
  public async Task<ActionResult<List<DeskDto>>> Active(CancellationToken ct) =>
    (await context.Desks.AsNoTracking().Where(d => d.Status)
      .OrderBy(d => d.SortOrder).ThenBy(d => d.DeskName).ToListAsync(ct))
    .Select(ToDto).ToList();

  [HttpGet("all")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<ActionResult<List<DeskDto>>> All(CancellationToken ct) =>
    (await context.Desks.AsNoTracking()
      .OrderByDescending(d => d.Status).ThenBy(d => d.SortOrder).ThenBy(d => d.DeskName).ToListAsync(ct))
    .Select(ToDto).ToList();

  [HttpPost]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Create(SaveDeskDto dto, CancellationToken ct)
  {
    var problem = await CheckAsync(dto, null, ct);
    if (problem is not null)
      return problem;

    var now = Now();
    var desk = new Desk
    {
      DeskName = dto.Name!.Trim(),
      SortOrder = dto.SortOrder,
      Status = dto.Status,
      CreatedBy = currentUser.Id,
      DateCreated = now,
    };
    context.Desks.Add(desk);
    await context.SaveChangesAsync(ct);
    Log(desk, "Created", null, now);
    await context.SaveChangesAsync(ct);
    return StatusCode(StatusCodes.Status201Created, ToDto(desk));
  }

  [HttpPut("{id:int}")]
  [Authorize(Policy = AuthPolicies.UserAdmin)]
  public async Task<IActionResult> Update(int id, SaveDeskDto dto, CancellationToken ct)
  {
    var desk = await context.Desks.FirstOrDefaultAsync(d => d.DeskId == id, ct);
    if (desk is null)
      return NotFound(new { message = "Desk not found." });
    var problem = await CheckAsync(dto, id, ct);
    if (problem is not null)
      return problem;

    var before = ToDto(desk);
    var now = Now();
    desk.DeskName = dto.Name!.Trim();
    desk.SortOrder = dto.SortOrder;
    desk.Status = dto.Status;
    desk.LastUpdatedBy = currentUser.Id;
    desk.DateLastUpdated = now;
    Log(desk, "Updated", before, now);
    await context.SaveChangesAsync(ct);
    return Ok(ToDto(desk));
  }

  private async Task<IActionResult?> CheckAsync(SaveDeskDto dto, int? id, CancellationToken ct)
  {
    var name = dto.Name?.Trim();
    if (string.IsNullOrEmpty(name))
      return BadRequest(new { message = "Give the desk a name." });
    if (name.Length > MaxName)
      return BadRequest(new { message = $"Keep the desk name to {MaxName} characters." });
    // inactive desks keep their name for old calls, so only active ones must be unique
    if (dto.Status && await context.Desks.AnyAsync(d => d.Status && d.DeskId != id && d.DeskName.Trim().ToUpper() == name.ToUpper(), ct))
      return Conflict(new { message = "Another desk already has that name." });
    return null;
  }

  private void Log(Desk desk, string action, DeskDto? before, DateTime now) =>
    context.AuditLogs.Add(new AuditLog
    {
      Entity = Entity,
      EntityId = desk.DeskId,
      Action = action,
      OldValues = before is null ? null : JsonSerializer.Serialize(before, LogJson),
      NewValues = JsonSerializer.Serialize(ToDto(desk), LogJson),
      UserId = currentUser.Id,
      DateCreated = now,
    });
}
