namespace Claims.API.DTOs.Lists;

public record ListInfoDto(string Key, string Title, bool HasCode);

// UsedBy: how many records point to this value. Only unused values can be deleted.
public record ListItemDto(int Id, string? Code, string Name, int UsedBy);

public class SaveListItemDto
{
  public string? Code { get; set; }
  public string? Name { get; set; }
}
