namespace Claims.API.Auth;

public static class RoleNames
{
  public const string SuperAdministrator = "Super Administrator";
  public const string Administrator = "Administrator";
  public const string Frontliner = "Frontliner";
  public const string SocialWorker = "Social Worker";
  public const string Approver = "Approver";
  public const string Display = "Display";

  public static readonly IReadOnlySet<string> Privileged =
    new HashSet<string>(StringComparer.Ordinal) { SuperAdministrator, Administrator };

  public const string UserAdmins = SuperAdministrator + "," + Administrator;
}
