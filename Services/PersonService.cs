using Claims.API.Data;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Persons;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public enum PersonOutcome
{
  Ok,
  NotFound,
  Invalid,
  Duplicate,
  Stale,
  Forbidden,
}

public record PersonResult(
  PersonOutcome Outcome, string? Message = null, PersonDto? Person = null, IReadOnlyList<PersonMatchDto>? Matches = null);
public record FamilyMemberResult(PersonOutcome Outcome, string? Message = null, FamilyMemberDto? FamilyMember = null);

public partial class PersonService(ApplicationDbContext db, TimeProvider time)
{
  private const int MaxText = 50;
  private const int DefaultPageSize = 20;
  private const int MaxPageSize = 100;
  private const decimal MaxIncome = 99_999_999.99m;
  private static readonly DateOnly EarliestBirthdate = new(1900, 1, 1);

  [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M} .'\-]*$")]
  private static partial Regex NamePattern();

  [GeneratedRegex(@"^[0-9 +()\-]+$")]
  private static partial Regex ContactPattern();

  [GeneratedRegex(@"^[0-9 +()\-]*[0-9][0-9 +()\-]*$")]
  private static partial Regex NumberSearchPattern();

  public static readonly IReadOnlyList<string> Sectors = ["senior", "pwd", "4ps", "solo-parent"];

  public async Task<PagedResult<PersonListItemDto>?> SearchAsync(PersonSearchDto filter)
  {
    var page = Math.Max(1, filter.Page);
    var pageSize = filter.PageSize <= 0 ? DefaultPageSize : Math.Min(filter.PageSize, MaxPageSize);
    var sectors = (filter.Sector ?? []).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
    if (sectors.Any(s => !Sectors.Contains(s)))
    {
      return null;
    }

    var query = db.People.AsNoTracking().Where(p => p.Status);
    var search = filter.Search?.Trim() ?? "";

    if (NumberSearchPattern().IsMatch(search))
    {
      var digits = new string(search.Where(char.IsAsciiDigit).ToArray());
      var id = digits.Length <= 9 ? int.Parse(digits) : -1;
      var contactPattern = $"%{digits}%";
      query = query.Where(p =>
        p.PersonId == id
        || (digits.Length >= 3 && EF.Functions.Like(
          p.ContactNo!.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace("+", ""),
          contactPattern)));
    }
    else
    {
      var words = search.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
      foreach (var word in words)
      {
        var pattern = $"%{EscapeLike(word)}%";
        query = query.Where(p =>
          EF.Functions.Like(p.FirstName!, pattern, "\\")
          || EF.Functions.Like(p.MiddleName!, pattern, "\\")
          || EF.Functions.Like(p.LastName!, pattern, "\\"));
      }
    }

    if (filter.BarangayId is { } barangayId)
    {
      query = query.Where(p => p.BarangayId == barangayId);
    }
    else if (filter.CityMunicipalityId is { } cityId)
    {
      query = query.Where(p => p.BarangayNavigation!.CityMunicipalityId == cityId);
    }
    if (filter.SexId is { } sexId)
    {
      query = query.Where(p => p.SexId == sexId);
    }
    foreach (var sector in sectors)
    {
      query = sector switch
      {
        "senior" => query.Where(p => p.IsSeniorCitizen == true),
        "pwd" => query.Where(p => p.IsPwd == true),
        "4ps" => query.Where(p => p.Is4P == true),
        _ => query.Where(p => p.IsSoloParent == true),
      };
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

    var totalPages = (int)Math.Ceiling((double)total / pageSize);

    return new PagedResult<PersonListItemDto>(items, total, page, pageSize, totalPages);
  }

  public async Task<PersonDto?> GetAsync(int id)
  {
    var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.PersonId == id);
    if (person is null)
    {
      return null;
    }

    var family = await FamilyMemberProjection(db.FamilyCompositions.AsNoTracking()
        .Where(f => f.PersonId == id && f.Status && f.FamilyPerson!.Status)
        .OrderBy(f => f.FamilyCompId))
      .ToListAsync();
    var familyOf = await FamilyOfProjection(db.FamilyCompositions.AsNoTracking()
        .Where(f => f.FamilyPersonId == id && f.Status && f.Person.Status)
        .OrderBy(f => f.FamilyCompId))
      .ToListAsync();

    var place = person.BarangayId is { } barangayId
      ? await db.Barangays.AsNoTracking()
          .Where(b => b.BarangayId == barangayId)
          .Select(b => new { b.CityMunicipalityId, b.CityMunicipality!.ProvinceId })
          .FirstOrDefaultAsync()
      : null;

    var dto = ToDto(person, family);
    dto.FamilyOf = familyOf;
    dto.CityMunicipalityId = place?.CityMunicipalityId;
    dto.ProvinceId = place?.ProvinceId;
    return dto;
  }

