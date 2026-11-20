namespace Claims.API.DTOs.Reports;

public class PerennialClientReportDto
{
    public int PatientPersonId { get; set; }

    public string? PatientName { get; set; }

    public string? PatientAddress { get; set; }

    public string? AssistanceType { get; set; }

    public int AvailmentCount { get; set; }

    public decimal TotalAmount { get; set; }
}