using Dapper;
using Microsoft.Data.SqlClient;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// Rows to test against, and the small amount of SQL it takes to set them up.
/// </summary>
/// <remarks>
/// Tests share one database, so nothing here reuses a phone number, a product
/// name or a slug: each helper mints a unique one. That is cheaper than
/// resetting the database between tests and it means a test never passes only
/// because it ran first.
/// </remarks>
public static class Given
{
    private static int _counter;

    /// <summary>A value no other test in this run will use.</summary>
    public static int Unique() => Interlocked.Increment(ref _counter);

    /// <summary>A phone in the shop's local format, unique to the caller.</summary>
    public static string NewPhone() => $"03{300000000 + Unique():D9}"[..11];

    public static async Task<SqlConnection> OpenAsync(this ShopDatabase db)
    {
        var connection = new SqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A product on the shelf with the stock asked for.</summary>
    public static async Task<int> AProductAsync(this ShopDatabase db, int stock = 10, decimal price = 5000)
    {
        var n = Unique();

        await using var connection = await db.OpenAsync();

        return await connection.ExecuteScalarAsync<int>(
            """
            DECLARE @CategoryId INT = (SELECT TOP 1 CategoryId FROM dbo.Categories WHERE IsActive = 1);
            DECLARE @SwatchId   INT = (SELECT TOP 1 SwatchId   FROM dbo.Swatches   WHERE IsActive = 1);

            INSERT INTO dbo.Products (CategoryId, Name, Slug, Price, Stock, LowStockAt,
                                      DefaultSwatchId, IsActive)
            OUTPUT INSERTED.ProductId
            VALUES (@CategoryId, @Name, @Slug, @Price, @Stock, 3, @SwatchId, 1);
            """,
            new { Name = $"Test Product {n}", Slug = $"test-product-{n}", Price = price, Stock = stock });
    }

    public static async Task<int> StockOfAsync(this ShopDatabase db, int productId)
    {
        await using var connection = await db.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT Stock FROM dbo.Products WHERE ProductId = @productId", new { productId });
    }

    public static async Task<int> SwatchIdAsync(this ShopDatabase db)
    {
        await using var connection = await db.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT TOP 1 SwatchId FROM dbo.Swatches WHERE IsActive = 1");
    }

    /// <summary>A staff user, which the identity procedures need to act on.</summary>
    public static async Task<string> AUserAsync(this ShopDatabase db, string role = "Staff")
    {
        var n  = Unique();
        var id = Guid.NewGuid().ToString();

        await using var connection = await db.OpenAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.Users (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
                                   EmailConfirmed, PasswordHash, FullName, IsActive, SecurityStamp)
            VALUES (@Id, @Email, UPPER(@Email), @Email, UPPER(@Email), 1, N'hash', @FullName, 1, NEWID());

            INSERT INTO dbo.UserRoles (UserId, RoleId)
            SELECT @Id, Id FROM dbo.Roles WHERE NormalizedName = UPPER(@Role);
            """,
            new { Id = id, Email = $"staff{n}@saadsshop.pk", FullName = $"Staff {n}", Role = role });

        return id;
    }

    /// <summary>A request that will place a valid order for one product.</summary>
    public static PlaceOrderRequest AnOrderFor(int productId, int quantity = 1, string? phone = null) => new()
    {
        CustomerName    = "Hina Tariq",
        Phone           = phone ?? NewPhone(),
        DeliveryAddress = "House 12, Street 4, Raja Bazaar, Rawalpindi",
        Area            = "Raja Bazaar",
        PaymentMethod   = "CashOnDelivery",
        Lines           = [new CartLineRequest { ProductId = productId, Quantity = quantity }],
    };
}
