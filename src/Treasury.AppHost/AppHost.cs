var builder = DistributedApplication.CreateBuilder(args);

// `aspire publish` / `aspire deploy` turn this model into docker-compose files (ADR-0004).
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard.WithHostPort(18888));

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("treasury-pgdata")
    .WithLifetime(ContainerLifetime.Persistent); // run mode: survives AppHost restarts
var db = postgres.AddDatabase("treasurydb");     // => ConnectionStrings__treasurydb in the API

// First Admin, created by the API on first startup (no Members yet). Run mode: set
// Parameters:admin-password via `aspire secret set` / user secrets, or enter it when the dashboard prompts.
// Publish/deploy: ADMIN_USERNAME / ADMIN_PASSWORD in the generated .env.
var adminUserName = builder.AddParameter("admin-username", "admin");
var adminPassword = builder.AddParameter("admin-password", secret: true);

var api = builder.AddDotnetProject("api", "../Treasury.Api/Treasury.Api.csproj")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("Admin__UserName", adminUserName)
    .WithEnvironment("Admin__Password", adminPassword)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
        // Fixed host port instead of the random default.
        service.Ports.Clear();
        service.Ports.Add("${TREASURY_HTTP_PORT:-8080}:${API_PORT}");
    });

// Run mode: Vite dev server; it proxies /api to the API using the injected API_HTTP(S) env vars.
var web = builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api);

// Publish: `npm run build` output is copied into the API image's wwwroot -> one container, one origin.
api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
