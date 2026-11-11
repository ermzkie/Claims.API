using Claims.API.Data;
using Claims.API.DTOs.Lookups;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Route("api/lookups")]
[ApiController]
public class LookupsController(ApplicationDbContext context) : ControllerBase
{
  [HttpGet("sexes")]
  public async Task<ActionResult<List<LookupItemDto>>> Sexes() =>
    await context.Sexes.AsNoTracking()
      .OrderBy(x => x.SexId)
      .Select(x => new LookupItemDto(x.SexId, x.SexCode, x.SexDesc ?? ""))
      .ToListAsync();

  [HttpGet("civil-statuses")]
  public async Task<ActionResult<List<LookupItemDto>>> CivilStatuses() =>
    await context.CivilStatuses.AsNoTracking()
      .OrderBy(x => x.CivilStatusId)
      .Select(x => new LookupItemDto(x.CivilStatusId, x.CivilStatusCode, x.CivilStatusDesc ?? ""))
      .ToListAsync();

  [HttpGet("name-suffixes")]
  public async Task<ActionResult<List<LookupItemDto>>> NameSuffixes() =>
    await context.NameSuffixes.AsNoTracking()
      .OrderBy(x => x.NameSuffixId)
      .Select(x => new LookupItemDto(x.NameSuffixId, x.NameSuffixCode, x.NameSuffixCode ?? ""))
      .ToListAsync();

  [HttpGet("relationship-statuses")]
  public async Task<ActionResult<List<LookupItemDto>>> RelationshipStatuses() =>
    await context.RelationshipStatuses.AsNoTracking()
      .OrderBy(x => x.RelationshipStatusId)
      .Select(x => new LookupItemDto(x.RelationshipStatusId, null, x.RelationshipStatusDesc ?? ""))
      .ToListAsync();

  [HttpGet("modes-of-assistance")]
  public async Task<ActionResult<List<LookupItemDto>>> ModesOfAssistance() =>
    await context.ModeOfAssistances.AsNoTracking()
      .OrderBy(x => x.ModeId)
      .Select(x => new LookupItemDto(x.ModeId, null, x.ModeDesc ?? ""))
      .ToListAsync();

  [HttpGet("sources-of-assistance")]
  public async Task<ActionResult<List<LookupItemDto>>> SourcesOfAssistance() =>
    await context.SourceOfAssistances.AsNoTracking()
      .OrderBy(x => x.SourceId)
      .Select(x => new LookupItemDto(x.SourceId, null, x.SourceDesc ?? ""))
      .ToListAsync();

  [HttpGet("provinces")]
  public async Task<ActionResult<List<LookupItemDto>>> Provinces() =>
    await context.Provinces.AsNoTracking()
      .OrderBy(x => x.ProvinceName)
      .Select(x => new LookupItemDto(x.ProvinceId, x.ProvinceCode, x.ProvinceName))
      .ToListAsync();

  // Filter required: the full lists are 1,600+ cities and 42,000+ barangays.
  [HttpGet("cities")]
  public async Task<ActionResult<List<LookupItemDto>>> Cities(int? provinceId)
  {
    if (provinceId is null)
      return BadRequest(new { message = "provinceId is required." });

    return await context.CityMunicipalities.AsNoTracking()
      .Where(x => x.ProvinceId == provinceId)
      .OrderBy(x => x.CityMunicipalityName)
      .Select(x => new LookupItemDto(x.CityMunicipalityId, x.CityMunicipalityCode, x.CityMunicipalityName))
      .ToListAsync();
  }

  [HttpGet("barangays")]
  public async Task<ActionResult<List<LookupItemDto>>> Barangays(int? cityMunicipalityId)
  {
    if (cityMunicipalityId is null)
      return BadRequest(new { message = "cityMunicipalityId is required." });

    return await context.Barangays.AsNoTracking()
      .Where(x => x.CityMunicipalityId == cityMunicipalityId)
      .OrderBy(x => x.BarangayName)
      .Select(x => new LookupItemDto(x.BarangayId, x.BarangayCode, x.BarangayName))
      .ToListAsync();
  }
}
