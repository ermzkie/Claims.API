
public class TransactionQueryParameters
{
  // Pagination
  public int Page { get; set; } = 1;

  // Date filtering
  public DateTime? StartDate { get; set; }
  public DateTime? EndDate { get; set; }

  // Search
  public string? Search { get; set; }

  // Status filtering
  public string? Status { get; set; }

  // Sorting
  public string SortBy { get; set; } = "Date";
  public string SortDirection { get; set; } = "desc";
}