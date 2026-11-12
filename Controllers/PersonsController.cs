using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Models;

namespace Claims.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PersonsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PersonsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Persons
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Person>>> GetPeople()
        {
            return await _context.People
                .Include(p => p.BarangayNavigation)
                .Include(p => p.CivilStatus)
                .Include(p => p.NameSuffix)
                .Include(p => p.Sex)
                .Take(100)
                .ToListAsync();
        }

        // GET: api/Persons/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Person>> GetPerson(int id)
        {
            var person = await _context.People
                .Include(p => p.BarangayNavigation)
                .Include(p => p.CivilStatus)
                .Include(p => p.NameSuffix)
                .Include(p => p.Sex)
                .FirstOrDefaultAsync(p => p.PersonId == id);

            if (person == null)
            {
                return NotFound();
            }

            return person;
        }

        // PUT: api/Persons/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPerson(int id, Person person)
        {
            if (id != person.PersonId)
            {
                return BadRequest();
            }

            person.DateLastUpdated = DateTime.Now;
            _context.Entry(person).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PersonExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Persons
        [HttpPost]
        public async Task<ActionResult<Person>> PostPerson(Person person)
        {
            person.DateCreated = DateTime.Now;
            _context.People.Add(person);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPerson), new { id = person.PersonId }, person);
        }

        // DELETE: api/Persons/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePerson(int id)
        {
            var person = await _context.People.FindAsync(id);
            if (person == null)
            {
                return NotFound();
            }

            _context.People.Remove(person);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool PersonExists(int id)
        {
            return _context.People.Any(e => e.PersonId == id);
        }

        // GET: api/Persons/search
        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Person>>> SearchPersons(
            [FromQuery] string? firstName, 
            [FromQuery] string? middleName, 
            [FromQuery] string? lastName)
        {
            var query = _context.People
                .Include(p => p.BarangayNavigation)
                .Include(p => p.CivilStatus)
                .Include(p => p.NameSuffix)
                .Include(p => p.Sex)
                .AsQueryable();

            // Apply filters only if the parameter is provided/not empty
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                query = query.Where(p => p.FirstName != null && p.FirstName.Contains(firstName));
            }

            if (!string.IsNullOrWhiteSpace(middleName))
            {
                query = query.Where(p => p.MiddleName != null && p.MiddleName.Contains(middleName));
            }

            if (!string.IsNullOrWhiteSpace(lastName))
            {
                query = query.Where(p => p.LastName != null && p.LastName.Contains(lastName));
            }

            return await query.Take(10).ToListAsync();
        }

        // GET: api/Persons/search-full-name
        [HttpGet("search-full-name")]
        public async Task<ActionResult<IEnumerable<Person>>> SearchByFullName([FromQuery] string nameKeyword)
        {
            if (string.IsNullOrWhiteSpace(nameKeyword))
            {
                return BadRequest("Please provide a name keyword to search.");
            }

            var keyword = nameKeyword.Trim();

            var persons = await _context.People
                .Include(p => p.BarangayNavigation)
                .Include(p => p.CivilStatus)
                .Include(p => p.NameSuffix)
                .Include(p => p.Sex)
                .Where(p => (p.FirstName != null && p.FirstName.Contains(keyword)) ||
                            (p.MiddleName != null && p.MiddleName.Contains(keyword)) ||
                            (p.LastName != null && p.LastName.Contains(keyword)))
                .Take(2)
                .ToListAsync();

            return persons;
        }

        // GET: api/Persons/search-three-words
        [HttpGet("search-three-words")]
        public async Task<ActionResult<IEnumerable<Person>>> SearchByThreeWords([FromQuery] string queryWords)
        {
            if (string.IsNullOrWhiteSpace(queryWords))
            {
                return BadRequest("Please provide search words.");
            }

            // Split the input into individual words and filter out empty entries
            var words = queryWords
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .ToArray();

            if (words.Length == 0)
            {
                return BadRequest("No valid search words provided.");
            }

            var query = _context.People
                .Include(p => p.BarangayNavigation)
                .Include(p => p.CivilStatus)
                .Include(p => p.NameSuffix)
                .Include(p => p.Sex)
                .AsQueryable();

            // For every word provided, ensure that the word matches AT LEAST ONE of the name fields (First, Middle, or Last)
            foreach (var word in words)
            {
                var currentWord = word; // Local copy for closure/LINQ safety
                query = query.Where(p => 
                    (p.FirstName != null && p.FirstName.Contains(currentWord)) ||
                    (p.MiddleName != null && p.MiddleName.Contains(currentWord)) ||
                    (p.LastName != null && p.LastName.Contains(currentWord))
                );
            }

            return await query.Take(10).ToListAsync();
        }
    }
}