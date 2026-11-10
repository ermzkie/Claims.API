using System.Data;
using System.Text.RegularExpressions;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Users;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public enum AdminOutcome
{
  Ok,
  Created,
  NotFound,
  Forbidden,
  Invalid,
  Conflict,
}

public record AdminResult(AdminOutcome Outcome, string? Message = null, UserSummaryDto? User = null);

public partial class UserAdminService
{
  private const int MaxNameLength = 50;

  [GeneratedRegex("^[A-Za-z0-9._-]+$")]
  private static partial Regex UsernamePattern();

  private readonly ApplicationDbContext _db;
  private readonly TimeProvider _time;
  private readonly ILogger<UserAdminService> _logger;

  public UserAdminService(ApplicationDbContext db, TimeProvider time, ILogger<UserAdminService> logger)
  {
    _db = db;
    _time = time;
    _logger = logger;
  }

  public async Task<IReadOnlyList<UserSummaryDto>> ListAsync()
  {
    var users = await _db.Users.AsNoTracking()
      .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
      .OrderBy(u => u.Username)
      .ToListAsync();
    var now = Now();
    return users.Select(u => ToSummary(u, now)).ToList();
  }

  public async Task<UserSummaryDto?> GetAsync(int id)
  {
    var user = await _db.Users.AsNoTracking()
      .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
      .FirstOrDefaultAsync(u => u.UserId == id);
    return user is null ? null : ToSummary(user, Now());
  }

