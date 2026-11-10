namespace Claims.API.Auth;

public static class PasswordPolicy
{
  public const int MinLength = 8;
  public const int MaxLength = 128;

  /// <summary>
  /// Returns a user-facing error message, or null when the password is acceptable.
  /// </summary>
  public static string? Validate(string? password, string username, string? currentPassword = null)
  {
    if (string.IsNullOrEmpty(password) || password.Length < MinLength)
    {
      return $"Password must be at least {MinLength} characters.";
    }

    if (string.IsNullOrWhiteSpace(password))
    {
      return "Password must not be only spaces.";
    }

    if (password.Length > MaxLength)
    {
      return $"Password must be at most {MaxLength} characters.";
    }

    if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
    {
      return "Password must not be the same as your username.";
    }

    if (currentPassword is not null && string.Equals(password, currentPassword, StringComparison.Ordinal))
    {
      return "New password must be different from your current password.";
    }

    return null;
  }
}
