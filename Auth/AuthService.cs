using Claims.API.Data;
using Claims.API.DTOs.Auth;
using Claims.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Auth;

public enum LoginOutcome
{
  Success,
  InvalidRequest,
  InvalidCredentials,
  LockedOut,
}

// AttemptsLeft: wrong tries left before lockout (InvalidCredentials). RetryAfter: time until unlock (LockedOut).
public record LoginResult(
  LoginOutcome Outcome, LoginResponseDto? Response, int AttemptsLeft = 0, TimeSpan RetryAfter = default);

public record FailedAttempt(int FailedCount, DateTime? LockoutEnd);

public enum ChangePasswordOutcome
{
  Success,
  LockedOut,
  WrongCurrentPassword,
  PolicyViolation,
}

public record ChangePasswordResult(ChangePasswordOutcome Outcome, string? Message);

public class AuthService
{
  public const int MaxFailedAttempts = 10;
  public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

  private readonly ApplicationDbContext _db;
  private readonly TokenService _tokens;
  private readonly TimeProvider _time;
  private readonly UnknownUsernameAttempts _unknownAttempts;

  public AuthService(ApplicationDbContext db, TokenService tokens, TimeProvider time, UnknownUsernameAttempts unknownAttempts)
  {
    _db = db;
    _tokens = tokens;
    _time = time;
    _unknownAttempts = unknownAttempts;
  }

  public async Task<LoginResult> LoginAsync(string? username, string? password)
  {
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
      return new LoginResult(LoginOutcome.InvalidRequest, null);
    }

    // Case-insensitive lookup on the trimmed name
    var normalized = username.Trim().ToLowerInvariant();
    var user = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized);

    var now = _time.GetLocalNow().DateTime;

    // Unknown user: same hashing time, same countdown and lockout as a real account
    if (user is null)
    {
      if (_unknownAttempts.LockoutEnd(normalized) is { } unknownLockoutEnd && unknownLockoutEnd > now)
      {
        return Locked(unknownLockoutEnd, now);
      }
      PasswordHashing.Hasher.VerifyHashedPassword(new User(), PasswordHashing.DummyHash, password);
      return Failed(_unknownAttempts.RecordFailure(normalized, now), now);
    }

    // Locked out: no verification, no counter change
    if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
    {
      return Locked(lockoutEnd, now);
    }

    var verify = PasswordHashing.Hasher.VerifyHashedPassword(user, user.PasswordHash, password);

    // Deactivated counts as a failure even with the right password, so it looks like a wrong password
    if (verify == PasswordVerificationResult.Failed || !user.Status)
    {
      return Failed(await RecordFailedAttemptAsync(user.UserId, now), now);
    }

    user.FailedLoginCount = 0;
    user.LockoutEnd = null;
    user.LastLoginDate = now;
    if (verify == PasswordVerificationResult.SuccessRehashNeeded)
    {
      user.PasswordHash = PasswordHashing.Hasher.HashPassword(user, password);
    }
    await _db.SaveChangesAsync();

    var (token, expiresAt) = _tokens.CreateToken(user.UserId);
    var current = (await GetCurrentUserAsync(user.UserId))!;
    return new LoginResult(LoginOutcome.Success, new LoginResponseDto
    {
      Token = token,
      ExpiresAt = expiresAt,
      MustChangePassword = current.MustChangePassword,
      User = current,
    });
  }

  public async Task<ChangePasswordResult> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword)
  {
    var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
    if (user is null)
    {
      return new ChangePasswordResult(ChangePasswordOutcome.WrongCurrentPassword, null);
    }

    var now = _time.GetLocalNow().DateTime;

    if (user.LockoutEnd > now)
    {
      return new ChangePasswordResult(ChangePasswordOutcome.LockedOut, null);
    }

    var verified = !string.IsNullOrEmpty(currentPassword)
      && PasswordHashing.Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword)
        != PasswordVerificationResult.Failed;
    if (!verified)
    {
      await RecordFailedAttemptAsync(user.UserId, now);
      return new ChangePasswordResult(ChangePasswordOutcome.WrongCurrentPassword, null);
    }

    var violation = PasswordPolicy.Validate(newPassword, user.Username, currentPassword);
    if (violation is not null)
    {
      return new ChangePasswordResult(ChangePasswordOutcome.PolicyViolation, violation);
    }

    user.PasswordHash = PasswordHashing.Hasher.HashPassword(user, newPassword!);
    user.MustChangePassword = false;
    user.FailedLoginCount = 0;
    user.LockoutEnd = null;
    user.LastUpdatedBy = userId;
    user.DateLastUpdated = now;
    await _db.SaveChangesAsync();

    return new ChangePasswordResult(ChangePasswordOutcome.Success, null);
  }

  // Set-based so concurrent wrong guesses cannot read the same counter value. The tracked entity is
  // not modified on the failure path, so a later SaveChanges cannot overwrite these columns.
  private async Task<FailedAttempt> RecordFailedAttemptAsync(int userId, DateTime now)
  {
    await _db.Users.Where(u => u.UserId == userId)
      .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1));

    DateTime? lockoutEnd = now + LockoutDuration;
    await _db.Users.Where(u => u.UserId == userId && u.FailedLoginCount >= MaxFailedAttempts)
      .ExecuteUpdateAsync(s => s
        .SetProperty(u => u.LockoutEnd, lockoutEnd)
        .SetProperty(u => u.FailedLoginCount, 0));

    var state = await _db.Users.AsNoTracking()
      .Where(u => u.UserId == userId)
      .Select(u => new { u.FailedLoginCount, u.LockoutEnd })
      .SingleAsync();
    return new FailedAttempt(state.FailedLoginCount, state.LockoutEnd > now ? state.LockoutEnd : null);
  }

  private static LoginResult Locked(DateTime lockoutEnd, DateTime now) =>
    new(LoginOutcome.LockedOut, null, RetryAfter: lockoutEnd - now);

  private static LoginResult Failed(FailedAttempt attempt, DateTime now) =>
    attempt.LockoutEnd is { } lockoutEnd
      ? Locked(lockoutEnd, now)
      : new(LoginOutcome.InvalidCredentials, null, AttemptsLeft: MaxFailedAttempts - attempt.FailedCount);

  public async Task<CurrentUserDto?> GetCurrentUserAsync(int userId)
  {
    var user = await _db.Users
      .AsNoTracking()
      .Where(u => u.UserId == userId)
      .Select(u => new
      {
        u.UserId,
        u.Username,
        u.Firstname,
        u.Middlename,
        u.Lastname,
        u.MustChangePassword,
        Roles = (from ur in u.UserRoles
            join r in _db.Roles on ur.RoleId equals r.RoleId
            where ur.Status && r.RoleName != null
            select r.RoleName).ToList(),
      })
      .FirstOrDefaultAsync();

    if (user is null)
    {
      return null;
    }

    return new CurrentUserDto
    {
      UserId = user.UserId,
      Username = user.Username,
      Firstname = user.Firstname,
      Middlename = user.Middlename,
      Lastname = user.Lastname,
      MustChangePassword = user.MustChangePassword,
      Roles = user.Roles.OfType<string>().ToList(),
    };
  }
}
