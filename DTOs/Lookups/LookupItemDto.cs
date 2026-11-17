using System.Text.Json.Serialization;

namespace Claims.API.DTOs.Lookups;

public record LookupItemDto(
  int Id,
  string? Code,
  string Name,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Group = null);
