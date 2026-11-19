using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Treasury.Api.Tests;

/// One PostgreSQL container per test session. A migrated "template" database is
/// built once; every test gets its own database cloned from it (fast, parallel-safe).
public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    public const string TemplateDb = "treasury_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecAsync($"CREATE DATABASE \"{TemplateDb}\"");

        // Run migrations once by booting the API against the template DB.
        await using (var app = new TreasuryApiFactory(ConnectionStringFor(TemplateDb)))
        {
            _ = app.Services; // triggers host start -> MigrateAsync in Program.cs
        }
        NpgsqlConnection.ClearAllPools(); // template must have no open connections
    }

    public async Task<string> CreateTestDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await ExecAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDb}\"");
        return ConnectionStringFor(name);
    }

    private string ConnectionStringFor(string db) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = db }.ConnectionString;

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
