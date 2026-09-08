using System.Reflection;
using Dapper;
using SaadsShop.Api.Constants;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// That the scripts in <c>database/</c> deploy, and that the C# and the
/// database still agree about what exists.
/// </summary>
/// <remarks>
/// This whole class runs against a database built by applying the real files,
/// so it passing at all is already the first assertion: the schema, the
/// procedures and the reference seed apply cleanly, in the order
/// <c>apply.sh</c> uses, to an empty SQL Server.
/// </remarks>
[Collection(ShopDatabaseCollection.Name)]
public class DeploymentTests(ShopDatabase db)
{
    /// <summary>Every name in the constants class, which is the only place the app names one.</summary>
    private static readonly string[] Names =
        [.. typeof(StoredProcedures)
              .GetFields(BindingFlags.Public | BindingFlags.Static)
              .Where(f => f is { IsLiteral: true, FieldType.Name: nameof(String) })
              .Select(f => (string)f.GetRawConstantValue()!)];

    public static TheoryData<string> EveryProcedure() => [.. Names];

    [Theory]
    [MemberData(nameof(EveryProcedure))]
    public async Task The_procedure_the_code_calls_exists_in_the_database(string procedure)
    {
        //  A renamed procedure is otherwise found by whoever next opens that
        //  screen. StoredProcedures.cs says an integration test should walk it;
        //  this is that test.
        await using var connection = await db.OpenAsync();

        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.procedures WHERE name = @procedure", new { procedure });

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task Nothing_in_the_database_is_left_unused_by_the_code()
    {
        // The other direction: a procedure nobody calls is either a missing
        // feature or dead code, and either way somebody should know.
        var known = Names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var connection = await db.OpenAsync();
        var inDatabase = await connection.QueryAsync<string>(
            "SELECT name FROM sys.procedures WHERE name LIKE 'usp[_]%'");

        var uncalled = inDatabase.Where(name => !known.Contains(name)).ToList();

        Assert.True(uncalled.Count == 0,
            "No code calls these procedures: " + string.Join(", ", uncalled));
    }

    [Fact]
    public async Task The_reference_data_a_fresh_shop_needs_is_there()
    {
        await using var connection = await db.OpenAsync();

        var counts = await connection.QuerySingleAsync<(int Roles, int BedSizes, int Categories, int Settings)>(
            """
            SELECT (SELECT COUNT(*) FROM dbo.Roles),
                   (SELECT COUNT(*) FROM dbo.BedSizes),
                   (SELECT COUNT(*) FROM dbo.Categories),
                   (SELECT COUNT(*) FROM dbo.ShopSettings)
            """);

        Assert.True(counts.Roles >= 2, "Owner and Staff at least");
        Assert.Equal(3, counts.BedSizes);
        Assert.True(counts.Categories > 0);
        Assert.Equal(1, counts.Settings);
    }

    [Fact]
    public async Task The_settings_row_is_the_only_one_there_can_ever_be()
    {
        // The shop has one set of settings; a second row would mean the public
        // storefront and the panel could disagree about the delivery charge.
        await using var connection = await db.OpenAsync();

        var second = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            "INSERT INTO dbo.ShopSettings (ShopSettingsId, DeliveryCharge) VALUES (2, 300)"));

        Assert.NotNull(second);
    }

    [Fact]
    public async Task The_orders_index_the_list_screen_depends_on_carries_ProductName()
    {
        //  Without the INCLUDE, building the "what was ordered" summary is a
        //  lookup per row. Measured at 40,000 orders it costs the list screen
        //  23%. See database/schema/03_indexes.sql.
        await using var connection = await db.OpenAsync();

        var included = await connection.QueryAsync<string>(
            """
            SELECT COL_NAME(ic.object_id, ic.column_id)
            FROM   sys.index_columns AS ic
            JOIN   sys.indexes AS i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE  i.name = 'IX_OrderLines_OrderId' AND ic.is_included_column = 1
            """);

        Assert.Contains("ProductName", included);
    }

    [Fact]
    public async Task Every_response_code_a_procedure_can_return_is_one_the_API_knows()
    {
        //  The contract this project is built on: a procedure's response code
        //  IS the HTTP status, so a procedure inventing 422 would have the API
        //  emit a status the client has no handling for.
        await using var connection = await db.OpenAsync();

        var definitions = await connection.QueryAsync<string>(
            """
            SELECT m.definition
            FROM   sys.sql_modules AS m
            JOIN   sys.procedures AS p ON p.object_id = m.object_id
            WHERE  p.name LIKE 'usp[_]%'
            """);

        var allowed = new[] { 200, 201, 204, 400, 401, 403, 404, 409, 422, 429, 500 };

        var found = definitions
            .SelectMany(d => System.Text.RegularExpressions.Regex.Matches(
                d, @"@ResponseCode\s*=\s*(\d{3})"))
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();

        var unknown = found.Where(code => !allowed.Contains(code)).ToList();

        Assert.NotEmpty(found);
        Assert.True(unknown.Count == 0,
            "Procedures return codes the API cannot map: " + string.Join(", ", unknown));
    }

    [Fact]
    public async Task Every_procedure_writes_its_failures_to_the_error_log()
    {
        // A procedure that swallows an exception silently is one nobody can
        // debug from a shopkeeper's screenshot.
        await using var connection = await db.OpenAsync();

        var withoutLogging = await connection.QueryAsync<string>(
            """
            SELECT p.name
            FROM   sys.procedures AS p
            JOIN   sys.sql_modules AS m ON m.object_id = p.object_id
            WHERE  p.name LIKE 'usp[_]%'
              AND  m.definition LIKE '%BEGIN CATCH%'
              AND  m.definition NOT LIKE '%dbo.ErrorLog%'
            """);

        var offenders = withoutLogging.ToList();

        Assert.True(offenders.Count == 0,
            "These catch an error without logging it: " + string.Join(", ", offenders));
    }
}
