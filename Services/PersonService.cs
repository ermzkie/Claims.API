using Claims.API.Data;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Persons;
using Claims.API.Models;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public enum PersonOutcome
{
  Ok,
  NotFound,
  Invalid,
}

public record PersonResult(PersonOutcome Outcome, string? Message = null, PersonDto? Person = null);

public partial class PersonService(ApplicationDbContext db, TimeProvider time)
{
  private const int MaxText = 50;
  private const int DefaultPageSize = 20;
  private const int MaxPageSize = 100;
  private const decimal MaxIncome = 99_999_999.99m;
  private static readonly DateOnly EarliestBirthdate = new(1900, 1, 1);

  // Letters (any language, including ñ and accents), spaces, periods, apostrophes and hyphens.
  [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M} .'\-]*$")]
  private static partial Regex NamePattern();

  [GeneratedRegex(@"^[0-9 +()\-]+$")]
  private static partial Regex ContactPattern();

  public async Task<PagedResult<PersonListItemDto>> SearchAsync(string? search, int page, int pageSize)
  {
    page = Math.Max(1, page);
    pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    var query = db.People.AsNoTracking().Where(p => p.Status);

    // Every word must appear in the first, middle or last name; "Dela Cruz, Juan" works too.
    var words = (search ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
    foreach (var word in words)
    {
      var pattern = $"%{EscapeLike(word)}%";
      query = query.Where(p =>
        EF.Functions.Like(p.FirstName!, pattern, "\\")
        || EF.Functions.Like(p.MiddleName!, pattern, "\\")
        || EF.Functions.Like(p.LastName!, pattern, "\\"));
    }

    var total = await query.CountAsync();
    var items = await query
      .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.PersonId)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .Select(p => new PersonListItemDto(
        p.PersonId, p.FirstName, p.MiddleName, p.LastName, p.Gender, p.Barangay, p.Municipality, p.Birthdate,
        p.IsSeniorCitizen == true, p.IsSoloParent == true, p.IsPwd == true, p.Is4P == true))
      .ToListAsync();

    return new PagedResult<PersonListItemDto>(items, total, page, pageSize);
  }

  public async Task<PersonDto?> GetAsync(int id)
  {
    var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.PersonId == id);
    if (person is null)
    {
      return null;
    }

    var family = await db.FamilyCompositions.AsNoTracking()
      .Where(f => f.PersonId == id && f.Status)
      .OrderBy(f => f.FamilyCompId)
      .Select(f => new FamilyMemberDto(
        f.FamilyCompId,
        f.FamilyPersonId,
        f.FamilyPerson!.FirstName,
        f.FamilyPerson.MiddleName,
        f.FamilyPerson.LastName,
        f.RelationshipStatusId,
        f.RelationshipStatus!.RelationshipStatusDesc))
      .ToListAsync();

    var place = person.BarangayId is { } barangayId
      ? await db.Barangays.AsNoTracking()
          .Where(b => b.BarangayId == barangayId)
          .Select(b => new { b.CityMunicipalityId, b.CityMunicipality!.ProvinceId })
          .FirstOrDefaultAsync()
      : null;

