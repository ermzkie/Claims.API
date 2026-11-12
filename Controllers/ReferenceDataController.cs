using Claims.API.Auth;
using Claims.API.DTOs.ReferenceData;
using Claims.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

// Admin maintenance of the reference data (small lookup lists). The dropdowns read it through /api/lookups.
[Route("api/reference-data")]
[ApiController]
[Authorize(Policy = AuthPolicies.UserAdmin)]
public class ReferenceDataController(ReferenceDataService lists) : ControllerBase
{
  [HttpGet]
  public IReadOnlyList<ListInfoDto> Index() => lists.Index();

  [HttpGet("{key}")]
  public async Task<ActionResult<List<ListItemDto>>> Get(string key) =>
    await lists.AllAsync(key) is { } items ? items : NotFound(new { message = "List not found." });

  [HttpPost("{key}")]
  public async Task<ActionResult<ListItemDto>> Add(string key, [FromBody] SaveListItemDto dto)
  {
    var result = await lists.AddAsync(key, dto);
    return result.Outcome == ListOutcome.Ok
      ? CreatedAtAction(nameof(Get), new { key }, result.Item)
      : Map(result);
  }

  [HttpPut("{key}/{id:int}")]
  public async Task<ActionResult<ListItemDto>> Update(string key, int id, [FromBody] SaveListItemDto dto)
  {
    var result = await lists.UpdateAsync(key, id, dto);
    return result.Outcome == ListOutcome.Ok ? result.Item! : Map(result);
  }

  [HttpDelete("{key}/{id:int}")]
  public async Task<IActionResult> Delete(string key, int id)
  {
    var result = await lists.DeleteAsync(key, id);
    return result.Outcome == ListOutcome.Ok ? NoContent() : Map(result);
  }

  private ActionResult Map(ListResult result) => result.Outcome switch
  {
    ListOutcome.NotFound => NotFound(new { message = result.Message }),
    ListOutcome.Conflict => Conflict(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };
}
