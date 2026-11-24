using Claims.API.Data;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Tests.Infrastructure;

public static class TestPrograms
{
  // programs are SourceOfAssistance rows with a code now (the Assistance table is gone)
  public static async Task AddProgramsAsync(this ApplicationDbContext db, params (string? Code, string? Name)[] programs)
  {
    var next = (short)((await db.SourceOfAssistances.MaxAsync(s => (short?)s.SourceId) ?? 0) + 1);
    foreach (var (code, name) in programs)
      db.SourceOfAssistances.Add(new SourceOfAssistance { SourceId = next++, SourceCode = code, SourceDesc = name });
    await db.SaveChangesAsync();
  }
}
