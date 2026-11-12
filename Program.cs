using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Auth;


var builder = WebApplication.CreateBuilder(args);

// Load User Secrets explicitly for IIS deployment
var secretsPath = @"C:\Users\Erma\AppData\Roaming\Microsoft\UserSecrets\61f54422-232e-4ec6-997e-b0744d8bda60\secrets.json";

builder.Configuration.AddJsonFile(
    secretsPath,
    optional: true,
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

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Claims.API.Services.UserAdminService>();
builder.Services.AddScoped<Claims.API.Services.PersonService>();
builder.Services.AddClaimsAuthentication(builder.Configuration);

// Unhandled errors: short JSON message + reference id for the client, details in the log only
builder.Services.AddExceptionHandler<Claims.API.Infrastructure.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Register controllers
builder.Services.AddControllers();

// OpenAPI
builder.Services.AddClaimsOpenApi();

var app = builder.Build();

// First in the pipeline so it catches errors from everything after it
app.UseExceptionHandler();

// Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    // API docs page at /swagger (development only)
    app.UseClaimsSwaggerUi();
}

// IIS manages the HTTP/HTTPS bindings.
// app.UseHttpsRedirection();

app.UseAuthentication();
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
            Status = "Connected"
        });
    }

    return Results.Problem("Unable to connect to the database.");
});

app.Run();
public partial class Program;
