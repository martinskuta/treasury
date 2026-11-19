using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Treasury.Api.Tests;

/// The API on its own database. Unless <paramref name="seedAdmin"/> is false it starts with the
/// first-Admin settings (Admin:UserName / Admin:Password), as the AppHost passes them.
public sealed class TreasuryApiFactory(string connectionString, bool seedAdmin = true) : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin-Pa55word";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting is visible to WebApplication.CreateBuilder config, i.e. before AddNpgsqlDbContext reads it.
        builder.UseSetting("ConnectionStrings:treasurydb", connectionString);
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
            // services.Replace(ServiceDescriptor.Singleton<IExchangeRateProvider, FakeExchangeRateProvider>());
        });
    }
}
