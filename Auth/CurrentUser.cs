using System.Globalization;
using System.Security.Claims;
using Claims.API.DTOs.Auth;

namespace Claims.API.Auth;

// The logged-in user for the current request. Inject it into a controller or service.
// Id, Username and Roles come from the request (already checked against the database
// on every request), so they cost nothing. GetProfileAsync loads the full record once.
public interface ICurrentUser
{
  bool IsAuthenticated { get; }
  int Id { get; }
  string Username { get; }
  IReadOnlyList<string> Roles { get; }
  bool IsInRole(string role);
  Task<CurrentUserDto?> GetProfileAsync();
}

public class CurrentUser : ICurrentUser
{
  private readonly IHttpContextAccessor _http;
  private readonly AuthService _auth;
  private Task<CurrentUserDto?>? _profile;

  public CurrentUser(IHttpContextAccessor http, AuthService auth)
  {
    _http = http;
    _auth = auth;
  }

  private ClaimsPrincipal Principal =>
    _http.HttpContext?.User ?? throw new InvalidOperationException("There is no current request.");

  public bool IsAuthenticated => _http.HttpContext?.User.Identity?.IsAuthenticated == true;

  public int Id
  {
    get
    {
      var sub = Principal.FindFirstValue("sub");
      if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        throw new InvalidOperationException("No user is logged in for this request.");
      return id;
    }
  }

  public string Username => Principal.FindFirstValue("name") ?? string.Empty;

  public IReadOnlyList<string> Roles =>
    Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

  public bool IsInRole(string role) => Principal.IsInRole(role);

  // Same data as GET /api/auth/me. Loaded on first use, reused for the rest of the request.
  public Task<CurrentUserDto?> GetProfileAsync() => _profile ??= _auth.GetCurrentUserAsync(Id);
}
