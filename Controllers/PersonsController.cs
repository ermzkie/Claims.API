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
  public async Task<ActionResult<PagedResult<PersonListItemDto>>> Search(
    string? search, int page = 1, int pageSize = 20) =>
    await persons.SearchAsync(search, page, pageSize);

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

  private ActionResult Map(PersonResult result) => result.Outcome switch
  {
    PersonOutcome.NotFound => NotFound(new { message = result.Message }),
    _ => BadRequest(new { message = result.Message }),
  };
}
