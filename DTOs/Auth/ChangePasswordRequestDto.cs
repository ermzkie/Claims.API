namespace Claims.API.DTOs.Auth;

public class ChangePasswordRequestDto
{
  public string? CurrentPassword { get; set; }
  public string? NewPassword { get; set; }
}
