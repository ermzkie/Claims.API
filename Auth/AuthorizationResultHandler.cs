using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Claims.API.Auth;

public class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var pendingChange = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure is { } failure
            && failure.FailedRequirements.OfType<PasswordChangeRequirement>().Any();

        if (pendingChange)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "You must change your password before continuing.",
                code = "password_change_required",
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
