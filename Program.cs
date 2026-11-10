using Microsoft.EntityFrameworkCore;
using Claims.API.Data;
using Claims.API.Auth;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5224");

// Register database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddClaimsAuthentication(builder.Configuration);

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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
public partial class Program;
