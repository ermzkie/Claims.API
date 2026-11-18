
namespace Claims.API.DTOs.Reports;

public class TransactionReportParameters
{
    public string? AssistanceCode { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }
}
