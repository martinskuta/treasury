using Microsoft.EntityFrameworkCore;
using Treasury.Api;
using Treasury.Api.Accounts;
using Treasury.Api.Auth;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.Imports;
using Treasury.Api.Matching;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Members;
using Treasury.Api.Reports;
using Treasury.Api.Rules;
using Treasury.Api.Storage;
using Treasury.Api.Tags;
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
builder.AddImports();
builder.AddStatementChecks();
builder.AddIngestScan();
builder.AddTags();
builder.AddRules(); // after AddTags: Rules run after date-range Tags in the Classify stage
builder.AddExchangeRates();
builder.AddMatching();

var app = builder.Build();

app.Services.GetRequiredService<DataRoot>(); // fail fast when Storage:DataRoot is missing

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>().Database.MigrateAsync();
    await AuthSetup.SeedFirstAdminAsync(scope.ServiceProvider, app.Configuration, app.Logger);
    await CategorySeeding.SeedCategoriesAsync(scope.ServiceProvider);
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
api.MapMatching();
api.MapImports();
api.MapIngestScan();
api.MapOwners();
api.MapMembers();
api.MapCategories();
api.MapReports();
api.MapTags();
api.MapRules();
api.MapFallback(() => Results.NotFound()).AllowAnonymous(); // unknown /api/* must never fall through to the SPA

// Production: the React build is copied into wwwroot by the AppHost (PublishWithContainerFiles).
app.UseFileServer();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
