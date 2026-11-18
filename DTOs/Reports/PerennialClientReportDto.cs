
namespace Claims.API.DTOs.Reports;

public class PerennialClientReportDto
{
    public int PatientPersonId { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public string PatientAddress { get; set; } = string.Empty;

    public string AssistanceCode { get; set; } = string.Empty;

    public int AvailmentCount { get; set; }

    public decimal TotalAmount { get; set; }
}
