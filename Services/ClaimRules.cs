using System.Linq.Expressions;
using Claims.API.Models;

namespace Claims.API.Services;

public static class ClaimRules
{
  // old-system claims with Status 0 still count; a cancel here always sets LastUpdatedBy
  public static readonly Expression<Func<Transaction, bool>> Counted =
    t => t.Status || (t.CreatedBy == null && t.LastUpdatedBy == null);

  public static bool IsCancelled(Transaction t) =>
    !t.Status && (t.CreatedBy != null || t.LastUpdatedBy != null);
}
