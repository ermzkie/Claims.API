using Claims.API.Auth;
using Claims.API.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        var result = await _auth.LoginAsync(request.Username, request.Password);

        return result.Outcome switch
        {
            LoginOutcome.Success => Ok(result.Response),
            LoginOutcome.InvalidRequest => BadRequest(new { message = "Username and password are required." }),
            LoginOutcome.LockedOut => Unauthorized(new { message = "Too many failed attempts. Try again later.", code = "locked_out" }),
            _ => Unauthorized(new { message = "Invalid username or password." }),
        };
    }
}
