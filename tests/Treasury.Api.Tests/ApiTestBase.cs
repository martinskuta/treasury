using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Treasury.Api.Auth;

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

    /// Creates a (non-Admin) Member and returns a new client logged in as them.
    /// TODO(#4): create the Member through the Admin's member endpoint once it exists, instead of UserManager.
    protected async Task<HttpClient> LoginAsNewMemberAsync(string userName)
    {
        using (var scope = App.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
            var created = await members.CreateAsync(new Member(userName), MemberPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }
        var client = NewClient();
        await LoginAsync(client, userName, MemberPassword);
        return client;
    }

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
        // Release this test's idle pooled connections now instead of after the pool's idle timeout.
        if (ConnectionString is not null)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(conn);
        }
    }
}
