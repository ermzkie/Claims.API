using Claims.API.Models;

namespace Claims.API.Tests.Infrastructure;

public static class TestMoney
{
  // amount, creditor, mode and fund source live on an allocation under the claim's service now
  public static Transaction WithMoney(
    this Transaction transaction,
    decimal? amount = null,
    int? creditorId = null,
    short? modeId = null,
    int? fundSourceId = null)
  {
    var service = transaction.TransactionServices.FirstOrDefault();
    if (service is null)
    {
      service = new TransactionService();
      transaction.TransactionServices.Add(service);
    }
    service.ServiceFundAllocations.Add(new ServiceFundAllocation
    {
      Status = true,
      Amount = amount,
      CreditorId = creditorId,
      ModeId = modeId,
      FundSourceId = fundSourceId,
    });
    return transaction;
  }

  // creditor totals count approved and released claims; the old claims came over that way
  public static Transaction Released(this Transaction transaction)
  {
    transaction.ApprovalStatusId = 3;
    transaction.IsReleased = true;
    return transaction;
  }
}
