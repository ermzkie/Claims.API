using Claims.API.Auth;
using Claims.API.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController(AuthService auth, ICurrentUser currentUser) : ControllerBase
{
  [HttpPost("login")]
  [AllowAnonymous]
  public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
  {
    var result = await auth.LoginAsync(request.Username, request.Password);

    return result.Outcome switch
    {
      LoginOutcome.Success => Ok(result.Response),
      LoginOutcome.InvalidRequest => BadRequest(new { message = "Username and password are required." }),
      LoginOutcome.LockedOut => Unauthorized(new { message = LockedMessage(result.RetryAfter), code = "locked_out" }),
      _ => Unauthorized(new { message = InvalidCredentialsMessage(result.AttemptsLeft) }),
    };
  }

  private const int WarnWhenAttemptsLeft = 3;

  private static string InvalidCredentialsMessage(int attemptsLeft)
  {
    if (attemptsLeft > WarnWhenAttemptsLeft)
      return "Invalid username or password.";

    var minutes = Minutes(AuthService.LockoutDuration);
    return $"Invalid username or password. {Plural(attemptsLeft, "attempt")} left before the account is locked for {Plural(minutes, "minute")}.";
  }

  private static string LockedMessage(TimeSpan retryAfter) =>
    $"Too many failed attempts. Try again in {Plural(Minutes(retryAfter), "minute")}.";

  private static int Minutes(TimeSpan t) => Math.Max(1, (int)Math.Ceiling(t.TotalMinutes));

  private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

  [HttpGet("me")]
  [Authorize(Policy = AuthPolicies.AllowPendingPasswordChange)]
  public async Task<ActionResult<CurrentUserDto>> Me()
  {
    var current = await currentUser.GetProfileAsync();
    return current is null ? Unauthorized() : Ok(current);
  }

  [HttpPost("change-password")]
  [Authorize(Policy = AuthPolicies.AllowPendingPasswordChange)]
  public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
  {
    var result = await auth.ChangePasswordAsync(currentUser.Id, request.CurrentPassword, request.NewPassword);

    return result.Outcome switch
    {
      ChangePasswordOutcome.Success => NoContent(),
      ChangePasswordOutcome.LockedOut => BadRequest(new { message = "Too many failed attempts. Try again later.", code = "locked_out" }),
      ChangePasswordOutcome.WrongCurrentPassword => BadRequest(new { message = "Current password is incorrect." }),
      _ => BadRequest(new { message = result.Message }),
    };
  }
}
