namespace Claims.API.DTOs.ReferenceData;

// Groups: allowed groups, relationships only
public record ListInfoDto(string Key, string Title, bool HasCode, IReadOnlyList<string>? Groups = null);

// UsedBy: how many records point to this value. Only unused values can be deleted.
public record ListItemDto(int Id, string? Code, string Name, int UsedBy, string? Group = null);

public class SaveListItemDto
{
  public string? Code { get; set; }
  public string? Name { get; set; }
  public string? Group { get; set; }
}