  public async Task<AdminResult> CreateAsync(int actorId, CreateUserDto dto)
  {
    // Granting a privileged role requires Super Administrator
    var roleIds = (dto.RoleIds ?? []).Distinct().ToList();
    var roles = await _db.Roles.Where(r => roleIds.Contains(r.RoleId)).ToListAsync();
    if (GrantsPrivilegedRole(roles) && !await IsSuperAdministratorAsync(actorId))
    {
      return Forbidden();
    }

    var username = dto.Username?.Trim() ?? string.Empty;
    if (username.Length == 0 || username.Length > MaxNameLength || !UsernamePattern().IsMatch(username))
    {
      return Invalid("Username must be 1 to 50 characters: letters, digits, '.', '_' or '-' only.");
    }
    var roleError = ValidateRoleIds(roleIds, roles);
    if (roleError is not null)
    {
      return Invalid(roleError);
    }
    var passwordError = PasswordPolicy.Validate(dto.TemporaryPassword, username);
    if (passwordError is not null)
    {
      return Invalid(passwordError);
    }
    var nameError = await ValidateNamesAsync(dto.Firstname, dto.Middlename, dto.Lastname, dto.NameSuffixId);
    if (nameError is not null)
    {
      return Invalid(nameError);
    }

    var normalized = username.ToLower();
    if (await _db.Users.AnyAsync(u => u.Username.ToLower() == normalized))
    {
      return Conflict("That username is already taken.");
    }

    var now = Now();
    var user = new User
    {
      Username = username,
      Firstname = dto.Firstname,
      Middlename = dto.Middlename,
      Lastname = dto.Lastname,
      NameSuffixId = dto.NameSuffixId,
      MustChangePassword = true,
      Status = true,
      CreatedBy = actorId,
      DateCreated = now,
    };
    user.PasswordHash = PasswordHashing.Hasher.HashPassword(user, dto.TemporaryPassword!);
    foreach (var roleId in roleIds)
    {
      user.UserRoles.Add(new UserRole
      {
        RoleId = roleId,
        Status = true,
        CreatedBy = actorId,
        DateCreated = now,
      });
    }
    _db.Users.Add(user);
    try
    {
      await _db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
    {
      // Only report "taken" if the username really exists now. Anything else is a real error.
      _db.ChangeTracker.Clear();
      if (await _db.Users.AnyAsync(u => u.Username.ToLower() == normalized))
      {
        return Conflict("That username is already taken.");
      }
      _logger.LogError(ex, "Failed to create user {Username}.", username);
      throw;
    }

    return new AdminResult(AdminOutcome.Created, null, await GetAsync(user.UserId));
  }

  public async Task<AdminResult> UpdateAsync(int actorId, int id, UpdateUserDto dto)
  {
    var target = await LoadTargetAsync(id);
    if (target is null)
    {
      return NotFound();
    }

    // Editing your own names is allowed.
    // Otherwise a non-Super Administrator cannot touch a privileged account.
    if (id != actorId && HoldsPrivilegedRole(target) && !await IsSuperAdministratorAsync(actorId))
    {
      return Forbidden();
    }

    var nameError = await ValidateNamesAsync(dto.Firstname, dto.Middlename, dto.Lastname, dto.NameSuffixId);
    if (nameError is not null)
    {
      return Invalid(nameError);
    }

    target.Firstname = dto.Firstname;
    target.Middlename = dto.Middlename;
    target.Lastname = dto.Lastname;
    target.NameSuffixId = dto.NameSuffixId;
    Touch(target, actorId, Now());
    await _db.SaveChangesAsync();

    return new AdminResult(AdminOutcome.Ok, null, await GetAsync(id));
  }

  public async Task<AdminResult> ReplaceRolesAsync(int actorId, int id, ReplaceRolesDto dto)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var target = await LoadTargetAsync(id);
    if (target is null)
    {
      return NotFound();
    }

    if (id == actorId)
    {
      return Forbidden();
    }

    var roleIds = (dto.RoleIds ?? []).Distinct().ToList();
    var roles = await _db.Roles.Where(r => roleIds.Contains(r.RoleId)).ToListAsync();
    if ((HoldsPrivilegedRole(target) || GrantsPrivilegedRole(roles)) && !await IsSuperAdministratorAsync(actorId))
    {
      return Forbidden();
    }

    var roleError = ValidateRoleIds(roleIds, roles);
    if (roleError is not null)
    {
      return Invalid(roleError);
    }

    // Never leave the system without an active Super Administrator
    var keepsSuper = roles.Any(r => r.RoleName == RoleNames.SuperAdministrator);
    if (target.Status && HoldsSuperAdministratorRole(target) && !keepsSuper
      && !await AnotherActiveSuperAdministratorExistsAsync(id))
    {
      return Conflict("The last active Super Administrator cannot lose that role.");
    }

    var now = Now();
    foreach (var row in target.UserRoles)
    {
      var wanted = roleIds.Contains(row.RoleId);
      if (row.Status != wanted)
      {
        row.Status = wanted;
        row.LastUpdatedBy = actorId;
        row.DateLastUpdated = now;
      }
    }
    foreach (var roleId in roleIds.Where(r => target.UserRoles.All(ur => ur.RoleId != r)).ToList())
    {
      target.UserRoles.Add(new UserRole
      {
        RoleId = roleId,
        Status = true,
        CreatedBy = actorId,
        DateCreated = now,
        LastUpdatedBy = actorId,
        DateLastUpdated = now,
      });
    }
    Touch(target, actorId, now);
    await _db.SaveChangesAsync();
    await tx.CommitAsync();

    return new AdminResult(AdminOutcome.Ok, null, await GetAsync(id));
  }

  public async Task<AdminResult> ResetPasswordAsync(int actorId, int id, ResetPasswordDto dto)
  {
    var target = await LoadTargetAsync(id);
    if (target is null)
    {
      return NotFound();
    }

    if (id == actorId)
    {
      return Forbidden();
    }

    if (HoldsPrivilegedRole(target) && !await IsSuperAdministratorAsync(actorId))
    {
      return Forbidden();
    }

    var passwordError = PasswordPolicy.Validate(dto.TemporaryPassword, target.Username);
    if (passwordError is not null)
    {
      return Invalid(passwordError);
    }

    target.PasswordHash = PasswordHashing.Hasher.HashPassword(target, dto.TemporaryPassword!);
    target.MustChangePassword = true;
    target.FailedLoginCount = 0;
    target.LockoutEnd = null;
    Touch(target, actorId, Now());
    await _db.SaveChangesAsync();

    return new AdminResult(AdminOutcome.Ok);
  }

  public async Task<AdminResult> SetActiveAsync(int actorId, int id, bool active)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var target = await LoadTargetAsync(id);
    if (target is null)
    {
      return NotFound();
    }

