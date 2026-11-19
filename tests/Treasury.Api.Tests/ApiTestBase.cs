namespace Treasury.Api.Tests;

/// Per-test API instance on its own cloned database.
public abstract class ApiTestBase : IAsyncDisposable
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
    public required PostgresFixture Postgres { get; init; }

    protected TreasuryApiFactory App { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [Before(Test)]
    public async Task StartApi()
    {
        App = new TreasuryApiFactory(await Postgres.CreateTestDatabaseAsync());
        // HandleCookies keeps auth cookies between requests
        Client = App.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
}
