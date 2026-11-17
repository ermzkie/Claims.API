namespace Claims.API.DTOs.Persons;

public record PersonReviewRowDto(
  int PersonId,
  string? Name,
  DateOnly? Birthdate,
  string? Sex,
  string? Address,
  string? ContactNo,
  DateTime DateCreated,
  int Claims,
  int AsRepresentative,
  DateTime? LastClaimDate,
  int FamilyLinks);

public record DuplicateGroupDto(
  string LastName,
  string FirstName,
  string? MiddleName,
  DateOnly? Birthdate,
  int Claims,
  IReadOnlyList<PersonReviewRowDto> Persons,
  DuplicateReviewDto? Review);

public record DuplicateReviewDto(string Decision, string? By, DateTime At, string? Remark, int? KeepPersonId);

public record DuplicateDecisionDto(IReadOnlyList<int>? PersonIds, string? Remark, int? KeepPersonId = null);

public record DuplicateGroupValues(IReadOnlyList<int> PersonIds, int? KeepPersonId = null);

public record PersonReviewClaimDto(
  int TransactionId,
  string? RefNo,
  DateTime? Date,
  string Role,
  string? OtherPerson,
  int? OtherPersonId,
  string? AssistanceCode,
  string? Program,
  int? CreditorId,
  string? CreditorName,
  decimal? Amount,
  string? SocialWorker);

public record PersonReviewPageDto<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);
