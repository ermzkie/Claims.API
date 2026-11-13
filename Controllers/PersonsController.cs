using Claims.API.Auth;
using Claims.API.DTOs.Common;
using Claims.API.DTOs.Persons;
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

  [HttpPost("{id:int}/family/add")]
  public async Task<ActionResult<FamilyMemberDto>> AddFamilyMember(int id, [FromBody] SaveFamilyMemberDto dto)
  {
    var result = await persons.AddFamilyMemberAsync(currentUser.Id, id, dto);
    return result.Outcome == PersonOutcome.Ok
      ? CreatedAtAction(nameof(Get), new { id }, result.FamilyMember)
      : Map(result);
  }

  [HttpPut("{id:int}/family/{familyCompId:int}/update")]
  public async Task<ActionResult<FamilyMemberDto>> UpdateFamilyMember(
    int id, int familyCompId, [FromBody] SaveFamilyMemberDto dto)
  {
    var result = await persons.UpdateFamilyMemberAsync(currentUser.Id, id, familyCompId, dto);
    return result.Outcome == PersonOutcome.Ok ? result.FamilyMember! : Map(result);
  }

  private ActionResult Map(PersonResult result) => result.Outcome switch
  {
    PersonOutcome.NotFound => NotFound(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };

  private ActionResult Map(FamilyMemberResult result) => result.Outcome switch
  {
    PersonOutcome.NotFound => NotFound(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };
}
