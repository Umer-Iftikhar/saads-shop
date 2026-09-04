using System.Data;
using Microsoft.Data.SqlClient;

namespace SaadsShop.Api.Data;

public interface ISqlConnectionFactory
{
    /// <summary>Opens a connection. The caller owns it and must dispose it.</summary>
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Hands out open connections to the shop database.
/// </summary>
/// <remarks>
/// There is no connection pooling code here because there does not need to be:
/// <c>SqlClient</c> pools underneath, and "opening" a connection usually just
/// takes one from the pool. Writing our own would be slower and wrong.
///
/// <c>QUOTED_IDENTIFIER</c> matters for this database — <c>Products</c> carries
/// filtered indexes, and SQL Server refuses writes from a session where the
/// option is off. SqlClient sets it on for every connection it opens, so there
/// is nothing to do here; the note exists so nobody "simplifies" by moving to a
/// driver that does not. See docs/database.md.
/// </remarks>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("SaadsShop");

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Fail at startup rather than on the first request. A misconfigured
            // deployment should refuse to come up, not serve 500s.
            throw new InvalidOperationException(
                "Connection string 'SaadsShop' is not configured. Set it with " +
                "'dotnet user-secrets set \"ConnectionStrings:SaadsShop\" \"...\"' in development, " +
                "or the ConnectionStrings__SaadsShop environment variable in production.");
        }

        var builder = new SqlConnectionStringBuilder(raw)
        {
            // Belt and braces: even if the supplied string forgot these, the
            // application never talks to SQL Server unencrypted.
            Encrypt = true,

            // Long enough to survive a container restart, short enough that a
            // wedged database surfaces as an error rather than a hung request.
            ConnectTimeout = 15,

            // Named so `sp_who2` and Extended Events show which app is holding
            // a lock. Worth its weight the first time checkout deadlocks.
            ApplicationName = "SaadsShop.Api"
        };

        _connectionString = builder.ConnectionString;
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
