using Microsoft.Extensions.Caching.Memory;

namespace Claims.API.Auth;

// Failed-login counter for usernames that don't exist, so they get the same countdown and lockout
// as real accounts and the login response never reveals whether a username is real.
// Kept in memory with a size cap: random usernames can't grow it without limit.
public sealed class UnknownUsernameAttempts : IDisposable
{
  private const int MaxTrackedUsernames = 10_000;

  private sealed class Entry
  {
    public int Count;
    public DateTime? LockoutEnd;
  }

  private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxTrackedUsernames });

  public DateTime? LockoutEnd(string normalizedUsername) =>
    _cache.TryGetValue(normalizedUsername, out Entry? entry) ? entry!.LockoutEnd : null;

  public FailedAttempt RecordFailure(string normalizedUsername, DateTime now)
  {
    var entry = _cache.GetOrCreate(normalizedUsername, e =>
    {
      e.Size = 1;
      e.SlidingExpiration = TimeSpan.FromHours(1);
      return new Entry();
    })!;

    lock (entry)
    {
      entry.Count++;
      if (entry.Count >= AuthService.MaxFailedAttempts)
      {
        entry.Count = 0;
        entry.LockoutEnd = now + AuthService.LockoutDuration;
      }
      return new FailedAttempt(entry.Count, entry.LockoutEnd > now ? entry.LockoutEnd : null);
    }
  }

  public void Dispose() => _cache.Dispose();
}
