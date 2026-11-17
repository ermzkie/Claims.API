using System.Text.Json;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Persons;
using Claims.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

// lists for the office to clean up persons; the only thing saved here is a review decision in AuditLog
[Route("api/person-review")]
[ApiController]
[Authorize(Policy = AuthPolicies.UserAdmin)]
public class PersonReviewController(ApplicationDbContext context, ICurrentUser currentUser, TimeProvider time)
  : ControllerBase
{
  private const int MaxPageSize = 100;
  private const string GroupEntity = "DuplicateGroup";
  private const string Same = "SamePerson";
  private const string NotSame = "NotSamePerson";
  private const string Reopened = "Reopened";
  private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

  // active persons with the same last, first and middle name and the same birthdate.
  // decision: none (still to review, default), same (waiting to be merged) or different
  [HttpGet("duplicates")]
  public async Task<ActionResult<PersonReviewPageDto<DuplicateGroupDto>>> Duplicates(
    [FromQuery] string? search = null,
    [FromQuery] string? decision = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
  {
    if (!ValidPage(page, pageSize))
    {
      return BadRequest(new { message = $"Page must be 1 or more and page size 1 to {MaxPageSize}." });
    }
    decision = string.IsNullOrWhiteSpace(decision) ? "none" : decision.Trim().ToLowerInvariant();
    if (decision is not ("none" or "same" or "different"))
    {
      return BadRequest(new { message = "Decision must be none, same or different." });
    }

    var reviews = await ReviewsAsync();
    var groups = (await GroupsAsync(search))
      .Select(g => g with { Review = reviews.GetValueOrDefault(GroupKey(g.Persons.Select(p => p.PersonId))) })
      .Where(g => (g.Review?.Decision ?? "none") == decision)
      .ToList();

    return new PersonReviewPageDto<DuplicateGroupDto>(
      groups.Count, page, pageSize, groups.Skip((page - 1) * pageSize).Take(pageSize).ToList());
  }

  // confirms the records are one person and which record to keep, to be merged later;
  // nothing is merged yet. Sending it again with another keepPersonId changes the record to keep.
  [HttpPost("duplicates/same")]
  public Task<IActionResult> MarkSame(DuplicateDecisionDto dto) => DecideAsync(dto, Same);

  // marks a group as different people so it stops showing; it comes back if someone new joins it
  [HttpPost("duplicates/not-same")]
  public Task<IActionResult> MarkNotSame(DuplicateDecisionDto dto) => DecideAsync(dto, NotSame);

  [HttpPost("duplicates/reopen")]
  public Task<IActionResult> Reopen(DuplicateDecisionDto dto) => DecideAsync(dto, Reopened);

  private async Task<IActionResult> DecideAsync(DuplicateDecisionDto dto, string action)
  {
    var ids = (dto.PersonIds ?? []).Distinct().Order().ToList();
    var remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim();
    if (ids.Count < 2)
    {
      return BadRequest(new { message = "Pick at least two persons." });
    }
    if (remark?.Length > 500)
    {
      return BadRequest(new { message = "Keep the remark to 500 characters." });
    }

    // only a whole current group can be marked, so an old page can't mark the wrong set
    var key = GroupKey(ids);
    var group = (await GroupsAsync(null)).FirstOrDefault(g => GroupKey(g.Persons.Select(p => p.PersonId)) == key);
    if (group is null)
    {
      return BadRequest(new { message = "These persons are no longer a possible duplicate group. Reload the list." });
    }

    int? keep = null;
    if (action == Same)
    {
      if (dto.KeepPersonId is not { } k || !ids.Contains(k))
      {
        return BadRequest(new { message = "Pick which record to keep." });
      }
      keep = k;
    }

    // changing a decision straight from same to different (or back) is allowed
    var review = (await ReviewsAsync()).GetValueOrDefault(key);
    var current = review?.Decision;
    if (action == Same && current == "same" && review!.KeepPersonId == keep)
    {
      return BadRequest(new { message = $"This group is already marked as the same person, keeping #{keep}." });
    }
    if (action == NotSame && current == "different")
    {
      return BadRequest(new { message = "This group is already marked as different people." });
    }
    if (action == Reopened && current is null)
    {
      return BadRequest(new { message = "This group is not marked, so there is nothing to reopen." });
    }

    context.AuditLogs.Add(new AuditLog
    {
      Entity = GroupEntity,
      EntityId = ids[0],
      PersonId = ids[0],
      Action = action,
      NewValues = JsonSerializer.Serialize(new DuplicateGroupValues(ids, keep), LogJson),
      Remark = remark,
      UserId = currentUser.Id,
      DateCreated = time.GetLocalNow().DateTime,
    });
    await context.SaveChangesAsync();
    return NoContent();
  }

  private static string GroupKey(IEnumerable<int> ids) => string.Join(",", ids.Order());

  // the latest decision per group wins; reopening clears it
  private async Task<Dictionary<string, DuplicateReviewDto>> ReviewsAsync()
  {
    var rows = await context.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == GroupEntity)
      .OrderBy(a => a.DateCreated).ThenBy(a => a.AuditLogId)
      .Select(a => new { a.Action, a.NewValues, a.Remark, a.DateCreated, a.User.Lastname, a.User.Firstname, a.User.Username })
      .ToListAsync();

    var latest = new Dictionary<string, DuplicateReviewDto?>();
    foreach (var a in rows)
    {
      var values = a.NewValues is null ? null : JsonSerializer.Deserialize<DuplicateGroupValues>(a.NewValues, LogJson);
      var ids = values?.PersonIds;
      if (ids is null || ids.Count < 2)
      {
        continue;
      }
      latest[GroupKey(ids)] = a.Action is Same or NotSame
        ? new DuplicateReviewDto(
          a.Action == Same ? "same" : "different",
          a.Lastname is null && a.Firstname is null ? a.Username : $"{a.Lastname}, {a.Firstname}".Trim(' ', ','),
          a.DateCreated,
          a.Remark,
          a.Action == Same ? values!.KeepPersonId : null)
        : null;
    }
    return latest.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);
  }

  private async Task<List<DuplicateGroupDto>> GroupsAsync(string? search)
  {
    var named = context.People.AsNoTracking().Where(p => p.Status && p.LastName != null && p.FirstName != null);
    var keys = await named
      .GroupBy(p => new { p.LastName, p.FirstName, p.MiddleName, p.Birthdate })
      .Where(g => g.Count() > 1)
      .Select(g => g.Key)
      .ToListAsync();

    var words = (search ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    keys = keys
      .Where(k => words.All(w => $"{k.LastName} {k.FirstName} {k.MiddleName}".ToUpperInvariant().Contains(w)))
      .ToList();

    var lastNames = keys.Select(k => k.LastName).Distinct().ToList();
    var firstNames = keys.Select(k => k.FirstName).Distinct().ToList();
    var candidates = await named
      .Where(p => lastNames.Contains(p.LastName) && firstNames.Contains(p.FirstName))
      .Select(p => new { p.PersonId, p.LastName, p.FirstName, p.MiddleName, p.Birthdate })
      .ToListAsync();
    // the database compares names ignoring capitals, so match the same way here
    static string Key(string? last, string? first, string? middle, DateOnly? born) =>
      $"{last?.Trim().ToUpperInvariant()}|{first?.Trim().ToUpperInvariant()}|{middle?.Trim().ToUpperInvariant()}|{born}";
    var keySet = keys.Select(k => Key(k.LastName, k.FirstName, k.MiddleName, k.Birthdate)).ToHashSet();
    var members = candidates
      .Select(p => new { p.PersonId, p.LastName, p.FirstName, p.MiddleName, p.Birthdate, Key = Key(p.LastName, p.FirstName, p.MiddleName, p.Birthdate) })
      .Where(p => keySet.Contains(p.Key))
      .ToList();

    var rows = (await RowsAsync(members.Select(p => p.PersonId).ToList())).ToDictionary(r => r.PersonId);

    return members
      .GroupBy(p => p.Key)
      .Where(g => g.Count() > 1)
      .Select(g =>
      {
        var persons = g.Select(p => rows[p.PersonId])
          .OrderByDescending(r => r.Claims)
          .ThenBy(r => r.PersonId)
          .ToList();
        var p = g.First();
        return new DuplicateGroupDto(
          p.LastName!.Trim(), p.FirstName!.Trim(), p.MiddleName?.Trim(), p.Birthdate, persons.Sum(r => r.Claims), persons, null);
      })
      .OrderByDescending(g => g.Claims)
      .ThenBy(g => g.LastName)
      .ThenBy(g => g.FirstName)
      .ToList();
  }

  // active persons missing a last or first name; most claims first so the busiest get fixed first
  [HttpGet("nameless")]
  public async Task<ActionResult<PersonReviewPageDto<PersonReviewRowDto>>> Nameless(
    [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
  {
    if (!ValidPage(page, pageSize))
    {
      return BadRequest(new { message = $"Page must be 1 or more and page size 1 to {MaxPageSize}." });
    }

    var ids = await context.People.AsNoTracking()
      .Where(p => p.Status && (p.LastName == null || p.FirstName == null))
      .Select(p => p.PersonId)
      .ToListAsync();
    var rows = (await RowsAsync(ids))
      .OrderByDescending(r => r.Claims)
      .ThenBy(r => r.PersonId)
      .ToList();

    return new PersonReviewPageDto<PersonReviewRowDto>(
      rows.Count, page, pageSize, rows.Skip((page - 1) * pageSize).Take(pageSize).ToList());
  }

  // a person's latest claims, as patient or representative, to help work out who they are
  [HttpGet("persons/{id:int}/claims")]
  public async Task<ActionResult<IReadOnlyList<PersonReviewClaimDto>>> Claims(int id, [FromQuery] int take = 10)
  {
    if (take is < 1 or > 50)
    {
      return BadRequest(new { message = "Take 1 to 50 claims." });
    }
    if (!await context.People.AnyAsync(p => p.PersonId == id))
    {
      return NotFound(new { message = "Person not found." });
    }

    var rows = await context.Transactions.AsNoTracking()
      .Where(t => (t.Status || t.CreatedBy == null) && (t.PatientPersonId == id || t.RepresentativePersonId == id))
      .OrderByDescending(t => t.Date).ThenByDescending(t => t.TransactionId)
      .Take(take)
      .Select(t => new
      {
        t.TransactionId,
        t.RefNo,
        t.Date,
        t.PatientPersonId,
        PatientLast = t.PatientPerson!.LastName,
        PatientFirst = t.PatientPerson.FirstName,
        PatientMiddle = t.PatientPerson.MiddleName,
        t.RepresentativePersonId,
        RepLast = t.RepresentativePerson!.LastName,
        RepFirst = t.RepresentativePerson.FirstName,
        RepMiddle = t.RepresentativePerson.MiddleName,
        RepText = t.Representative,
        t.AssistanceCode,
        t.CreditorId,
        CreditorName = t.Creditor!.CreditorName,
        t.Amount,
        WorkerLast = t.SocialWorkerUser!.Lastname,
        WorkerFirst = t.SocialWorkerUser.Firstname,
        t.SocialWorkerId,
      })
      .ToListAsync();

    var programs = (await context.Assistances.AsNoTracking()
        .Where(a => a.AssistanceCode != null)
        .Select(a => new { a.AssistanceCode, a.AssistanceType })
        .ToListAsync())
      .GroupBy(a => a.AssistanceCode!.Trim().ToUpperInvariant())
      .ToDictionary(g => g.Key, g => g.First().AssistanceType);

    // old claims point at Officers, which also stores passwords: read names only
    var officerIds = rows
      .Select(r => int.TryParse(r.SocialWorkerId?.Trim(), out var n) ? n : (int?)null)
      .OfType<int>()
      .Distinct()
      .ToList();
    var officers = await context.Officers.AsNoTracking()
      .Where(o => officerIds.Contains(o.Idnumber))
      .Select(o => new { o.Idnumber, o.Name })
      .ToDictionaryAsync(o => o.Idnumber, o => o.Name);

    return rows.Select(r =>
      {
        var asPatient = r.PatientPersonId == id;
        var other = asPatient
          ? Join(", ", r.RepLast, Join(" ", r.RepFirst, r.RepMiddle)) ?? (string.IsNullOrWhiteSpace(r.RepText) ? null : r.RepText.Trim())
          : Join(", ", r.PatientLast, Join(" ", r.PatientFirst, r.PatientMiddle));
        var worker = Join(", ", r.WorkerLast, r.WorkerFirst)
          ?? (int.TryParse(r.SocialWorkerId?.Trim(), out var officerId) ? officers.GetValueOrDefault(officerId)?.Trim() : null);
        return new PersonReviewClaimDto(
          r.TransactionId,
          r.RefNo?.Trim(),
          r.Date,
          asPatient ? "patient" : "representative",
          other,
          asPatient ? r.RepresentativePersonId : r.PatientPersonId,
          r.AssistanceCode?.Trim(),
          r.AssistanceCode is null ? null : programs.GetValueOrDefault(r.AssistanceCode.Trim().ToUpperInvariant()),
          r.CreditorId,
          r.CreditorName,
          r.Amount,
          worker);
      })
      .ToList();
  }

  private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= MaxPageSize;

  private async Task<List<PersonReviewRowDto>> RowsAsync(List<int> ids)
  {
    if (ids.Count == 0)
    {
      return [];
    }

    var people = await context.People.AsNoTracking()
      .Where(p => ids.Contains(p.PersonId))
      .Select(p => new
      {
        p.PersonId,
        p.LastName,
        p.FirstName,
        p.MiddleName,
        p.Birthdate,
        Sex = p.Sex!.SexDesc,
        LinkedBarangay = p.BarangayNavigation!.BarangayName,
        LinkedTown = p.BarangayNavigation.CityMunicipality!.CityMunicipalityName,
        p.Barangay,
        p.Municipality,
        p.ContactNo,
        p.DateCreated,
      })
      .ToListAsync();

    // same rule as everywhere else: old-system claims count, deleted ones made here don't
    var claims = context.Transactions.AsNoTracking().Where(t => t.Status || t.CreatedBy == null);
    var asPatient = await claims
      .Where(t => t.PatientPersonId != null && ids.Contains(t.PatientPersonId!.Value))
      .GroupBy(t => t.PatientPersonId!.Value)
      .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(t => t.Date) })
      .ToDictionaryAsync(x => x.Id);
    var asRepresentative = await claims
      .Where(t => t.RepresentativePersonId != null && ids.Contains(t.RepresentativePersonId!.Value))
      .GroupBy(t => t.RepresentativePersonId!.Value)
      .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(t => t.Date) })
      .ToDictionaryAsync(x => x.Id);

    var links = await context.FamilyCompositions.AsNoTracking()
      .Where(f => f.Status && (ids.Contains(f.PersonId) || (f.FamilyPersonId != null && ids.Contains(f.FamilyPersonId.Value))))
      .Select(f => new { f.PersonId, f.FamilyPersonId })
      .ToListAsync();
    var linkCounts = links
      .SelectMany(f => new[] { f.PersonId, f.FamilyPersonId ?? 0 })
      .GroupBy(id => id)
      .ToDictionary(g => g.Key, g => g.Count());

    return people.Select(p =>
      {
        var patient = asPatient.GetValueOrDefault(p.PersonId);
        var representative = asRepresentative.GetValueOrDefault(p.PersonId);
        DateTime?[] dates = [patient?.Last, representative?.Last];
        return new PersonReviewRowDto(
          p.PersonId,
          Join(", ", p.LastName, Join(" ", p.FirstName, p.MiddleName)),
          p.Birthdate,
          p.Sex,
          Join(", ", p.LinkedBarangay ?? p.Barangay, p.LinkedTown ?? p.Municipality),
          string.IsNullOrWhiteSpace(p.ContactNo) ? null : p.ContactNo.Trim(),
          p.DateCreated,
          patient?.Count ?? 0,
          representative?.Count ?? 0,
          dates.Max(),
          linkCounts.GetValueOrDefault(p.PersonId));
      })
      .ToList();
  }

  private static string? Join(string separator, params string?[] parts) =>
    string.Join(separator, parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())) is { Length: > 0 } s
      ? s
      : null;
}
