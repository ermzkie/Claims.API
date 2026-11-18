namespace Claims.API.DTOs.Persons;

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

  public int? ExceptPersonId { get; set; }
}
