namespace Claims.API.DTOs.Desks;

public record DeskDto(int DeskId, string Name, int SortOrder, bool Status);

public record SaveDeskDto(string? Name, int SortOrder = 0, bool Status = true);
