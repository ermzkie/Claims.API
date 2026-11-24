using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.DTOs.Reports;
using Microsoft.AspNetCore.Authorization;

using Claims.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Controllers;

[Authorize(Policy = AuthPolicies.ClaimsViewer)]
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

        var reportRows = query.SelectMany(
            transaction => transaction.TransactionServices
                .SelectMany(service => service.ServiceFundAllocations)
                .Where(allocation => allocation.Status)
                .DefaultIfEmpty(),
            (transaction, allocation) => new { Transaction = transaction, Allocation = allocation });

        var count = await reportRows.CountAsync();
        if (count > MaxReportRows)
        {
            return BadRequest(new
            {
                message = $"This report has {count:N0} rows, more than {MaxReportRows:N0}. Narrow the dates or pick a program.",
            });
        }

        var records = await reportRows
            .OrderByDescending(x => x.Transaction.Date)
            .ThenBy(x => x.Transaction.TransactionId)
            .ThenBy(x => x.Allocation == null ? 0 : x.Allocation.ServiceFundAllocationId)
            .Select(x => new
            {
                x.Transaction.TransactionId,
                x.Transaction.AssistanceCode,
                x.Transaction.RefNo,
                x.Transaction.Date,
                x.Transaction.PatientPersonId,

                FirstName = x.Transaction.PatientPerson != null
                    ? x.Transaction.PatientPerson.FirstName
                    : null,

                MiddleName = x.Transaction.PatientPerson != null
                    ? x.Transaction.PatientPerson.MiddleName
                    : null,

                LastName = x.Transaction.PatientPerson != null
                    ? x.Transaction.PatientPerson.LastName
                    : null,

                BarangayName =
                    x.Transaction.PatientPerson != null &&
                    x.Transaction.PatientPerson.BarangayNavigation != null
                    ? x.Transaction.PatientPerson.BarangayNavigation.BarangayName
                    : null,

                CityName =
                    x.Transaction.PatientPerson != null &&
                    x.Transaction.PatientPerson.BarangayNavigation != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality != null
                    ? x.Transaction.PatientPerson.BarangayNavigation
                        .CityMunicipality.CityMunicipalityName
                    : null,

                ProvinceName =
                    x.Transaction.PatientPerson != null &&
                    x.Transaction.PatientPerson.BarangayNavigation != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality.Province != null
                    ? x.Transaction.PatientPerson.BarangayNavigation
                        .CityMunicipality.Province.ProvinceName
                    : null,

                x.Transaction.Representative,
                Amount = x.Allocation == null ? null : x.Allocation.Amount,
                CreditorId = x.Allocation == null ? null : x.Allocation.CreditorId,
                x.Transaction.IsReleased
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
                x.TransactionServices.Any(service => service.ServiceFundAllocations.Any(allocation =>
                    allocation.Status
                    && allocation.FundSource != null
                    && allocation.FundSource.SourceId == parameters.SourceId.Value)));
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

        // Retrieve allocation and beneficiary details
        var records = await query
            .SelectMany(
                transaction => transaction.TransactionServices
                    .SelectMany(service => service.ServiceFundAllocations)
                    .Where(allocation => allocation.Status
                        && (!parameters.SourceId.HasValue
                            || (allocation.FundSource != null
                                && allocation.FundSource.SourceId == parameters.SourceId.Value)))
                    .DefaultIfEmpty(),
                (transaction, allocation) => new { Transaction = transaction, Allocation = allocation })
            .Select(x => new
            {
                x.Transaction.TransactionId,
                PatientPersonId = x.Transaction.PatientPersonId!.Value,

                SourceId = x.Allocation != null && x.Allocation.FundSource != null
                    ? x.Allocation.FundSource.SourceId
                    : (short?)null,

                SourceDesc =
                    x.Allocation != null &&
                    x.Allocation.FundSource != null &&
                    x.Allocation.FundSource.Source != null
                    ? x.Allocation.FundSource.Source.SourceDesc
                    : null,

                Amount = x.Allocation == null ? null : x.Allocation.Amount,

                FirstName = x.Transaction.PatientPerson!.FirstName,
                MiddleName = x.Transaction.PatientPerson.MiddleName,
                LastName = x.Transaction.PatientPerson.LastName,

                BarangayName =
                    x.Transaction.PatientPerson.BarangayNavigation != null
                    ? x.Transaction.PatientPerson.BarangayNavigation.BarangayName
                    : null,

                CityName =
                    x.Transaction.PatientPerson.BarangayNavigation != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality != null
                    ? x.Transaction.PatientPerson.BarangayNavigation
                        .CityMunicipality.CityMunicipalityName
                    : null,

                ProvinceName =
                    x.Transaction.PatientPerson.BarangayNavigation != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality != null &&
                    x.Transaction.PatientPerson.BarangayNavigation.CityMunicipality.Province != null
                    ? x.Transaction.PatientPerson.BarangayNavigation
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

                    AvailmentCount = group
                        .Select(x => x.TransactionId)
                        .Distinct()
                        .Count(),

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