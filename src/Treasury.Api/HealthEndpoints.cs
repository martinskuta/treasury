using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Treasury.Api;

public static class HealthEndpoints
{
    public record HealthCheckEntry(string Name, string Status);

    public record HealthResponse(string Status, IReadOnlyList<HealthCheckEntry> Checks);

    /// GET /api/health: overall status plus each registered check, as JSON for the frontend. Anonymous.
    public static void MapHealth(this RouteGroupBuilder api) =>
        api.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new HealthResponse(
                report.Status.ToString(),
                [.. report.Entries.Select(e => new HealthCheckEntry(e.Key, e.Value.Status.ToString()))])),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();
}
