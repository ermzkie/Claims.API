namespace Claims.API.DTOs.Persons;

// update replaces every field
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
  // null keeps it while the date is unchanged; a new date without it is exact
  public bool? BirthdateEstimated { get; set; }
  public string? ContactNo { get; set; }
  public string? SkillOccupation { get; set; }
  public decimal? EstimatedMonthlyIncome { get; set; }

  // the Version from GET; left out = no check
  public string? Version { get; set; }

  public bool ConfirmDuplicate { get; set; }
  public string? DuplicateReason { get; set; }
}

public record PersonChangeDto(
  int AuditLogId, string Action, string? OldValues, string? NewValues, string? Remark, string? ChangedBy, DateTime Date);
