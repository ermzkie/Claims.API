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

public record PersonListItemDto(
  int PersonId,
  string? FirstName,
  string? MiddleName,
  string? LastName,
  string? Sex,
  string? Barangay,
  string? Municipality,
  DateOnly? Birthdate);
