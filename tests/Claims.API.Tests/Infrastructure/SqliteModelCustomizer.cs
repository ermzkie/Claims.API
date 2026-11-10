using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Claims.API.Tests.Infrastructure;

/// <summary>
/// The production model uses SQL Server defaults such as (getdate()); SQLite needs CURRENT_TIMESTAMP instead.
/// </summary>
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
