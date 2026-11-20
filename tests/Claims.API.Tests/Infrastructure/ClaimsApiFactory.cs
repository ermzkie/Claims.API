using Claims.API.Data;
using Claims.API.Models;
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

  public FakeFinanceAccounts Finance { get; } = new();

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
    // Program.cs needs one at startup, replaced with SQLite below
    builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused");

    builder.ConfigureServices(services =>
    {
      services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
      services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
      services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(_connection)
           .ReplaceService<IModelCustomizer, SqliteModelCustomizer>());

      services.RemoveAll<TimeProvider>();
      services.AddSingleton<TimeProvider>(Clock);

      services.RemoveAll<Claims.API.Services.IFinanceAccounts>();
      services.AddSingleton<Claims.API.Services.IFinanceAccounts>(Finance);
    });
  }

  protected override IHost CreateHost(IHostBuilder builder)
  {
    var host = base.CreateHost(builder);
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    // every claim points at a status, so the lookup rows must exist like in the real DB
    if (!db.ApprovalStatuses.Any())
      db.ApprovalStatuses.AddRange(
        new ApprovalStatus { ApprovalStatusId = 1, ApprovalStatusCode = "P", ApprovalStatusDesc = "Pending" },
        new ApprovalStatus { ApprovalStatusId = 2, ApprovalStatusCode = "R", ApprovalStatusDesc = "For Approval" },
        new ApprovalStatus { ApprovalStatusId = 3, ApprovalStatusCode = "A", ApprovalStatusDesc = "Approved" });
    db.SaveChanges();
    return host;
  }

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
