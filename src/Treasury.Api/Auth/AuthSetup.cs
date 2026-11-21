using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Treasury.Api.Data;

namespace Treasury.Api.Auth;

public static class AuthSetup
{
    /// ASP.NET Identity with a cookie session for the React app (same origin). Unauthenticated API calls get
    /// 401 and forbidden ones 403 - never a redirect, the SPA decides where to go.
    public static void AddTreasuryAuth(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<Member>(o => o.User.RequireUniqueEmail = false)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TreasuryDbContext>()
            .AddSignInManager<TreasurySignInManager>() // registered as SignInManager<Member>; blocks deactivated Members
            .AddDefaultTokenProviders(); // password reset by the Admin (token generated and used server-side)

        // Check the security stamp on every request (not every 30 min): a password reset, role change or
        // deactivation takes effect on the Member's open sessions immediately. One small query per request.
        builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "treasury.auth";
            o.Cookie.HttpOnly = true;
            // Strict + JSON-only endpoints is our CSRF protection (no antiforgery tokens), see docs/adr/0006.
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization();

        // Keys live in the database: container restarts must not log everyone out.
        builder.Services.AddDataProtection()
            .SetApplicationName("Treasury")
            .PersistKeysToDbContext<TreasuryDbContext>();
    }

    /// Creates the Admin role and, on first startup (no Members yet), the first Admin from
    /// Admin:UserName / Admin:Password (env Admin__UserName / Admin__Password, set by the AppHost).
    public static async Task SeedFirstAdminAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(Roles.Admin))
            await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin) { Id = Guid.CreateVersion7() });

        var members = services.GetRequiredService<UserManager<Member>>();
        if (members.Users.Any()) return;

        var userName = config["Admin:UserName"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("No Members exist and Admin:UserName / Admin:Password are not set; nobody can log in.");
            return;
        }

        var admin = new Member(userName, firstName: userName);
        var created = await members.CreateAsync(admin, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                "Could not create the first Admin: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        await members.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Created the first Admin {UserName}.", userName);
    }
}
