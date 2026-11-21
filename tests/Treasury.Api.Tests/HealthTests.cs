using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class HealthTests : ApiTestBase
{
    [Test]
    public async Task Api_health_reports_healthy_with_the_database_reachable()
    {
        var res = await Client.GetAsync("/api/health");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var health = await res.Content.ReadFromJsonAsync<HealthResponse>();
        await Assert.That(health!.Status).IsEqualTo("Healthy");
        await Assert.That(health.Checks).Contains(new HealthCheckEntry("database", "Healthy"));
    }

    [Test]
    public async Task Orchestrator_health_endpoints_are_mapped_outside_development()
    {
        // The AppHost and docker-compose probe /health and /alive; the test host runs as "Testing", not Development.
        await Assert.That((await Client.GetAsync("/health")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await Client.GetAsync("/alive")).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Unknown_api_route_is_404_and_not_the_spa()
    {
        var res = await Client.GetAsync("/api/does-not-exist");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private sealed record HealthResponse(string Status, List<HealthCheckEntry> Checks);

    private sealed record HealthCheckEntry(string Name, string Status);
}
