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

// set when the group was marked; Decision is "same" or "different"
public record DuplicateReviewDto(string Decision, string? By, DateTime At, string? Remark);

public record DuplicateDecisionDto(IReadOnlyList<int>? PersonIds, string? Remark);

// stored in AuditLog.NewValues
public record DuplicateGroupValues(IReadOnlyList<int> PersonIds);

public record PersonReviewPageDto<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);
