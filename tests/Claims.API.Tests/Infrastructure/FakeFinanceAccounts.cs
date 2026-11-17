using Claims.API.Services;

namespace Claims.API.Tests.Infrastructure;

// tests can't reach sysPFM
public class FakeFinanceAccounts : IFinanceAccounts
{
  public bool Down { get; set; }
  public List<(int Year, FinanceAccountDto Account)> Appropriations { get; } = [];
  public List<FinanceAccountDto> TrustFunds { get; } = [];

  public Task<IReadOnlyList<FinanceAccountDto>> AppropriationsAsync(int year) =>
    Answer(Appropriations.Where(a => a.Year == year).Select(a => a.Account).ToList());

  public Task<IReadOnlyList<FinanceAccountDto>> TrustFundsAsync() => Answer(TrustFunds.ToList());

  public Dictionary<int, FinanceUsageDto> Usage { get; } = [];

  public Task<IReadOnlyList<FinanceUsageDto>> AppropriationUsageAsync(IReadOnlyCollection<int> ids) => UsageOf(ids);

  public Task<IReadOnlyList<FinanceUsageDto>> TrustFundUsageAsync(IReadOnlyCollection<int> ids) => UsageOf(ids);

  private Task<IReadOnlyList<FinanceUsageDto>> UsageOf(IReadOnlyCollection<int> ids) =>
    Answer(ids.Select(id => Usage.GetValueOrDefault(id) ?? new FinanceUsageDto(id, null, null)).ToList());

  private Task<IReadOnlyList<T>> Answer<T>(IReadOnlyList<T> rows) =>
    Down
      ? Task.FromException<IReadOnlyList<T>>(new FinanceUnavailableException(new TimeoutException()))
      : Task.FromResult(rows);
}
