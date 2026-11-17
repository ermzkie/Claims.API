namespace Claims.API.DTOs.Persons;

// Level is "same" or "similar"; Reason says why, e.g. "Middle name is only an initial"
public record PersonMatchDto(
  int PersonId,
  string Name,
  DateOnly? Birthdate,
  string? Address,
  int Claims,
  string Level,
  string Reason);

public class PersonMatchQueryDto
{
  public string? LastName { get; set; }
  public string? FirstName { get; set; }
  public string? MiddleName { get; set; }
  public short? NameSuffixId { get; set; }
  public DateOnly? Birthdate { get; set; }

  // leave out the person being edited
  public int? ExceptPersonId { get; set; }
}
