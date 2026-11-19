namespace Claims.API.DTOs.Creditor;

public record CreditorTotalsDto(
  int CreditorId,
  int Claims,
  decimal Amount,
  DateTime? LastClaim,
  int AllClaims);

public record CreditorYearDto(int Year, int Claims, decimal Amount);

public record CreditorSummaryDto(
  int CreditorId,
  string? CreditorName,
  bool IsActive,
  int Claims,
  decimal Amount,
  DateTime? FirstClaim,
  DateTime? LastClaim,
  int AllClaims,
  int ClaimsWithoutDate,
  IReadOnlyList<CreditorYearDto> ByYear);

public record CreditorClaimDto(
  int TransactionId,
  string? RefNo,
  DateTime? Date,
  int? PatientPersonId,
  string? PatientName,
  string? AssistanceCode,
  string? AssistanceType,
  decimal? Amount);

public record CreditorClaimsPageDto(
  int Page,
  int PageSize,
  int TotalRecords,
  int TotalPages,
  decimal TotalAmount,
  IReadOnlyList<CreditorClaimDto> Data);

public record CreditorProgramDto(string Code, string? Type, int Claims);

public record ClaimPersonDto(int PersonId, string? Name, string? Sex, string? Address);

public record CreditorClaimDetailDto(
  int TransactionId,
  string? RefNo,
  DateTime? Date,
  decimal? Amount,
  string? AssistanceCode,
  string? AssistanceType,
  IReadOnlyList<string> NaturesOfService,
  bool? IsReleased,
  bool IsActive,
  ClaimPersonDto? Patient,
  int? RepresentativePersonId,
  string? RepresentativeName,
  string? Mode,
  string? FundSource,
  string? SocialWorker,
  string? SocialWorkerPosition,
  string? SocialWorkerAssessment,
  string? Remarks,
  string? CreatedBy,
  DateTime? DateCreated,
  string? LastUpdatedBy,
  DateTime? DateLastUpdated);

public record CreditorMonthDto(int Month, int Claims, decimal Amount);