    if (id == actorId)
    {
      return Forbidden();
    }

    if (HoldsPrivilegedRole(target) && !await IsSuperAdministratorAsync(actorId))
    {
      return Forbidden();
    }

    // Never leave the system without an active Super Administrator
    if (!active && target.Status && HoldsSuperAdministratorRole(target)
      && !await AnotherActiveSuperAdministratorExistsAsync(id))
    {
      return Conflict("The last active Super Administrator cannot be deactivated.");
    }

    target.Status = active;
    Touch(target, actorId, Now());
    await _db.SaveChangesAsync();
    await tx.CommitAsync();

    return new AdminResult(AdminOutcome.Ok);
  }

  private DateTime Now() => _time.GetLocalNow().DateTime;

  private static void Touch(User user, int actorId, DateTime now)
  {
    user.LastUpdatedBy = actorId;
    user.DateLastUpdated = now;
  }

  private Task<User?> LoadTargetAsync(int id) =>
    _db.Users
      .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
      .FirstOrDefaultAsync(u => u.UserId == id);

  private Task<bool> IsSuperAdministratorAsync(int actorId) =>
    _db.UserRoles.AnyAsync(ur => ur.UserId == actorId && ur.Status
      && ur.Role != null && ur.Role.RoleName == RoleNames.SuperAdministrator);

  private Task<bool> AnotherActiveSuperAdministratorExistsAsync(int excludingUserId) =>
    _db.UserRoles.AnyAsync(ur => ur.UserId != excludingUserId && ur.Status
      && ur.User!.Status
      && ur.Role != null && ur.Role.RoleName == RoleNames.SuperAdministrator);

  private static bool GrantsPrivilegedRole(IEnumerable<Role> roles) =>
    roles.Any(r => r.RoleName is { } n && RoleNames.Privileged.Contains(n));

  private static bool HoldsPrivilegedRole(User user) =>
    user.UserRoles.Any(ur => ur.Status && ur.Role?.RoleName is { } n && RoleNames.Privileged.Contains(n));

  private static bool HoldsSuperAdministratorRole(User user) =>
    user.UserRoles.Any(ur => ur.Status && ur.Role?.RoleName == RoleNames.SuperAdministrator);

  private static string? ValidateRoleIds(List<short> roleIds, List<Role> found)
  {
    if (roleIds.Count == 0)
    {
      return "At least one role is required.";
    }
    if (found.Count != roleIds.Count)
    {
      return "One or more roles do not exist.";
    }
    return null;
  }

  private async Task<string?> ValidateNamesAsync(string? first, string? middle, string? last, short? suffixId)
  {
    if (first?.Length > MaxNameLength || middle?.Length > MaxNameLength || last?.Length > MaxNameLength)
    {
      return "Names must be at most 50 characters.";
    }
    if (suffixId is not null && !await _db.NameSuffixes.AnyAsync(s => s.NameSuffixId == suffixId))
    {
      return "Name suffix does not exist.";
    }
    return null;
  }

  private static UserSummaryDto ToSummary(User u, DateTime now) => new()
  {
    UserId = u.UserId,
    Username = u.Username,
    Firstname = u.Firstname,
    Middlename = u.Middlename,
    Lastname = u.Lastname,
    NameSuffixId = u.NameSuffixId,
    Roles = u.UserRoles
      .Where(ur => ur.Status)
      .Select(ur => ur.Role?.RoleName)
      .OfType<string>()
      .OrderBy(n => n)
      .ToList(),
    Status = u.Status,
    MustChangePassword = u.MustChangePassword,
    LockedUntil = u.LockoutEnd > now ? u.LockoutEnd : null,
    LastLoginDate = u.LastLoginDate,
  };

  private static AdminResult NotFound() => new(AdminOutcome.NotFound, "User not found.");
  private static AdminResult Forbidden() => new(AdminOutcome.Forbidden, "You are not allowed to perform this action.");
  private static AdminResult Invalid(string message) => new(AdminOutcome.Invalid, message);
  private static AdminResult Conflict(string message) => new(AdminOutcome.Conflict, message);
}
