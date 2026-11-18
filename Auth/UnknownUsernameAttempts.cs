using Microsoft.Extensions.Caching.Memory;

namespace Claims.API.Auth;

// fake lockout for unknown usernames so login never reveals which names are real.
// no expiry (waiting would give it away); capped size, reset on restart
public sealed class UnknownUsernameAttempts : IDisposable
{
  private const int MaxTrackedUsernames = 100_000;

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
