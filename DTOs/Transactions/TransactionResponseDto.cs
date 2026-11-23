namespace Claims.API.DTOs.Transactions;

public class TransactionResponseDto
{
  public int TransactionId { get; set; }
  public string? AssistanceCode { get; set; }
  public short? QueueNo { get; set; }
  public string? QueueNoCode { get; set; }
  public string? RefNo { get; set; }
  public DateTime? Date { get; set; }
  public short? LaneTypeId { get; set; }
  public string? LaneTypeDesc { get; set; }
  public int? PatientPersonId { get; set; }
  public string? PatientPersonName { get; set; }
  public int? RepresentativePersonId { get; set; }
  public string? RepresentativePersonName { get; set; }
  public string? Representative { get; set; }
  public int? FundSourceId { get; set; }
  public string? SourceDesc { get; set; }
  public int? CreditorId { get; set; }
  public string? CreditorName { get; set; }
  public short? ModeId { get; set; }
  public string? ModeDesc { get; set; }
  public decimal? Amount { get; set; }
  public int? SocialWorkerUserId { get; set; }
  public string? SocialWorkerUserFullName { get; set; }
  public string? SocialWorkerAssessment { get; set; }
  public string? Remarks { get; set; }
  public string? EndorsedBy { get; set; }
  public string? EndorsementReason { get; set; }
  public bool? IsReleased { get; set; }
  public bool? Status { get; set; }
  public int? CreatedBy { get; set; }
  public DateTime? DateCreated { get; set; }
  public int? LastUpdatedBy { get; set; }
  public DateTime? DateLastUpdated { get; set; }
  public List<short> NatureIds { get; set; } = [];

  public int? ApprovalStatusId {get; set;}
}
