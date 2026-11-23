namespace Claims.API.DTOs.ReferenceData;

public record ListFlagDto(string Key, string Label);

public record ListInfoDto(
  string Key, string Title, bool HasCode, IReadOnlyList<string>? Groups = null, IReadOnlyList<ListFlagDto>? Flags = null);

// Locked: the code can't change and the value can't be deleted
public record ListItemDto(
  int Id, string? Code, string Name, int UsedBy, string? Group = null,
  IReadOnlyDictionary<string, bool>? Flags = null, bool Locked = false);

public class SaveListItemDto
{
  public string? Code { get; set; }
  public string? Name { get; set; }
  public string? Group { get; set; }
  // a flag left out keeps its value
  public Dictionary<string, bool>? Flags { get; set; }
}