    var dto = ToDto(person, family);
    dto.CityMunicipalityId = place?.CityMunicipalityId;
    dto.ProvinceId = place?.ProvinceId;
    return dto;
  }

  public async Task<PersonResult> CreateAsync(int actorId, SavePersonDto dto)
  {
    var person = new Person { CreatedBy = actorId };
    var error = await ApplyAsync(person, dto);
    if (error is not null)
    {
      return new PersonResult(PersonOutcome.Invalid, error);
    }

    db.People.Add(person);
    await db.SaveChangesAsync();
    return new PersonResult(PersonOutcome.Ok, Person: await GetAsync(person.PersonId));
  }

  public async Task<PersonResult> UpdateAsync(int actorId, int id, SavePersonDto dto)
  {
    var person = await db.People.FirstOrDefaultAsync(p => p.PersonId == id);
    if (person is null)
    {
      return new PersonResult(PersonOutcome.NotFound, "Person not found.");
    }

    var error = await ApplyAsync(person, dto);
    if (error is not null)
    {
      return new PersonResult(PersonOutcome.Invalid, error);
    }

    person.LastUpdatedBy = actorId;
    person.DateLastUpdated = Now();
    await db.SaveChangesAsync();
    return new PersonResult(PersonOutcome.Ok, Person: await GetAsync(id));
  }

  // Validates and copies the DTO onto the entity. Returns an error message, or null when valid.
  private async Task<string?> ApplyAsync(Person person, SavePersonDto dto)
  {
    var firstName = Name(dto.FirstName);
    var lastName = Name(dto.LastName);
    if (firstName is null)
    {
      return "First name is required.";
    }
    if (lastName is null)
    {
      return "Last name is required.";
    }

    var middleName = Name(dto.MiddleName);

    string?[] texts =
      [firstName, middleName, lastName, dto.NoBuildingName, dto.Street, dto.PurokSubdivision, dto.ContactNo, dto.SkillOccupation];
    if (texts.Any(t => Clean(t)?.Length > MaxText))
    {
      return $"Text fields can be at most {MaxText} characters.";
    }

    // Old records can hold other characters (commas, slashes); only typed or changed names are checked.
    (string? Value, string? Stored)[] names =
      [(lastName, person.LastName), (firstName, person.FirstName), (middleName, person.MiddleName)];
    if (names.Any(n => n.Value is not null && n.Value != n.Stored && !NamePattern().IsMatch(n.Value)))
    {
      return "Names can only contain letters, spaces, periods (.), apostrophes (') and hyphens (-).";
    }

    var contactNo = Clean(dto.ContactNo);
    if (contactNo is not null)
    {
      var digits = contactNo.Count(char.IsAsciiDigit);
      if (!ContactPattern().IsMatch(contactNo) || digits < 7 || digits > 15)
      {
        return "Contact no. can only contain numbers, spaces, +, - and ( ), with 7 to 15 digits.";
      }
    }

    if (dto.Birthdate > DateOnly.FromDateTime(Now()))
    {
      return "Birthdate can't be in the future.";
    }
    if (dto.Birthdate < EarliestBirthdate)
    {
      return "Birthdate can't be before 1900.";
    }
    if (dto.EstimatedMonthlyIncome < 0)
    {
      return "Estimated monthly income can't be negative.";
    }
    if (dto.EstimatedMonthlyIncome > MaxIncome)
    {
      return "Estimated monthly income is too large.";
    }
    if (dto.EstimatedMonthlyIncome is { } income && decimal.Round(income, 2) != income)
    {
      return "Estimated monthly income can have at most 2 decimal places.";
    }

    Sex? sex = null;
    if (dto.SexId is { } sexId && (sex = await db.Sexes.FindAsync(sexId)) is null)
    {
      return "Sex not found.";
    }
    if (dto.CivilStatusId is { } civilStatusId && await db.CivilStatuses.FindAsync(civilStatusId) is null)
    {
      return "Civil status not found.";
    }
    if (dto.NameSuffixId is { } suffixId && await db.NameSuffixes.FindAsync(suffixId) is null)
    {
      return "Name suffix not found.";
    }

    var barangay = dto.BarangayId is { } barangayId
      ? await db.Barangays.AsNoTracking()
          .Where(b => b.BarangayId == barangayId)
          .Select(b => new { b.BarangayName, Municipality = b.CityMunicipality!.CityMunicipalityName })
          .FirstOrDefaultAsync()
      : null;
    if (dto.BarangayId is not null && barangay is null)
    {
      return "Barangay not found.";
    }

    person.FirstName = firstName;
    person.MiddleName = middleName;
    person.LastName = lastName;
    person.NameSuffixId = dto.NameSuffixId;
    person.CivilStatusId = dto.CivilStatusId;
    person.NoBuildingName = Clean(dto.NoBuildingName);
    person.Street = Clean(dto.Street);
    person.PurokSubdivision = Clean(dto.PurokSubdivision);
    person.IsSeniorCitizen = dto.IsSeniorCitizen;
    person.IsSoloParent = dto.IsSoloParent;
    person.IsPwd = dto.IsPwd;
    person.IsRr = dto.IsRr;
    person.Is4P = dto.Is4P;
    person.Birthdate = dto.Birthdate;
    person.ContactNo = contactNo;
    person.SkillOccupation = Clean(dto.SkillOccupation);
    person.EstimatedMonthlyIncome = dto.EstimatedMonthlyIncome;

    // Reports read the text columns, and old records only have text. Fill the text from the
    // chosen lookup; with nothing chosen, keep whatever text an old record already has.
    person.SexId = dto.SexId;
    if (sex is not null)
    {
      person.Gender = sex.SexDesc;
    }
    person.BarangayId = dto.BarangayId;
    if (barangay is not null)
    {
      person.Barangay = barangay.BarangayName;
      person.Municipality = barangay.Municipality;
    }
    return null;
  }

  private static PersonDto ToDto(Person p, IReadOnlyList<FamilyMemberDto> family) => new()
  {
    PersonId = p.PersonId,
    FirstName = p.FirstName,
    MiddleName = p.MiddleName,
    LastName = p.LastName,
    NameSuffixId = p.NameSuffixId,
    SexId = p.SexId,
    Sex = p.Gender,
    CivilStatusId = p.CivilStatusId,
    NoBuildingName = p.NoBuildingName,
    Street = p.Street,
    PurokSubdivision = p.PurokSubdivision,
    BarangayId = p.BarangayId,
    Barangay = p.Barangay,
    Municipality = p.Municipality,
    IsSeniorCitizen = p.IsSeniorCitizen,
    IsSoloParent = p.IsSoloParent,
    IsPwd = p.IsPwd,
    IsRr = p.IsRr,
    Is4P = p.Is4P,
    Birthdate = p.Birthdate,
    ContactNo = p.ContactNo,
    SkillOccupation = p.SkillOccupation,
    EstimatedMonthlyIncome = p.EstimatedMonthlyIncome,
    DateCreated = p.DateCreated,
    Family = family,
  };

  private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  // Names are stored in capitals, like every record from the old system.
  private static string? Name(string? value) => Clean(value)?.ToUpperInvariant();

  private static string EscapeLike(string value) =>
    value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

  private DateTime Now() => time.GetLocalNow().DateTime;
}
