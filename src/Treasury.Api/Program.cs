using Microsoft.EntityFrameworkCore;
using Treasury.Api;
using Treasury.Api.Accounts;
using Treasury.Api.Auth;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Storage;
using Treasury.Api.Transactions;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
// Connection string "treasurydb" is injected by the AppHost (AddDatabase("treasurydb")).
// Aspire's own DbContext health check is replaced by one with a stable name ("database").
builder.AddNpgsqlDbContext<TreasuryDbContext>("treasurydb", s => s.DisableHealthChecks = true);
builder.Services.AddHealthChecks().AddDbContextCheck<TreasuryDbContext>("database");
builder.Services.AddSingleton(TimeProvider.System);
builder.AddTreasuryAuth();
builder.AddDataRoot();
builder.AddAccounts();
builder.AddExchangeRates();

var app = builder.Build();

app.Services.GetRequiredService<DataRoot>(); // fail fast when Storage:DataRoot is missing

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>().Database.MigrateAsync();
    await AuthSetup.SeedFirstAdminAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

// Every /api endpoint requires a logged-in Member unless it opts out with AllowAnonymous().
var api = app.MapGroup("/api").RequireAuthorization();
api.MapHealth();
api.MapAuth();
api.MapAccounts();
api.MapTransactions();
api.MapOwners();
api.MapFallback(() => Results.NotFound()).AllowAnonymous(); // unknown /api/* must never fall through to the SPA

// Production: the React build is copied into wwwroot by the AppHost (PublishWithContainerFiles).
app.UseFileServer();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
