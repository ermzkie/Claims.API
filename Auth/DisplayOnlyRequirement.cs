using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Claims.API.Auth;

// a TV account (Display and nothing else) may only read the now-serving feed
public class DisplayOnlyRequirement : IAuthorizationRequirement
{
  public static bool IsDisplayOnly(IEnumerable<string> roles)
  {
    var list = roles.ToList();
    return list.Count > 0 && list.All(r => r == RoleNames.Display);
  }
}

public class DisplayOnlyHandler : AuthorizationHandler<DisplayOnlyRequirement>
{
  protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DisplayOnlyRequirement requirement)
  {
    if (!DisplayOnlyRequirement.IsDisplayOnly(context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)))
      context.Succeed(requirement);
    return Task.CompletedTask;
  }
}
