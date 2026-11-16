namespace Claims.API.DTOs.Dashboard;

public record DashboardDto(
  int Year,
  int Month,
  int Claims,
  decimal Amount,
  int PreviousClaims,
  decimal PreviousAmount,
  int YearClaims,
  decimal YearAmount,
  int NewPersons,
  IReadOnlyList<DashboardProgramDto> ByProgram,
  IReadOnlyList<DashboardCreditorDto> TopCreditors,
  IReadOnlyList<DashboardMonthDto> Trend,
  IReadOnlyList<DashboardClaimDto> Recent);

public record DashboardProgramDto(string Code, string? Name, int Claims, decimal Amount);

public record DashboardCreditorDto(int CreditorId, string? Name, int Claims, decimal Amount);

public record DashboardMonthDto(int Year, int Month, int Claims, decimal Amount);

public record DashboardClaimDto(
  int TransactionId,
  string? RefNo,
  DateTime? Date,
  int? PatientPersonId,
  string? PatientName,
  string? AssistanceCode,
  string? Program,
  int? CreditorId,
  string? CreditorName,
  decimal? Amount);