  public async Task<PagedResult<TransactionResponseDto>?> GetTransactionHistoryAsync(
    int personId,
    int page,
    CancellationToken cancellationToken = default)
  {
    if (!await db.People.AnyAsync(person => person.PersonId == personId, cancellationToken))
    {
      return null;
    }

    const int pageSize = 10;
    var query = db.Transactions
      .AsNoTracking()
      .Where(ClaimRules.Counted)
      .Where(transaction =>
        transaction.PatientPersonId == personId || transaction.RepresentativePersonId == personId);
    var total = await query.CountAsync(cancellationToken);
    var transactions = await query
      .OrderByDescending(transaction => transaction.Date)
      .ThenByDescending(transaction => transaction.TransactionId)
      .Include(transaction => transaction.TransactionServices)
      .Include(transaction => transaction.PatientPerson)
      .Include(transaction => transaction.RepresentativePerson)
      .Include(transaction => transaction.SocialWorkerUser)
      .Include(transaction => transaction.LaneType)
      .Include(transaction => transaction.Creditor)
      .Include(transaction => transaction.Mode)
      .Include(transaction => transaction.FundSource)
        .ThenInclude(fundSource => fundSource!.Source)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .ToListAsync(cancellationToken);

    return new PagedResult<TransactionResponseDto>(
      transactions.Select(TransactionResponseMapper.ToResponse).ToList(),
      total,
      page,
      pageSize,
      (int)Math.Ceiling((double)total / pageSize));
  }

  public async Task<PersonResult> CreateAsync(int actorId, SavePersonDto dto, bool basicsOnly = false)
  {
    if (basicsOnly && (dto.CivilStatusId is not null || Clean(dto.SkillOccupation) is not null || dto.EstimatedMonthlyIncome is not null))
    {
      return new PersonResult(PersonOutcome.Forbidden,
        "Civil status, occupation and income are filled in by the social worker.");
    }
    // re-scaffolding drops the = true default
    var person = new Person { CreatedBy = actorId, Status = true };
    var error = await ApplyAsync(person, dto);
    if (error is not null)
    {
      return new PersonResult(PersonOutcome.Invalid, error);
    }

    await SaveLock.WaitAsync();
    try
    {
      var (stop, confirmed) = await CheckDuplicateAsync(person, dto);
      if (stop is not null)
      {
        return stop;
      }

      db.People.Add(person);
      await db.SaveChangesAsync();
      if (confirmed.Count > 0)
      {
        LogSavedAsDuplicate(actorId, person.PersonId, confirmed, dto.DuplicateReason);
        await db.SaveChangesAsync();
      }
    }
    finally
    {
      SaveLock.Release();
    }
    return new PersonResult(PersonOutcome.Ok, Person: await GetAsync(person.PersonId));
  }

