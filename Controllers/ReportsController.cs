using Claims.API.Data;
using Claims.API.DTOs.Reports;
using Microsoft.AspNetCore.Authorization;

using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Authorize]
[Route("api/reports")]
[ApiController]
public class ReportsController(ApplicationDbContext context)
    : ControllerBase
{
    // a bigger report needs narrower dates, one response can't carry all the claims
    public const int MaxReportRows = 5000;

    // =====================================================
    // TRANSACTION REPORT
    // =====================================================

    [HttpGet("transactions")]
    public async Task<ActionResult<List<TransactionReportDto>>>
        GetTransactionReport(
            [FromQuery] TransactionReportParameters parameters)
    {
        // Validate date range
        if (parameters.DateFrom.HasValue &&
            parameters.DateTo.HasValue &&
            parameters.DateFrom.Value.Date >
            parameters.DateTo.Value.Date)
        {
            return BadRequest(
                "Date From cannot be later than Date To.");
        }

        // old-system claims have Status false but still count
        var query = context.Transactions
            .AsNoTracking()
            .Where(ClaimRules.Counted);

        // Assistance Type filter
        if (!string.IsNullOrWhiteSpace(parameters.AssistanceCode))
        {
            query = query.Where(x =>
                x.AssistanceCode == parameters.AssistanceCode);
        }

        // Date From filter
        if (parameters.DateFrom.HasValue)
        {
            var dateFrom = parameters.DateFrom.Value.Date;

            query = query.Where(x =>
                x.Date >= dateFrom);
        }

        // Date To filter (inclusive)
        if (parameters.DateTo.HasValue)
        {
            var exclusiveEndDate =
                parameters.DateTo.Value.Date.AddDays(1);

            query = query.Where(x =>
                x.Date < exclusiveEndDate);
        }

        var count = await query.CountAsync();
        if (count > MaxReportRows)
        {
            return BadRequest(new
            {
                message = $"This report has {count:N0} claims, more than {MaxReportRows:N0}. Narrow the dates or pick a program.",
            });
        }

        var records = await query
            .OrderByDescending(x => x.Date)
            .Select(x => new
            {
                x.TransactionId,
                x.AssistanceCode,
                x.RefNo,
                x.Date,
                x.PatientPersonId,

                FirstName = x.PatientPerson != null
                    ? x.PatientPerson.FirstName
                    : null,

                MiddleName = x.PatientPerson != null
                    ? x.PatientPerson.MiddleName
                    : null,

                LastName = x.PatientPerson != null
                    ? x.PatientPerson.LastName
                    : null,

                BarangayName =
                    x.PatientPerson != null &&
                    x.PatientPerson.BarangayNavigation != null
                    ? x.PatientPerson.BarangayNavigation.BarangayName
                    : null,

                CityName =
                    x.PatientPerson != null &&
                    x.PatientPerson.BarangayNavigation != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality != null
                    ? x.PatientPerson.BarangayNavigation
                        .CityMunicipality.CityMunicipalityName
                    : null,

                ProvinceName =
                    x.PatientPerson != null &&
                    x.PatientPerson.BarangayNavigation != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality.Province != null
                    ? x.PatientPerson.BarangayNavigation
                        .CityMunicipality.Province.ProvinceName
                    : null,

                x.Representative,
                x.Amount,
                x.CreditorId,
                x.IsReleased
            })
            .ToListAsync();

        var result = records.Select(x =>
            new TransactionReportDto
            {
                TransactionId = x.TransactionId,
                AssistanceCode = x.AssistanceCode,
                RefNo = x.RefNo,
                Date = x.Date,
                PatientPersonId = x.PatientPersonId,

                PatientName = string.Join(" ",
                    new[]
                    {
                        x.FirstName,
                        x.MiddleName,
                        x.LastName
                    }.Where(s =>
                        !string.IsNullOrWhiteSpace(s))),

                PatientAddress = string.Join(", ",
                    new[]
                    {
                        x.BarangayName,
                        x.CityName,
                        x.ProvinceName
                    }.Where(s =>
                        !string.IsNullOrWhiteSpace(s))),

                Representative = x.Representative,
                Amount = x.Amount,
                CreditorId = x.CreditorId,
                IsReleased = x.IsReleased,
                // only counted claims are listed
                Status = true
            }).ToList();

        return Ok(result);
    }


    // =====================================================
    // PERENNIAL CLIENTS REPORT
    // =====================================================

    [HttpGet("perennial-clients")]
    public async Task<ActionResult<List<PerennialClientReportDto>>>
        GetPerennialClients(
            [FromQuery] PerennialClientReportParameters parameters)
    {
        // Validate date range
        if (parameters.DateFrom.HasValue &&
            parameters.DateTo.HasValue &&
            parameters.DateFrom.Value.Date >
            parameters.DateTo.Value.Date)
        {
            return BadRequest(
                "Date From cannot be later than Date To.");
        }

        // Base query
        var query = context.Transactions
            .AsNoTracking()
            .Where(ClaimRules.Counted)
            .Where(x =>
                x.PatientPerson != null &&
                x.PatientPersonId.HasValue);

        // Filter by Source of Assistance
        if (parameters.SourceId.HasValue)
        {
            query = query.Where(x =>
                x.FundSource != null &&
                x.FundSource.SourceId == parameters.SourceId.Value);
        }

        // Date From filter
        if (parameters.DateFrom.HasValue)
        {
            var dateFrom = parameters.DateFrom.Value.Date;

            query = query.Where(x =>
                x.Date >= dateFrom);
        }

        // Date To filter (inclusive)
        if (parameters.DateTo.HasValue)
        {
            var exclusiveEndDate =
                parameters.DateTo.Value.Date.AddDays(1);

            query = query.Where(x =>
                x.Date < exclusiveEndDate);
        }

        // Retrieve transaction and beneficiary details
        var records = await query
            .Select(x => new
            {
                PatientPersonId = x.PatientPersonId!.Value,

                SourceId = x.FundSource != null
                    ? x.FundSource.SourceId
                    : (short?)null,

                SourceDesc =
                    x.FundSource != null &&
                    x.FundSource.Source != null
                    ? x.FundSource.Source.SourceDesc
                    : null,

                Amount = x.Amount,

                FirstName = x.PatientPerson!.FirstName,
                MiddleName = x.PatientPerson.MiddleName,
                LastName = x.PatientPerson.LastName,

                BarangayName =
                    x.PatientPerson.BarangayNavigation != null
                    ? x.PatientPerson.BarangayNavigation.BarangayName
                    : null,

                CityName =
                    x.PatientPerson.BarangayNavigation != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality != null
                    ? x.PatientPerson.BarangayNavigation
                        .CityMunicipality.CityMunicipalityName
                    : null,

                ProvinceName =
                    x.PatientPerson.BarangayNavigation != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality != null &&
                    x.PatientPerson.BarangayNavigation.CityMunicipality.Province != null
                    ? x.PatientPerson.BarangayNavigation
                        .CityMunicipality.Province.ProvinceName
                    : null
            })
            .ToListAsync();

        // Group by beneficiary and source of assistance
        var result = records
            .GroupBy(x => new
            {
                x.PatientPersonId,
                x.SourceId,
                x.SourceDesc
            })
            .Select(group =>
            {
                var person = group.First();

                return new PerennialClientReportDto
                {
                    PatientPersonId = group.Key.PatientPersonId,

                    PatientName = string.Join(" ",
                        new[]
                        {
                            person.FirstName,
                            person.MiddleName,
                            person.LastName
                        }.Where(s =>
                            !string.IsNullOrWhiteSpace(s))),

                    PatientAddress = string.Join(", ",
                        new[]
                        {
                            person.BarangayName,
                            person.CityName,
                            person.ProvinceName
                        }.Where(s =>
                            !string.IsNullOrWhiteSpace(s))),

                    AssistanceType =
                        group.Key.SourceDesc ?? "N/A",

                    AvailmentCount = group.Count(),

                    TotalAmount = group.Sum(x =>
                        x.Amount ?? 0m)
                };
            })
            .OrderByDescending(x => x.AvailmentCount)
            .ThenBy(x => x.PatientName)
            .ToList();

        return Ok(result);
    }
}