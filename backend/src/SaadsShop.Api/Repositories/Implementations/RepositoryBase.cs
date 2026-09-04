using System.Data;
using Dapper;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;

namespace SaadsShop.Api.Repositories.Implementations;

/// <summary>
/// Shared plumbing for the stored-procedure contract: open a connection, call
/// one procedure with <c>QueryMultipleAsync</c>, read the payload sets, then the
/// status row that always comes last.
/// </summary>
/// <remarks>
/// Every repository in this project derives from this and calls exactly one
/// procedure per method. There is no query builder, no inline SQL and nothing
/// that concatenates a string into a command — the only thing a repository can
/// express is "run this procedure with these parameters".
/// </remarks>
public abstract class RepositoryBase(ISqlConnectionFactory connectionFactory)
{
    /// <summary>
    /// How long a procedure may run before the command is cancelled. Reads and
    /// writes here are all indexed single-row or small-page operations; if one
    /// takes longer than this, something is wrong and failing fast beats
    /// holding a connection and a lock.
    /// </summary>
    protected const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Runs a procedure and lets <paramref name="read"/> consume its payload
    /// sets in order. The status row is read afterwards, by this method, so no
    /// caller has to remember to do it — and none can read it out of order.
    /// </summary>
    protected async Task<ProcedureResult<T>> ExecuteAsync<T>(
        string procedure,
        object? parameters,
        Func<SqlMapper.GridReader, Task<T>> read,
        CancellationToken ct = default)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);

        var command = new CommandDefinition(
            procedure,
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: ct);

        await using var grid = await connection.QueryMultipleAsync(command);

        var data   = await read(grid);
        var status = await grid.ReadSingleAsync<ProcedureStatus>();

        return ProcedureResult<T>.From(data, status);
    }

    /// <summary>For procedures whose only result set is the status row.</summary>
    protected Task<ProcedureResult<bool>> ExecuteAsync(
        string procedure,
        object? parameters,
        CancellationToken ct = default)
        => ExecuteAsync(procedure, parameters, _ => Task.FromResult(true), ct);

    /// <summary>
    /// Builds the table-valued parameter the checkout procedure expects.
    /// </summary>
    /// <remarks>
    /// A TVP carries the whole cart across in one call, so the checkout
    /// transaction opens once and is held for the shortest possible time. A
    /// procedure call per line would either hold a transaction open across
    /// round trips or give up atomicity — neither is acceptable when the thing
    /// being protected is stock.
    ///
    /// The column order must match <c>dbo.OrderLineTableType</c>: SQL Server
    /// binds TVP columns positionally, not by name, so a reordering here would
    /// silently swap quantity and product id.
    /// </remarks>
    protected static DataTable BuildOrderLinesTable(
        IEnumerable<(int ProductId, int Quantity, int? SwatchId, string? BedSize)> lines)
    {
        var table = new DataTable();
        table.Columns.Add("ProductId", typeof(int));
        table.Columns.Add("Quantity",  typeof(int));
        table.Columns.Add("SwatchId",  typeof(int));
        table.Columns.Add("BedSize",   typeof(string));

        foreach (var line in lines)
        {
            table.Rows.Add(
                line.ProductId,
                line.Quantity,
                (object?)line.SwatchId ?? DBNull.Value,
                (object?)line.BedSize  ?? DBNull.Value);
        }

        return table;
    }

    /// <summary>Builds the <c>dbo.IntListTableType</c> parameter.</summary>
    protected static DataTable BuildIntListTable(IEnumerable<int> values)
    {
        var table = new DataTable();
        table.Columns.Add("Value", typeof(int));

        // Distinct because the type has a primary key on Value; a duplicate
        // swatch id from an over-eager editor would otherwise fail the insert.
        foreach (var value in values.Distinct())
            table.Rows.Add(value);

        return table;
    }

    /// <summary>
    /// Combines ordinary parameters with a table-valued one.
    /// </summary>
    /// <remarks>
    /// A TVP cannot simply be a property on an anonymous object: Dapper only
    /// honours <c>ICustomQueryParameter</c> when it is added to
    /// <see cref="DynamicParameters"/>, and nested in an anonymous type it is
    /// treated as an ordinary value, which fails at execution with
    /// "No mapping exists from object type Dapper.TableValuedParameter".
    ///
    /// The type name is required — without it SQL Server cannot resolve the
    /// parameter's shape.
    /// </remarks>
    protected static DynamicParameters WithTableParameter(
        object? scalarParameters, string parameterName, DataTable table, string typeName)
    {
        var parameters = scalarParameters is null
            ? new DynamicParameters()
            : new DynamicParameters(scalarParameters);

        parameters.Add(parameterName, table.AsTableValuedParameter(typeName));

        return parameters;
    }
}
