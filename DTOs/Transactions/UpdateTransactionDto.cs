using System.ComponentModel.DataAnnotations;

namespace Claims.API.DTOs.Transactions;

public class UpdateTransactionDto
{
  [StringLength(50)]
  public string? AssistanceCode { get; set; }

  [StringLength(50)]
  public string? RefNo { get; set; }

  [StringLength(50)]
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
}
