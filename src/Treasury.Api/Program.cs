using Microsoft.EntityFrameworkCore;
using Treasury.Api;
using Treasury.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
// Connection string "treasurydb" is injected by the AppHost (AddDatabase("treasurydb")).
// Aspire's own DbContext health check is replaced by one with a stable name ("database").
builder.AddNpgsqlDbContext<TreasuryDbContext>("treasurydb", s => s.DisableHealthChecks = true);
builder.Services.AddHealthChecks().AddDbContextCheck<TreasuryDbContext>("database");
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

var api = app.MapGroup("/api");
api.MapHealth();
api.MapFallback(() => Results.NotFound()); // unknown /api/* must never fall through to the SPA

// Production: the React build is copied into wwwroot by the AppHost (PublishWithContainerFiles).
app.UseFileServer();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
