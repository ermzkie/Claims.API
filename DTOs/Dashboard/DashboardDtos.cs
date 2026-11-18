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
  int WaitingClaims,
  decimal WaitingAmount,
  IReadOnlyList<DashboardProgramDto> ByProgram,
  IReadOnlyList<DashboardCreditorDto> TopCreditors,
  IReadOnlyList<DashboardMonthDto> Trend,
  IReadOnlyList<DashboardClaimDto> Waiting);

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

public record DashboardBeneficiariesDto(
  int Year,
  int Month,
  string Period,
  int Claims,
  int Beneficiaries,
  int ClaimsWithoutPatient,
  int FirstTime,
  int Returning,
  IReadOnlyList<DashboardGroupDto> Municipalities,
  IReadOnlyList<DashboardGroupDto> Sexes,
  IReadOnlyList<DashboardGroupDto> AgeGroups,
  IReadOnlyList<DashboardGroupDto> Sectors);

public record DashboardGroupDto(string Name, int Persons, int Claims, decimal Amount);

public record DashboardBudgetDto(string? Source, int? Year, decimal Released, decimal Obligated);
