using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Data;

public partial class ApplicationDbContext
{
  // every claim write sets DateLastUpdated, so two saves on the same claim can't both win
  partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Transaction>().Property(t => t.DateLastUpdated).IsConcurrencyToken();
  }
}
