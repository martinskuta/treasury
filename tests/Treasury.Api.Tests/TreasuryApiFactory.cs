using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests;

/// The API on its own database. Unless <paramref name="seedAdmin"/> is false it starts with the
/// first-Admin settings (Admin:UserName / Admin:Password), as the AppHost passes them.
public sealed class TreasuryApiFactory(string connectionString, bool seedAdmin = true) : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin-Pa55word";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    /// The exchange-rate source this API fetches from (instead of Frankfurter); publish rates on it.
    public FakeExchangeRateProvider ExchangeRates { get; } = new();

    /// This API's own data root (Storage:DataRoot): a fresh temp folder, deleted on dispose.
    public string DataRoot { get; } = Directory.CreateTempSubdirectory("treasury-test-").FullName;

    /// Where Ingest folders live under the data root.
    public string IngestRoot => Path.Combine(DataRoot, "ingest");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting is visible to WebApplication.CreateBuilder config, i.e. before AddNpgsqlDbContext reads it.
        builder.UseSetting("ConnectionStrings:treasurydb", connectionString);
        builder.UseSetting("Storage:DataRoot", DataRoot);
        if (seedAdmin)
        {
            builder.UseSetting("Admin:UserName", AdminUserName);
            builder.UseSetting("Admin:Password", AdminPassword);
        }
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            // The auth cookie keeps real time: the test cookie jar drops cookies whose Expires (computed from
            // the fake clock) lies in the real past, which would break every logged-in test 14 days after 2026-11-20.
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                o => o.TimeProvider = TimeProvider.System);
            services.RemoveAll<IExchangeRateProvider>(); // never call the real Frankfurter
            services.AddSingleton<IExchangeRateProvider>(ExchangeRates);
            foreach (var integration in TestIntegrations.All)
                services.AddSingleton<IIntegration>(integration);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(DataRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
