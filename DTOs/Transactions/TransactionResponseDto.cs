namespace Claims.API.DTOs.Transactions;

public class TransactionResponseDto
{
  public int TransactionId { get; set; }
  public string? AssistanceCode { get; set; }
  public string? RefNo { get; set; }
  public string? SocialWorkerId { get; set; }
  public DateTime? Date { get; set; }
  public int? PatientPersonId { get; set; }
  public int? RepresentativePersonId { get; set; }
  public string? Representative { get; set; }
  public int? FundSourceId { get; set; }
  public int? CreditorId { get; set; }
  public short? ModeId { get; set; }
  public decimal? Amount { get; set; }
  public string? SocialWorkerAssessment { get; set; }
  public string? Remarks { get; set; }
  public bool? IsReleased { get; set; }
  public bool? Status { get; set; }
  public int? CreatedBy { get; set; }
  public DateTime? DateCreated { get; set; }
  public int? LastUpdatedBy { get; set; }
  public DateTime? DateLastUpdated { get; set; }
  public int? SocialWorkerUserId { get; set; }
  public List<short> NatureIds { get; set; } = [];
}
