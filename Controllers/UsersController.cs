using Claims.API.Auth;
using Claims.API.DTOs.Users;
using Claims.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/users")]
[ApiController]
[Authorize(Policy = AuthPolicies.UserAdmin)]
public class UsersController(UserAdminService users, ICurrentUser currentUser) : ControllerBase
{
  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> List() =>
    Ok(await users.ListAsync());

  [HttpGet("{id:int}")]
  public async Task<ActionResult<UserSummaryDto>> Get(int id)
  {
    var user = await users.GetAsync(id);
    return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
  }

  [HttpPost]
  public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
  {
    var result = await users.CreateAsync(currentUser.Id, dto);
    if (result.Outcome == AdminOutcome.Created)
    {
      return CreatedAtAction(nameof(Get), new { id = result.User!.UserId }, result.User);
    }
    return Map(result);
  }

  [HttpPut("{id:int}")]
  public async Task<IActionResult> Update(int id, [FromBody] UpdateUserDto dto) =>
    Map(await users.UpdateAsync(currentUser.Id, id, dto));

  [HttpPut("{id:int}/roles")]
  public async Task<IActionResult> ReplaceRoles(int id, [FromBody] ReplaceRolesDto dto) =>
    Map(await users.ReplaceRolesAsync(currentUser.Id, id, dto));

  [HttpPost("{id:int}/reset-password")]
  public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordDto dto) =>
    Map(await users.ResetPasswordAsync(currentUser.Id, id, dto), noContentOnOk: true);

  [HttpPost("{id:int}/deactivate")]
  public async Task<IActionResult> Deactivate(int id) =>
    Map(await users.SetActiveAsync(currentUser.Id, id, false), noContentOnOk: true);

  [HttpPost("{id:int}/activate")]
  public async Task<IActionResult> Activate(int id) =>
    Map(await users.SetActiveAsync(currentUser.Id, id, true), noContentOnOk: true);

  private IActionResult Map(AdminResult result, bool noContentOnOk = false) => result.Outcome switch
  {
    AdminOutcome.Ok when noContentOnOk => NoContent(),
    AdminOutcome.Ok => Ok(result.User),
    AdminOutcome.NotFound => NotFound(new { message = result.Message }),
    AdminOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Message }),
    AdminOutcome.Conflict => Conflict(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };
}