  public async Task<PersonResult> UpdateAsync(int actorId, int id, SavePersonDto dto, bool basicsOnly = false)
  {
    var person = await db.People.FirstOrDefaultAsync(p => p.PersonId == id && p.Status);
    if (person is null)
    {
      return new PersonResult(PersonOutcome.NotFound, "Person not found.");
    }
    if (dto.Version is not null && dto.Version != VersionOf(person))
    {
      return new PersonResult(PersonOutcome.Stale,
        "Someone else changed this person while you were editing. Reload to see their changes, then edit again.");
    }

    // the front desk keeps to the basics; only a real change counts, the page resends everything
    if (basicsOnly && (dto.CivilStatusId != person.CivilStatusId
        || Clean(dto.SkillOccupation) != Clean(person.SkillOccupation)
        || dto.EstimatedMonthlyIncome != person.EstimatedMonthlyIncome))
    {
      return new PersonResult(PersonOutcome.Forbidden,
        "Civil status, occupation and income are filled in by the social worker.");
    }

    var before = (person.LastName, person.FirstName, person.MiddleName, person.NameSuffixId, person.Birthdate);
    var oldValues = Snapshot(person);
    var error = await ApplyAsync(person, dto);
    if (error is not null)
    {
      return new PersonResult(PersonOutcome.Invalid, error);
    }

    // only check renames, so existing duplicates can still be edited
    var renamed = before != (person.LastName, person.FirstName, person.MiddleName, person.NameSuffixId, person.Birthdate);
    if (renamed)
    {
      await SaveLock.WaitAsync();
    }
    try
    {
      var (stop, confirmed) = renamed ? await CheckDuplicateAsync(person, dto) : (null, []);
      if (stop is not null)
      {
        return stop;
      }
      if (confirmed.Count > 0)
      {
        LogSavedAsDuplicate(actorId, id, confirmed, dto.DuplicateReason);
      }
      LogChanges(actorId, id, oldValues, Snapshot(person));

      person.LastUpdatedBy = actorId;
      person.DateLastUpdated = Now();
      await db.SaveChangesAsync();
    }
    finally
    {
      if (renamed)
      {
        SaveLock.Release();
      }
    }
    return new PersonResult(PersonOutcome.Ok, Person: await GetAsync(id));
  }

  public async Task<FamilyMemberResult> AddFamilyMemberAsync(int actorId, int personId, SaveFamilyMemberDto dto)
  {
    if (!await db.People.AnyAsync(p => p.PersonId == personId && p.Status))
    {
      return new FamilyMemberResult(PersonOutcome.NotFound, "Person not found.");
    }
    var error = await ValidateFamilyMemberAsync(personId, dto, exceptFamilyCompId: null) ?? RemarkError(dto.Remark);
    if (error is not null)
    {
      return new FamilyMemberResult(PersonOutcome.Invalid, error);
    }

    await using var transaction = await db.Database.BeginTransactionAsync();
    var familyComposition = new FamilyComposition
    {
      PersonId = personId,
      FamilyPersonId = dto.FamilyPersonId,
      RelationshipStatusId = dto.RelationshipStatusId,
      Status = true,
      CreatedBy = actorId,
    };
    db.FamilyCompositions.Add(familyComposition);
    await db.SaveChangesAsync();
    LogFamily(actorId, personId, familyComposition.FamilyCompId, "Added",
      null, await LinkValuesAsync(dto.FamilyPersonId, dto.RelationshipStatusId), dto.Remark);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    var familyMember = await FamilyMemberProjection(db.FamilyCompositions.AsNoTracking()
      .Where(f => f.FamilyCompId == familyComposition.FamilyCompId)).SingleAsync();
    return new FamilyMemberResult(PersonOutcome.Ok, FamilyMember: familyMember);
  }

