using Microsoft.AspNetCore.Authorization;

namespace Claims.API.Auth;

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
