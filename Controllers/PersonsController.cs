using Claims.API.Auth;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Persons;
using Claims.API.DTOs.Transactions;
using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Claims.API.Controllers;

[Route("api/persons")]
[ApiController]
public class PersonsController(PersonService persons, ICurrentUser currentUser) : ControllerBase
{
  [HttpGet]
  public async Task<ActionResult<PagedResult<PersonListItemDto>>> Search([FromQuery] PersonSearchDto filter) =>
    await persons.SearchAsync(filter) is { } result
      ? result
      : BadRequest(new { message = $"Sector must be one of: {string.Join(", ", PersonService.Sectors)}." });

  [HttpGet("{id:int}")]
  public async Task<ActionResult<PersonDto>> Get(int id)
  {
    var person = await persons.GetAsync(id);
    return person is null ? NotFound(new { message = "Person not found." }) : person;
  }

  // persons who may be the one being typed in; "same" ones block saving until confirmed
  [HttpGet("matches")]
  public async Task<ActionResult<List<PersonMatchDto>>> Matches([FromQuery] PersonMatchQueryDto query) =>
    await persons.FindMatchesAsync(query);

  [HttpGet("{id:int}/transactions")]
  public async Task<ActionResult<PagedResult<TransactionResponseDto>>> GetTransactionHistory(
    int id,
    [FromQuery] int page = 1,
    CancellationToken cancellationToken = default)
  {
    if (page < 1)
      return BadRequest("Page must be greater than 0.");

    var history = await persons.GetTransactionHistoryAsync(id, page, cancellationToken);
    return history is null
      ? NotFound(new { message = "Person not found." })
      : Ok(history);
  }

  [HttpPost]
  public async Task<ActionResult<PersonDto>> Create([FromBody] SavePersonDto dto)
  {
    var result = await persons.CreateAsync(currentUser.Id, dto);
    return result.Outcome == PersonOutcome.Ok
      ? CreatedAtAction(nameof(Get), new { id = result.Person!.PersonId }, result.Person)
      : Map(result);
  }

  [HttpPut("{id:int}")]
  public async Task<ActionResult<PersonDto>> Update(int id, [FromBody] SavePersonDto dto)
  {
    var result = await persons.UpdateAsync(currentUser.Id, id, dto);
    return result.Outcome == PersonOutcome.Ok ? result.Person! : Map(result);
  }

  [HttpPost("{id:int}/family")]
  [HttpPost("{id:int}/family/add")]
  public async Task<ActionResult<FamilyMemberDto>> AddFamilyMember(int id, [FromBody] SaveFamilyMemberDto dto)
  {
    var result = await persons.AddFamilyMemberAsync(currentUser.Id, id, dto);
    return result.Outcome == PersonOutcome.Ok
      ? CreatedAtAction(nameof(Get), new { id }, result.FamilyMember)
      : Map(result);
  }

  [HttpPut("{id:int}/family/{familyCompId:int}")]
  [HttpPut("{id:int}/family/{familyCompId:int}/update")]
  public async Task<ActionResult<FamilyMemberDto>> UpdateFamilyMember(
    int id, int familyCompId, [FromBody] SaveFamilyMemberDto dto)
  {
    var result = await persons.UpdateFamilyMemberAsync(currentUser.Id, id, familyCompId, dto);
    return result.Outcome == PersonOutcome.Ok ? result.FamilyMember! : Map(result);
  }

  [HttpDelete("{id:int}/family/{familyCompId:int}")]
  public async Task<IActionResult> RemoveFamilyMember(
    int id, int familyCompId, [FromBody] RemoveFamilyMemberDto? dto = null)
  {
    var result = await persons.RemoveFamilyMemberAsync(currentUser.Id, id, familyCompId, dto?.Remark);
    return result.Outcome == PersonOutcome.Ok ? NoContent() : Map(result);
  }

  // family change log, newest first
  [HttpGet("{id:int}/family/history")]
  public async Task<ActionResult<List<FamilyChangeDto>>> FamilyHistory(int id) =>
    await persons.FamilyHistoryAsync(id) is { } history
      ? history
      : NotFound(new { message = "Person not found." });

  private ActionResult Map(PersonResult result) => result.Outcome switch
  {
    PersonOutcome.NotFound => NotFound(new { message = result.Message }),
    PersonOutcome.Duplicate => Conflict(new { message = result.Message, matches = result.Matches }),
    _ => BadRequest(new { message = result.Message }),
  };

  private ActionResult Map(FamilyMemberResult result) => result.Outcome switch
  {
    PersonOutcome.NotFound => NotFound(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };
}