  public async Task<FamilyMemberResult> UpdateFamilyMemberAsync(
    int actorId, int personId, int familyCompId, SaveFamilyMemberDto dto)
  {
    var familyComposition = await db.FamilyCompositions.FirstOrDefaultAsync(f =>
      f.PersonId == personId && f.FamilyCompId == familyCompId && f.Status);
    if (familyComposition is null)
    {
      return new FamilyMemberResult(PersonOutcome.NotFound, "Family member not found.");
    }

    var error = await ValidateFamilyMemberAsync(personId, dto, exceptFamilyCompId: familyCompId) ?? RemarkError(dto.Remark);
    if (error is not null)
    {
      return new FamilyMemberResult(PersonOutcome.Invalid, error);
    }

    if (familyComposition.FamilyPersonId != dto.FamilyPersonId
      || familyComposition.RelationshipStatusId != dto.RelationshipStatusId)
    {
      LogFamily(actorId, personId, familyCompId, "Changed",
        await LinkValuesAsync(familyComposition.FamilyPersonId, familyComposition.RelationshipStatusId),
        await LinkValuesAsync(dto.FamilyPersonId, dto.RelationshipStatusId), dto.Remark);
    }
    familyComposition.FamilyPersonId = dto.FamilyPersonId;
    familyComposition.RelationshipStatusId = dto.RelationshipStatusId;
    familyComposition.LastUpdatedBy = actorId;
    familyComposition.DateLastUpdated = Now();
    await db.SaveChangesAsync();

    var familyMember = await FamilyMemberProjection(db.FamilyCompositions.AsNoTracking()
      .Where(f => f.FamilyCompId == familyCompId)).SingleAsync();
    return new FamilyMemberResult(PersonOutcome.Ok, FamilyMember: familyMember);
  }

  public async Task<FamilyMemberResult> RemoveFamilyMemberAsync(int actorId, int personId, int familyCompId, string? remark)
  {
    if (RemarkError(remark) is { } error)
    {
      return new FamilyMemberResult(PersonOutcome.Invalid, error);
    }
    var familyComposition = await db.FamilyCompositions.FirstOrDefaultAsync(f =>
      f.PersonId == personId && f.FamilyCompId == familyCompId && f.Status);
    if (familyComposition is null)
    {
      return new FamilyMemberResult(PersonOutcome.NotFound, "Family member not found.");
    }

    LogFamily(actorId, personId, familyCompId, "Removed",
      await LinkValuesAsync(familyComposition.FamilyPersonId, familyComposition.RelationshipStatusId), null, remark);
    familyComposition.Status = false;
    familyComposition.LastUpdatedBy = actorId;
    familyComposition.DateLastUpdated = Now();
    await db.SaveChangesAsync();
    return new FamilyMemberResult(PersonOutcome.Ok);
  }

  private async Task<string?> ValidateFamilyMemberAsync(int personId, SaveFamilyMemberDto dto, int? exceptFamilyCompId)
  {
    if (dto.RelationshipStatusId is not { } relationshipStatusId)
    {
      return "Choose the relationship.";
    }
    if (!await db.RelationshipStatuses.AnyAsync(r => r.RelationshipStatusId == relationshipStatusId))
    {
      return "Relationship status not found.";
    }
    if (!await db.People.AnyAsync(p => p.PersonId == dto.FamilyPersonId && p.Status))
    {
      return "Family person not found.";
    }
    if (dto.FamilyPersonId == personId)
    {
      return "A person can't be listed as their own family member.";
    }
    if (await db.FamilyCompositions.AnyAsync(f =>
          f.PersonId == personId && f.FamilyPersonId == dto.FamilyPersonId && f.Status
          && f.FamilyCompId != exceptFamilyCompId))
    {
      return "This person is already listed as a family member.";
    }
    return null;
  }

  private static IQueryable<FamilyMemberDto> FamilyMemberProjection(IQueryable<FamilyComposition> query) =>
    query.Select(f => new FamilyMemberDto(
      f.FamilyCompId,
      f.FamilyPersonId,
      f.FamilyPerson!.FirstName,
      f.FamilyPerson.MiddleName,
      f.FamilyPerson.LastName,
      f.RelationshipStatusId,
      f.RelationshipStatus!.RelationshipStatusDesc,
      f.RelationshipStatus.RelationshipGroup,
      f.FamilyPerson.Sex != null ? f.FamilyPerson.Sex.SexDesc : f.FamilyPerson.Gender,
      f.FamilyPerson.Birthdate,
      f.FamilyPerson.CivilStatus != null ? f.FamilyPerson.CivilStatus.CivilStatusDesc : null,
      f.FamilyPerson.EstimatedMonthlyIncome,
      f.FamilyPerson.ContactNo,
      f.FamilyPerson.SkillOccupation));

