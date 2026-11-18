
namespace Claims.API.DTOs.Reports;

public class TransactionReportDto
{
    public int TransactionId { get; set; }
    public string? AssistanceCode { get; set; }
    public string? RefNo { get; set; }
    public DateTime? Date { get; set; }

    public int? PatientPersonId { get; set; }
    public string? PatientName { get; set; }
    public string? PatientAddress { get; set; }

    public string? Representative { get; set; }

    public decimal? Amount { get; set; }

    public int? CreditorId { get; set; }

public bool? IsReleased { get; set; }
    public bool Status { get; set; }
}
