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

public record LoginResult(LoginOutcome Outcome, LoginResponseDto? Response);

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
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;
    private readonly TokenService _tokens;
    private readonly TimeProvider _time;

    public AuthService(ApplicationDbContext db, TokenService tokens, TimeProvider time)
    {
        _db = db;
        _tokens = tokens;
        _time = time;
    }

    public async Task<LoginResult> LoginAsync(string? username, string? password)
    {
        // 1. Blank input
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginResult(LoginOutcome.InvalidRequest, null);
        }

        // 2. Case-insensitive lookup on the trimmed name
        var normalized = username.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized);

        // 3. Unknown user: spend the same hashing time, then fail generically
        if (user is null)
        {
            PasswordHashing.Hasher.VerifyHashedPassword(new User(), PasswordHashing.DummyHash, password);
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        var now = _time.GetLocalNow().DateTime;

        // 4. Locked out: no verification, no counter change
        if (user.LockoutEnd > now)
        {
            return new LoginResult(LoginOutcome.LockedOut, null);
        }

        // 5. Verify
        var verify = PasswordHashing.Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            RecordFailedAttempt(user, now);
            await _db.SaveChangesAsync();
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        // 6. Correct password but deactivated: indistinguishable from a wrong password
        if (!user.Status)
        {
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        // 7. Success
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
            RecordFailedAttempt(user, now);
            await _db.SaveChangesAsync();
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

    private static void RecordFailedAttempt(User user, DateTime now)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= MaxFailedAttempts)
        {
            user.LockoutEnd = now + LockoutDuration;
            user.FailedLoginCount = 0;
        }
    }

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
