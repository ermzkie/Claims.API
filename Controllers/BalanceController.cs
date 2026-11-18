using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Claims.API.Auth;
using Claims.API.Data;
using Claims.API.Models;

namespace Claims.API.Controllers
{
    // same finance figures as the dashboard budget card, so same rule: admins only
    [Authorize(Policy = AuthPolicies.UserAdmin)]
    [Route("api/[controller]")]
    [ApiController]
    public class BalanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BalanceController> _logger;

        public BalanceController(ApplicationDbContext context, ILogger<BalanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/balance/annual?year=2026
        [HttpGet("annual")]
        public async Task<ActionResult<IEnumerable<AnnualBalanceResult>>> GetAnnualBalance([FromQuery] int year)
        {
            List<AnnualBalanceResult> balances;
            try
            {
                // linked server, don't let it hang the page
                _context.Database.SetCommandTimeout(TimeSpan.FromSeconds(20));
                balances = await _context.AnnualBalanceResults
                    .FromSql($"EXEC getAnnualBalance @year = {year}")
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Balances could not be read for {Year}", year);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Budget figures from the finance system are not available right now." });
            }

            if (balances.Count == 0)
            {
                return NotFound(new { message = $"No balance records found for the year {year}." });
            }

            return Ok(balances);
        }
    }
}
