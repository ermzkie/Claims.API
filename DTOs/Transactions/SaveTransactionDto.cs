using System.ComponentModel.DataAnnotations;

namespace Claims.API.DTOs.Transactions;

public class SaveTransactionDto
{
  [Range(1, int.MaxValue)]
  public int? TransactionId { get; set; }

  public DateTime? Date { get; set; }
  public short? LaneTypeId { get; set; }
  public int? PatientPersonId { get; set; }
  public int? RepresentativePersonId { get; set; }
  public int? FundSourceId { get; set; }
  public int? CreditorId { get; set; }
  public short? ModeId { get; set; }
  public decimal? Amount { get; set; }
  public int? SocialWorkerUserId { get; set; }
  public string? SocialWorkerAssessment { get; set; }
  public string? Remarks { get; set; }
  // needed when the claim's lane needs an endorsement
  public string? EndorsedBy { get; set; }
  public string? EndorsementReason { get; set; }
  public bool? IsReleased { get; set; }
  // program code from /api/lookups/assistance-programs
  public string? AssistanceCode { get; set; }
  // same person already open in today's queue; the front desk confirms a real second visit
  public bool ConfirmSameDay { get; set; }
  // null keeps the representative, so removing one needs its own flag
  public bool ClearRepresentative { get; set; }
  public List<short>? NatureIds { get; set; }
}
