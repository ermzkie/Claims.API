using Claims.API.Data;
using Claims.API.DTOs.Lookups;
using Claims.API.Services;
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
    (await context.RelationshipStatuses.AsNoTracking()
      .Select(x => new LookupItemDto(x.RelationshipStatusId, null, x.RelationshipStatusDesc ?? "", x.RelationshipGroup))
      .ToListAsync())
      .OrderBy(x => RelationshipGroups.Order(x.Group))
      .ThenBy(x => x.Id)
      .ToList();

  [HttpGet("modes-of-assistance")]
  public async Task<ActionResult<List<LookupItemDto>>> ModesOfAssistance() =>
    await context.ModeOfAssistances.AsNoTracking()
      .OrderBy(x => x.ModeId)
      .Select(x => new LookupItemDto(x.ModeId, null, x.ModeDesc ?? ""))
      .ToListAsync();

  // fund sources page is admins only; social workers need the choices for a claim
  [HttpGet("fund-sources")]
  public async Task<ActionResult<List<FundSourceOptionDto>>> FundSources([FromQuery] short? year)
  {
    if (year is null)
      return BadRequest(new { message = "Give the year." });

    var rows = await context.FundSources.AsNoTracking()
      .Where(x => x.Status && x.Year == year)
      .OrderBy(x => x.FundSourceId)
      .Select(x => new { x.FundSourceId, x.Year, Desc = x.Source != null ? x.Source.SourceDesc : null })
      .ToListAsync();
    return rows
      .Select(x => new FundSourceOptionDto(
        x.FundSourceId,
        string.IsNullOrWhiteSpace(x.Desc) ? $"Fund source {x.FundSourceId}" : x.Desc.Trim(),
        x.Year!.Value))
      .ToList();
  }

  [HttpGet("lane-types")]
  public async Task<ActionResult<List<LookupItemDto>>> LaneTypes() =>
    await context.LaneTypes.AsNoTracking()
      .OrderBy(x => x.LaneTypeId)
      .Select(x => new LookupItemDto(x.LaneTypeId, x.LaneTypeCode, x.LaneTypeDesc ?? ""))
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

  // filter required, 42,000+ barangays
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

  // Assistance has a row per budget period
  [HttpGet("assistance-programs")]
  public async Task<ActionResult<List<AssistanceProgramDto>>> AssistancePrograms() =>
    (await context.Assistances.AsNoTracking()
      .Where(x => x.AssistanceCode != null && x.AssistanceType != null)
      .Select(x => new { x.AssistanceCode, x.AssistanceType })
      .ToListAsync())
      .GroupBy(x => x.AssistanceCode!.Trim().ToUpperInvariant())
      .Select(g => new AssistanceProgramDto(g.Key, g.First().AssistanceType!.Trim()))
      .OrderBy(x => x.Code)
      .ToList();
}