  private static IQueryable<FamilyMemberDto> FamilyOfProjection(IQueryable<FamilyComposition> query) =>
    query.Select(f => new FamilyMemberDto(
      f.FamilyCompId,
      f.PersonId,
      f.Person.FirstName,
      f.Person.MiddleName,
      f.Person.LastName,
      f.RelationshipStatusId,
      f.RelationshipStatus!.RelationshipStatusDesc,
      f.RelationshipStatus.RelationshipGroup,
      f.Person.Sex != null ? f.Person.Sex.SexDesc : f.Person.Gender,
      f.Person.Birthdate,
      f.Person.CivilStatus != null ? f.Person.CivilStatus.CivilStatusDesc : null,
      f.Person.EstimatedMonthlyIncome,
      f.Person.ContactNo,
      f.Person.SkillOccupation));

  private const string FamilyEntity = "FamilyComposition";
  private const int MaxRemark = 500;
  private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

  private static string? RemarkError(string? remark) =>
    remark?.Trim().Length > MaxRemark ? $"The remark can be at most {MaxRemark} characters." : null;

  private async Task<FamilyLinkValues> LinkValuesAsync(int? familyPersonId, short? relationshipId)
  {
    var member = await db.People.AsNoTracking()
      .Where(p => p.PersonId == familyPersonId)
      .Select(p => new { p.LastName, p.FirstName, p.MiddleName })
      .FirstOrDefaultAsync();
    var relationship = await db.RelationshipStatuses.AsNoTracking()
      .Where(r => r.RelationshipStatusId == relationshipId)
      .Select(r => r.RelationshipStatusDesc)
      .FirstOrDefaultAsync();
    var name = member is null
      ? null
      : string.Join(" ", new[] { member.LastName is null ? null : $"{member.LastName},", member.FirstName, member.MiddleName }
          .Where(n => !string.IsNullOrWhiteSpace(n)));
    return new FamilyLinkValues(familyPersonId, name, relationshipId, relationship);
  }

