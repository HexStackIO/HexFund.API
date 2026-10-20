using HexFund.Core.Entities;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HexFund.IntegrationTests;

/// <summary>
/// Gives the tests a real PostgreSQL database. Set <see cref="EnvironmentVariable"/> to a connection
/// string for a server where the user may create databases (for example your local dev server). The
/// fixture creates a uniquely named database, applies every migration to it, and drops it afterwards,
/// so existing databases on that server are never touched.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "HEXFUND_TEST_DB";

    private string? _serverConnectionString;
    private string? _databaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(_serverConnectionString))
            return;

        _databaseName = $"hexfund_it_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_serverConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = _databaseName }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName == null || _serverConnectionString == null)
            return;

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>A new context configured like production: untracked reads, UTC timestamps.</summary>
    public HexFundDbContext CreateContext() => new(
        new DbContextOptionsBuilder<HexFundDbContext>()
            .UseNpgsql(ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    /// <summary>Inserts a user and returns it, so tests can attach data to a real foreign key.</summary>
    public async Task<User> AddUserAsync()
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            UserId = id,
            EntraObjectId = id.ToString(),
            Email = $"{id:N}@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        await using var context = CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>A test that needs the database. It is skipped, not failed, when no connection string is set.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.EnvironmentVariable)))
            Skip = $"Set {PostgresFixture.EnvironmentVariable} to a PostgreSQL connection string to run database tests.";
    }
}
