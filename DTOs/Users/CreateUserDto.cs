namespace Claims.API.DTOs.Users;

public class CreateUserDto
{
  public string? Username { get; set; }
  public string? Firstname { get; set; }
  public string? Middlename { get; set; }
  public string? Lastname { get; set; }
  public short? NameSuffixId { get; set; }
  public string? TemporaryPassword { get; set; }
  public List<short>? RoleIds { get; set; }
}
