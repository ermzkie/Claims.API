using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Claims.API.Tests.Infrastructure;

// SQLite has no getdate()
public class SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
  : RelationalModelCustomizer(dependencies)
{
  public override void Customize(ModelBuilder modelBuilder, DbContext context)
  {
    base.Customize(modelBuilder, context);

    foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
    {
      if (property.GetDefaultValueSql() == "(getdate())")
      {
        property.SetDefaultValueSql("CURRENT_TIMESTAMP");
      }
    }
  }
}
