using Microsoft.AspNetCore.Authorization;

namespace Claims.API.Auth;

/// <summary>
/// Fails for users who still must change their password. Part of the shared base policy,
/// so every endpoint except those marked AllowPendingPasswordChange is gated by it.
/// </summary>
public class PasswordChangeRequirement : IAuthorizationRequirement
{
}

public class PasswordChangeHandler : AuthorizationHandler<PasswordChangeRequirement>
{
  protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangeRequirement requirement)
  {
    if (context.User.FindFirst("must_change_password")?.Value != "true")
    {
      context.Succeed(requirement);
    }

    return Task.CompletedTask;
  }
}
