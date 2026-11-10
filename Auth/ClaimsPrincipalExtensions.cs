using System.Globalization;
using System.Security.Claims;

namespace Claims.API.Auth;

public static class ClaimsPrincipalExtensions
{
  public static int GetUserId(this ClaimsPrincipal user)
  {
    var sub = user.FindFirstValue("sub");
    return int.Parse(sub!, CultureInfo.InvariantCulture);
  }
}
