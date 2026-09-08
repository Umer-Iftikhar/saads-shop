using System.Data;
using Microsoft.Data.SqlClient;
using SaadsShop.Api.Data;
using Testcontainers.MsSql;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// A real SQL Server with the shop's real schema and real procedures on it.
/// </summary>
/// <remarks>
/// The unit suite proves the C# reads a procedure's answer correctly. Nothing
/// there can prove the procedure gives the right answer, because there is no
/// database — and this project's rules live in the procedures. So these tests
/// run the actual scripts from <c>database/</c> against SQL Server in a
/// container: the same files <c>apply.sh</c> deploys, in the same order.
///
/// One container for the whole run, because starting SQL Server costs about a
/// minute and paying that per test class would make the suite something nobody
/// runs. Isolation comes from each test working on its own rows instead —
/// see <see cref="ShopDatabaseExtensions.NewPhone"/> and friends.
///
/// Set <c>SAADSSHOP_TEST_SQL</c> to a connection string to run against a server
/// you already have, and no container is started at all.
/// </remarks>
public sealed class ShopDatabase : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The factory the real repositories take, pointed at this database.</summary>
    public ISqlConnectionFactory Connections { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("SAADSSHOP_TEST_SQL");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            ConnectionString = existing;
        }
        else
        {
            //  Pinned rather than :latest — a test suite that changes engine
            //  version when someone else pushes a tag is not a test suite.
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        await ApplyDatabaseAsync();

        //  The same call AddShopServices makes at startup. Without it the first
        //  query carrying a DateOnly throws, which is a driver limitation the
        //  tests should meet exactly as production does.
        DapperTypeHandlers.Register();

        Connections = new TestConnectionFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    // ── applying the real scripts ────────────────────────────────────────────

    /// <summary>
    /// Runs schema, then procedures, then reference data — the order
    /// <c>apply.sh</c> uses, since these tests are also a check that it works.
    /// </summary>
    private async Task ApplyDatabaseAsync()
    {
        var database = FindDatabaseDirectory();

        var scripts = Directory.GetFiles(Path.Combine(database, "schema"), "*.sql").OrderBy(f => f)
            .Concat(Directory.GetFiles(Path.Combine(database, "procedures"), "*.sql").OrderBy(f => f))
            .Concat([
                Path.Combine(database, "seed", "01_reference.sql"),
                Path.Combine(database, "seed", "02_catalog.sql"),
            ]);

        foreach (var script in scripts)
            await RunScriptAsync(await File.ReadAllTextAsync(script), Path.GetFileName(script));
    }

    /// <summary>
    /// Splits on GO and runs the batches.
    /// </summary>
    /// <remarks>
    /// GO is not T-SQL — it is a separator sqlcmd understands and the driver
    /// does not — so a script has to be cut on it before SqlClient will take
    /// it. A procedure body containing the word "go" is safe: the split only
    /// matches GO alone on its own line.
    /// </remarks>
    private async Task RunScriptAsync(string script, string name)
    {
        var batches = System.Text.RegularExpressions.Regex.Split(
            script, @"^\s*GO\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;

            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };

            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"{name} failed to apply: {ex.Message}\n\nBatch was:\n{batch[..Math.Min(400, batch.Length)]}", ex);
            }
        }
    }

    /// <summary>
    /// Walks up from the test binary to the repository's <c>database/</c>.
    /// </summary>
    /// <remarks>
    /// The scripts are not copied into the output directory on purpose. Copying
    /// them would mean a stale copy could pass while the real script was
    /// broken, which is the one thing this suite exists to catch.
    /// </remarks>
    private static string FindDatabaseDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database");
            if (Directory.Exists(Path.Combine(candidate, "procedures"))) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository's database/ directory above " + AppContext.BaseDirectory);
    }

    private sealed class TestConnectionFactory(string connectionString) : ISqlConnectionFactory
    {
        public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return connection;
        }
    }
}

/// <summary>
/// One container, shared. xUnit runs collections in sequence, so tests inside
/// this one never run against the database at the same time as each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ShopDatabaseCollection : ICollectionFixture<ShopDatabase>
{
    public const string Name = "shop database";
}
