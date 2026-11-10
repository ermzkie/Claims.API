namespace Claims.API.DTOs.Users;

public class UpdateUserDto
{
    public string? Firstname { get; set; }
    public string? Middlename { get; set; }
    public string? Lastname { get; set; }
    public short? NameSuffixId { get; set; }
}
