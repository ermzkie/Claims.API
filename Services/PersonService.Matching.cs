using System.Text.Json;
using Claims.API.DTOs.Persons;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public partial class PersonService
{
  private const int MaxMatches = 20;
  private const string PersonEntity = "Person";
  private const string SavedAsDuplicate = "SavedAsDuplicate";

  // one at a time so two clerks can't add the same person at once (one server)
  private static readonly SemaphoreSlim SaveLock = new(1, 1);

  public async Task<List<PersonMatchDto>> FindMatchesAsync(PersonMatchQueryDto query)
  {
    var codes = await db.NameSuffixes.AsNoTracking()
      .ToDictionaryAsync(s => s.NameSuffixId, s => s.NameSuffixCode);
    string? Code(short? id) => id is { } s ? codes.GetValueOrDefault(s) : null;

    var target = PersonMatcher.Parse(query.LastName, query.FirstName, query.MiddleName, Code(query.NameSuffixId));
    if (target.Last.Length == 0 || target.First.Length == 0)
    {
      return [];
    }

    var lastStarts = Starts(target.Last[0]);
    var firstStarts = Starts(target.First[0]);
    var except = query.ExceptPersonId ?? 0;
    var candidates = await db.People.AsNoTracking()
      .Where(p => p.Status && p.LastName != null && p.FirstName != null && p.PersonId != except)
      .Where(p =>
        (lastStarts.Contains(p.LastName!.Substring(0, 1)) && firstStarts.Contains(p.FirstName!.Substring(0, 1)))
        || (firstStarts.Contains(p.LastName!.Substring(0, 1)) && lastStarts.Contains(p.FirstName!.Substring(0, 1))))
      .Select(p => new { p.PersonId, p.LastName, p.FirstName, p.MiddleName, p.NameSuffixId, p.Birthdate })
      .ToListAsync();

    var found = candidates
      .Select(c => new
      {
        c.PersonId,
        Match = PersonMatcher.Compare(
          target, query.Birthdate, PersonMatcher.Parse(c.LastName, c.FirstName, c.MiddleName, Code(c.NameSuffixId)), c.Birthdate),
      })
      .Where(x => x.Match.Level != MatchLevel.None)
      .ToDictionary(x => x.PersonId, x => x.Match);
    if (found.Count == 0)
    {
      return [];
    }

    var ids = found.Keys.ToList();
    var people = await db.People.AsNoTracking()
      .Where(p => ids.Contains(p.PersonId))
      .Select(p => new
      {
        p.PersonId,
        p.LastName,
        p.FirstName,
        p.MiddleName,
        Suffix = p.NameSuffix!.NameSuffixCode,
        p.Birthdate,
        LinkedBarangay = p.BarangayNavigation!.BarangayName,
        LinkedTown = p.BarangayNavigation.CityMunicipality!.CityMunicipalityName,
        p.Barangay,
        p.Municipality,
      })
      .ToListAsync();

    var claims = db.Transactions.AsNoTracking().Where(t => t.Status || (t.CreatedBy == null && t.LastUpdatedBy == null));
    var asPatient = await claims
      .Where(t => t.PatientPersonId != null && ids.Contains(t.PatientPersonId!.Value))
      .GroupBy(t => t.PatientPersonId!.Value)
      .Select(g => new { Id = g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.Id, x => x.Count);
    var asRepresentative = await claims
      .Where(t => t.RepresentativePersonId != null && ids.Contains(t.RepresentativePersonId!.Value))
      .GroupBy(t => t.RepresentativePersonId!.Value)
      .Select(g => new { Id = g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.Id, x => x.Count);

    return people
      .Select(p =>
      {
        var match = found[p.PersonId];
        return new PersonMatchDto(
          p.PersonId,
          JoinName(p.LastName, p.FirstName, p.MiddleName, p.Suffix),
          p.Birthdate,
          JoinParts(p.LinkedBarangay ?? p.Barangay, p.LinkedTown ?? p.Municipality),
          asPatient.GetValueOrDefault(p.PersonId) + asRepresentative.GetValueOrDefault(p.PersonId),
          match.Level == MatchLevel.Same ? "same" : "similar",
          match.Reason);
      })
      .OrderBy(m => m.Level == "same" ? 0 : 1)
      .ThenByDescending(m => m.Claims)
      .ThenBy(m => m.PersonId)
      .Take(MaxMatches)
      .ToList();
  }

  private async Task<(PersonResult? Stop, List<int> Confirmed)> CheckDuplicateAsync(Person person, SavePersonDto dto)
  {
    var matches = await FindMatchesAsync(new PersonMatchQueryDto
    {
      LastName = person.LastName,
      FirstName = person.FirstName,
      MiddleName = person.MiddleName,
      NameSuffixId = person.NameSuffixId,
      Birthdate = person.Birthdate,
      ExceptPersonId = person.PersonId == 0 ? null : person.PersonId,
    });
    var same = matches.Where(m => m.Level == "same").ToList();
    if (same.Count == 0)
    {
      return (null, []);
    }

    if (!dto.ConfirmDuplicate)
    {
      var message = same.Count == 1
        ? $"#{same[0].PersonId} {same[0].Name} looks like the same person."
        : $"{same.Count} records look like the same person.";
      return (new PersonResult(PersonOutcome.Duplicate, $"{message} Open the existing record, or save anyway with a reason.", Matches: matches), []);
    }

    var reason = dto.DuplicateReason?.Trim();
    if (string.IsNullOrEmpty(reason))
    {
      return (new PersonResult(PersonOutcome.Invalid, "Say why this is a different person."), []);
    }
    if (RemarkError(reason) is { } error)
    {
      return (new PersonResult(PersonOutcome.Invalid, error), []);
    }
    return (null, same.Select(m => m.PersonId).ToList());
  }

  private void LogSavedAsDuplicate(int actorId, int personId, List<int> sameAs, string? reason) =>
    db.AuditLogs.Add(new AuditLog
    {
      Entity = PersonEntity,
      EntityId = personId,
      PersonId = personId,
      Action = SavedAsDuplicate,
      NewValues = JsonSerializer.Serialize(new { sameAs }, LogJson),
      Remark = reason?.Trim(),
      UserId = actorId,
      DateCreated = Now(),
    });

  private static List<string> Starts(char letter)
  {
    List<string> starts = [letter.ToString(), char.ToLowerInvariant(letter).ToString()];
    if (letter == 'N')
    {
      starts.AddRange(["Ñ", "ñ"]);
    }
    return starts;
  }

  private static string JoinName(string? last, string? first, string? middle, string? suffix) =>
    JoinParts(last, JoinParts(first, middle, suffix?.Trim(), separator: " ")) ?? "";

  private static string? JoinParts(string? a, string? b, string? c = null, string separator = ", ") =>
    string.Join(separator, new[] { a, b, c }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())) is { Length: > 0 } s
      ? s
      : null;
}