  private void LogFamily(int actorId, int personId, int familyCompId, string action,
    FamilyLinkValues? oldValues, FamilyLinkValues? newValues, string? remark) =>
    db.AuditLogs.Add(new AuditLog
    {
      Entity = FamilyEntity,
      EntityId = familyCompId,
      PersonId = personId,
      Action = action,
      OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, LogJson),
      NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, LogJson),
      Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
      UserId = actorId,
      DateCreated = Now(),
    });

  public async Task<List<FamilyChangeDto>?> FamilyHistoryAsync(int personId)
  {
    if (!await db.People.AnyAsync(p => p.PersonId == personId))
    {
      return null;
    }
    var rows = await db.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == FamilyEntity && a.PersonId == personId)
      .OrderByDescending(a => a.DateCreated).ThenByDescending(a => a.AuditLogId)
      .Select(a => new
      {
        a.AuditLogId, a.EntityId, a.Action, a.OldValues, a.NewValues, a.Remark, a.DateCreated,
        a.User.Lastname, a.User.Firstname, a.User.Username,
      })
      .ToListAsync();
    return rows.Select(a => new FamilyChangeDto(
        a.AuditLogId,
        a.EntityId,
        a.Action,
        a.OldValues is null ? null : JsonSerializer.Deserialize<FamilyLinkValues>(a.OldValues, LogJson),
        a.NewValues is null ? null : JsonSerializer.Deserialize<FamilyLinkValues>(a.NewValues, LogJson),
        a.Remark,
        a.Lastname is null && a.Firstname is null ? a.Username : $"{a.Lastname}, {a.Firstname}".Trim(' ', ','),
        a.DateCreated))
      .ToList();
  }

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

    // old names can have commas and slashes, only check changed ones
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

    // reports still read the text columns; old rows may only have the text, so only clear it when an id is removed
    if (sex is not null)
    {
      person.Gender = sex.SexDesc;
    }
    else if (person.SexId is not null)
    {
      person.Gender = null;
    }
    person.SexId = dto.SexId;

    if (barangay is not null)
    {
      person.Barangay = barangay.BarangayName;
      person.Municipality = barangay.Municipality;
    }
    else if (person.BarangayId is not null)
    {
      person.Barangay = null;
      person.Municipality = null;
    }
    person.BarangayId = dto.BarangayId;
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
    Version = VersionOf(p),
    Family = family,
  };

  private static string VersionOf(Person p) =>
    (p.DateLastUpdated?.Ticks ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

  private static Dictionary<string, object?> Snapshot(Person p) => new()
  {
    ["lastName"] = p.LastName,
    ["firstName"] = p.FirstName,
    ["middleName"] = p.MiddleName,
    ["nameSuffixId"] = p.NameSuffixId,
    ["sexId"] = p.SexId,
    ["civilStatusId"] = p.CivilStatusId,
    ["birthdate"] = p.Birthdate,
    ["contactNo"] = p.ContactNo,
    ["skillOccupation"] = p.SkillOccupation,
    ["estimatedMonthlyIncome"] = p.EstimatedMonthlyIncome,
    ["barangayId"] = p.BarangayId,
    ["barangay"] = p.Barangay,
    ["municipality"] = p.Municipality,
    ["purokSubdivision"] = p.PurokSubdivision,
    ["street"] = p.Street,
    ["noBuildingName"] = p.NoBuildingName,
    ["isSeniorCitizen"] = p.IsSeniorCitizen == true,
    ["isSoloParent"] = p.IsSoloParent == true,
    ["isPwd"] = p.IsPwd == true,
    ["isRr"] = p.IsRr == true,
    ["is4P"] = p.Is4P == true,
  };

  // only the fields that changed
  private void LogChanges(int actorId, int personId, Dictionary<string, object?> before, Dictionary<string, object?> after)
  {
    var changed = after.Keys.Where(k => !Equals(before[k], after[k])).ToList();
    if (changed.Count == 0)
    {
      return;
    }
    db.AuditLogs.Add(new AuditLog
    {
      Entity = PersonEntity,
      EntityId = personId,
      PersonId = personId,
      Action = "Updated",
      OldValues = JsonSerializer.Serialize(changed.ToDictionary(k => k, k => before[k]), LogJson),
      NewValues = JsonSerializer.Serialize(changed.ToDictionary(k => k, k => after[k]), LogJson),
      UserId = actorId,
      DateCreated = Now(),
    });
  }

  public async Task<List<PersonChangeDto>?> HistoryAsync(int personId)
  {
    if (!await db.People.AnyAsync(p => p.PersonId == personId))
    {
      return null;
    }
    var rows = await db.AuditLogs.AsNoTracking()
      .Where(a => a.Entity == PersonEntity && a.EntityId == personId)
      .OrderByDescending(a => a.DateCreated).ThenByDescending(a => a.AuditLogId)
      .Select(a => new { a.AuditLogId, a.Action, a.OldValues, a.NewValues, a.Remark, a.DateCreated,
        a.User.Lastname, a.User.Firstname, a.User.Username })
      .ToListAsync();
    return rows.Select(a => new PersonChangeDto(
        a.AuditLogId, a.Action, a.OldValues, a.NewValues, a.Remark,
        a.Lastname is null && a.Firstname is null ? a.Username : $"{a.Lastname}, {a.Firstname}".Trim(' ', ','),
        a.DateCreated))
      .ToList();
  }

  private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  private static string? Name(string? value) =>
    Clean(value) is { } name ? Spaces().Replace(name, " ").ToUpperInvariant() : null;

  [GeneratedRegex(@"\s+")]
  private static partial Regex Spaces();

  private static string EscapeLike(string value) =>
    value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

  private DateTime Now() => time.GetLocalNow().DateTime;
}
