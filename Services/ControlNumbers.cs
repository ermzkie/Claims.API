using Claims.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

// the office's RefNo format: {program}-{MM}-{DD}-{YY}-{n}, n running per program per day
public static class ControlNumbers
{
  // raised when the lock isn't granted; sp_getapplock only returns a code, so without this we'd go on unlocked
  public const int LockNotGranted = 51000;

  public static string Prefix(string code, DateTime date) =>
    $"{code.Trim().ToUpperInvariant()}-{date:MM}-{date:dd}-{date:yy}-";

  // approvals take turns at the number so two can't read the same highest one; needs an open transaction
  public static async Task LockAsync(ApplicationDbContext db, CancellationToken ct)
  {
    if (!db.Database.IsSqlServer())
      return;
    await db.Database.ExecuteSqlRawAsync(
      "DECLARE @granted int; " +
      "EXEC @granted = sp_getapplock @Resource = 'ClaimControlNo', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; " +
      "IF @granted < 0 THROW 51000, 'Control number lock not granted.', 1;",
      ct);
  }

  public static async Task<string> NextAsync(ApplicationDbContext db, string code, DateTime date, CancellationToken ct)
  {
    var prefix = Prefix(code, date);
    var taken = await db.Transactions
      .Where(t => t.RefNo != null && t.RefNo.StartsWith(prefix))
      .Select(t => t.RefNo!)
      .ToListAsync(ct);
    // old numbers were typed by hand: skip tails that aren't numbers
    var highest = taken
      .Select(r => int.TryParse(r.AsSpan(prefix.Length), out var n) ? n : 0)
      .DefaultIfEmpty(0)
      .Max();
    return prefix + (highest + 1);
  }
}
