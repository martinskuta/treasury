using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Treasury.Api.Tests;

/// Per-test API instance on its own cloned database. The first Admin
/// (TreasuryApiFactory.AdminUserName / AdminPassword) is created when the API starts; Client starts signed out.
public abstract class ApiTestBase : IAsyncDisposable
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
    public required PostgresFixture Postgres { get; init; }

    protected TreasuryApiFactory App { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected string ConnectionString { get; private set; } = null!;

    [Before(Test)]
    public async Task StartApi()
    {
        ConnectionString = await Postgres.CreateTestDatabaseAsync();
        App = new TreasuryApiFactory(ConnectionString);
        Client = NewClient();
    }

    /// A fresh signed-out client with its own cookie jar (one per Member when a test needs several).
    protected HttpClient NewClient() => App.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });

    /// Logs Client in as the first Admin.
    protected Task LoginAsAdminAsync() =>
        LoginAsync(Client, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);

    public const string MemberPassword = "Member-Pa55word";

    /// Adds a (non-Admin) Member through the Admin's POST /api/admin/members (password MemberPassword) and
    /// returns a new client logged in as them. Doesn't touch Client's session.
    protected async Task<HttpClient> LoginAsNewMemberAsync(string userName)
    {
        await AddMemberAsync(userName);
        var client = NewClient();
        await LoginAsync(client, userName, MemberPassword);
        return client;
    }

    /// Adds a (non-Admin) Member with MemberPassword as the Admin (in a separate session) and returns their id.
    protected async Task<Guid> AddMemberAsync(string userName)
    {
        using var admin = NewClient();
        await LoginAsync(admin, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        var res = await admin.PostAsJsonAsync("/api/admin/members", new { userName, password = MemberPassword });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Adding Member {userName} failed: {(int)res.StatusCode}");
        return (await res.Content.ReadFromJsonAsync<CreatedMember>())!.Id;
    }

    private record CreatedMember(Guid Id);

    /// Logs the given client in through POST /api/auth/login and fails the test if that doesn't succeed.
    protected static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login as {userName} failed: {(int)res.StatusCode}");
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
        // Release this test's idle pooled connections so parallel tests don't exhaust max_connections.
        if (ConnectionString is not null) NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
    }
}
