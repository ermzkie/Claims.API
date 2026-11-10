using Microsoft.EntityFrameworkCore;
using Claims.API.Data;


var builder = WebApplication.CreateBuilder(args);

// Load User Secrets explicitly for IIS deployment
var secretsPath = @"C:\Users\Erma\AppData\Roaming\Microsoft\UserSecrets\61f54422-232e-4ec6-997e-b0744d8bda60\secrets.json";

builder.Configuration.AddJsonFile(
    secretsPath,
    optional: false,
    reloadOnChange: false
);

// Ensure environment variables can override secrets
builder.Configuration.AddEnvironmentVariables();

// Retrieve and validate connection string
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found. Check your secrets.json configuration."
    );
}

// Register database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString)
);

// Register controllers
builder.Services.AddControllers();

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// IIS manages the HTTP/HTTPS bindings.
// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Database health check
app.MapGet("/api/health/database", async (ApplicationDbContext db) =>
{
    bool connected = await db.Database.CanConnectAsync();

    if (connected)
    {
        return Results.Ok(new
        {
            Status = "Connected",
            Database = "sysCLAIMS"
        });
    }

    return Results.Problem("Unable to connect to the database.");
});

app.Run();