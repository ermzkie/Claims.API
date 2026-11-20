using System;

namespace Claims.API.DTOs.Reports;

public class PerennialClientReportParameters
{
    public short? SourceId { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }
}