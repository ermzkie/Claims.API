using System.Text.Json.Serialization;

namespace Claims.API.DTOs.Lookups;

public record LookupItemDto(
  int Id,
  string? Code,
  string Name,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Group = null);

// lanes that are on; the front desk asks for the endorsement when NeedsEndorsement
public record LaneLookupDto(int Id, string? Code, string Name, bool NeedsEndorsement);
