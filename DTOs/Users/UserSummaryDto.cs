namespace Claims.API.DTOs.Users;

public class UserSummaryDto
{
  public int UserId { get; set; }
  public string Username { get; set; } = string.Empty;
  public string? Firstname { get; set; }
  public string? Middlename { get; set; }
  public string? Lastname { get; set; }
  public short? NameSuffixId { get; set; }
  public IReadOnlyList<string> Roles { get; set; } = [];
  public bool Status { get; set; }
  public bool MustChangePassword { get; set; }
  public DateTime? LockedUntil { get; set; }
  public DateTime? LastLoginDate { get; set; }
}
