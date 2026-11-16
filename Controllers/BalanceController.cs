using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Models;

namespace Claims.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BalanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BalanceController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/balance/annual?year=2026
        [HttpGet("annual")]
        public async Task<ActionResult<IEnumerable<AnnualBalanceResult>>> GetAnnualBalance([FromQuery] int year)
        {
            // Safely pass the year parameter to the stored procedure using EF Core interpolation
            var balances = await _context.AnnualBalanceResults
                .FromSql($"EXEC getAnnualBalance @year = {year}")
                .ToListAsync();

            if (balances == null || balances.Count == 0)
            {
                return NotFound($"No balance records found for the year {year}.");
            }

            return Ok(balances);
        }
    }
}