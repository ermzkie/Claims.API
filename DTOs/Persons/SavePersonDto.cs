namespace Claims.API.DTOs.Persons;

// Used for both create and update; an update replaces every field.
public class SavePersonDto
{
  public string? FirstName { get; set; }
  public string? MiddleName { get; set; }
  public string? LastName { get; set; }
  public short? NameSuffixId { get; set; }
  public short? SexId { get; set; }
  public short? CivilStatusId { get; set; }
  public string? NoBuildingName { get; set; }
  public string? Street { get; set; }
  public string? PurokSubdivision { get; set; }
  public int? BarangayId { get; set; }
  public bool? IsSeniorCitizen { get; set; }
  public bool? IsSoloParent { get; set; }
  public bool? IsPwd { get; set; }
  public bool? IsRr { get; set; }
  public bool? Is4P { get; set; }
  public DateOnly? Birthdate { get; set; }
  public string? ContactNo { get; set; }
  public string? SkillOccupation { get; set; }
  public decimal? EstimatedMonthlyIncome { get; set; }
}
