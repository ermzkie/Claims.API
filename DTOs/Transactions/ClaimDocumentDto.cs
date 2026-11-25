namespace Claims.API.DTOs.Transactions;

// what the printed guarantee letter / credit slip shows
public record ClaimDocumentDto(
  string Title,
  string ControlNo,
  DateTime Date,
  string? CreditorName,
  string PatientName,
  string? Address,
  string? RepresentativeName,
  string? ProgramDesc,
  List<string> Needs,
  decimal Amount,
  string? FundSource,
  string? SocialWorkerName,
  string? ApproverName,
  DateTime ApprovedDate,
  bool Reprint);
