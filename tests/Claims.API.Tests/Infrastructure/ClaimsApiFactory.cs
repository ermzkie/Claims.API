using Claims.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Claims.API.Tests.Infrastructure;

public class ClaimsApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-0123456789-abcdefghijklmnop";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    public ClaimsApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", "Claims.API");
        builder.UseSetting("Jwt:Audience", "Claims.Web");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_connection)
                       .ReplaceService<IModelCustomizer, SqliteModelCustomizer>());

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>New scoped-lifetime context over the shared in-memory database, for arrange/assert.</summary>
    public ApplicationDbContext CreateDbContext() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
