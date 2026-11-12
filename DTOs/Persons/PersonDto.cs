namespace Claims.API.DTOs.Persons;

// Sex, Barangay and Municipality are the text columns: old records only have the text, not the IDs.
public class PersonDto
{
  public int PersonId { get; set; }
  public string? FirstName { get; set; }
  public string? MiddleName { get; set; }
  public string? LastName { get; set; }
  public short? NameSuffixId { get; set; }
  public short? SexId { get; set; }
  public string? Sex { get; set; }
  public short? CivilStatusId { get; set; }
  public string? NoBuildingName { get; set; }
  public string? Street { get; set; }
  public string? PurokSubdivision { get; set; }
  public int? BarangayId { get; set; }
  public int? CityMunicipalityId { get; set; }
  public int? ProvinceId { get; set; }
  public string? Barangay { get; set; }
  public string? Municipality { get; set; }
  public bool? IsSeniorCitizen { get; set; }
  public bool? IsSoloParent { get; set; }
  public bool? IsPwd { get; set; }
  public bool? IsRr { get; set; }
  public bool? Is4P { get; set; }
  public DateOnly? Birthdate { get; set; }
  public string? ContactNo { get; set; }
  public string? SkillOccupation { get; set; }
  public decimal? EstimatedMonthlyIncome { get; set; }
  public DateTime DateCreated { get; set; }
  public IReadOnlyList<FamilyMemberDto> Family { get; set; } = [];
}

public record FamilyMemberDto(
  int FamilyCompId,
  int? PersonId,
  string? FirstName,
  string? MiddleName,
  string? LastName,
  short? RelationshipStatusId,
  string? Relationship);

// Search filters; all are optional and combine with each other and with the search text.
public class PersonSearchDto
{
  public string? Search { get; set; }
  public int? CityMunicipalityId { get; set; }
  public int? BarangayId { get; set; }
  public short? SexId { get; set; }

  // senior, pwd, 4ps, solo-parent; a person must have all of the ones given.
  public List<string>? Sector { get; set; }

  public int Page { get; set; } = 1;
  public int PageSize { get; set; } = 20;
}

public record PersonListItemDto(
  int PersonId,
  string? FirstName,
  string? MiddleName,
  string? LastName,
  string? Sex,
  string? Barangay,
  string? Municipality,
  DateOnly? Birthdate,
  bool IsSeniorCitizen,
  bool IsSoloParent,
  bool IsPwd,
  bool Is4P);
