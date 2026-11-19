namespace Claims.API.DTOs.Transactions;

public record CheckoutRequest(bool TakeOver = false, string? Reason = null, int? DeskId = null);

public record ReasonRequest(string? Reason);

public record TransactionHistoryDto(
  int Id, string Action, string? OldValues, string? NewValues, string? Remark,
  int UserId, string? UserName, DateTime DateCreated);
