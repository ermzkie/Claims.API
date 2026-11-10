namespace Claims.API.DTOs.Auth;

public class CurrentUserDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Firstname { get; set; }
    public string? Middlename { get; set; }
    public string? Lastname { get; set; }
    public bool MustChangePassword { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}
