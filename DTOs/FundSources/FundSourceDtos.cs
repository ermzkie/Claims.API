using Claims.API.Services;

namespace Claims.API.DTOs.FundSources;

// money figures are null when finance can't be reached
public record FundSourceDto(
  int FundSourceId,
  short Year,
  short? SourceId,
  string? Program,
  string Kind,
  int? FinanceId,
  FinanceAccountDto? Finance,
  decimal? Released,
  decimal? Obligated,
  decimal? Pending,
  decimal? Original,
  decimal? Supplemental,
  decimal? Adjustments,
  int Claims,
  decimal ClaimsAmount,
  DateTime AddedOn,
  string? AddedBy,
  DateTime? ChangedOn,
  string? ChangedBy);

public record FundSourceListDto(int Year, IReadOnlyList<int> Years, bool FinanceAvailable, IReadOnlyList<FundSourceDto> Items);

public record FundSourceClaimDto(
  int TransactionId,
  string? RefNo,
  DateTime? Date,
  int? PatientPersonId,
  string? Patient,
  string? AssistanceCode,
  string? Program,
  int? CreditorId,
  string? CreditorName,
  decimal? Amount,
  bool? IsReleased);

// give either AppropriationId or TrustFundId
public class SaveFundSourceDto
{
  public int? Year { get; set; }
  public short? SourceId { get; set; }
  public int? AppropriationId { get; set; }
  public int? TrustFundId { get; set; }
}
