using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Data;

public partial class ApplicationDbContext
{
  public virtual DbSet<Desk> Desks { get; set; }

  // every claim write sets DateLastUpdated, so two saves on the same claim can't both win
  partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Transaction>().Property(t => t.DateLastUpdated).IsConcurrencyToken();
    // column added by script with DEFAULT 0
    modelBuilder.Entity<Person>().Property(p => p.BirthdateEstimated).HasDefaultValue(false);
  }
}
