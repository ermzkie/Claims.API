
public class TransactionQueryParameters
{
  // Pagination
  public int Page { get; set; } = 1;
  public int PageSize { get; set; } = 10;

  // Date filtering
  public DateTime? StartDate { get; set; }
  public DateTime? EndDate { get; set; }

  // Search
  public string? Search { get; set; }

  // active (default), cancelled or all; true/false also work
  public string? Status { get; set; }
  public int? SocialWorkerId { get; set; }
  public string? AssistanceCode { get; set; }

  // Sorting
  public string SortBy { get; set; } = "Date";
  public string SortDirection { get; set; } = "desc";
}