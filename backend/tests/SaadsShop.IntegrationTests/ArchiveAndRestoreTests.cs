using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Implementations.Commands;
using SaadsShop.Api.Repositories.Implementations.Queries;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// Removing a product, and getting it back.
/// </summary>
/// <remarks>
/// Nothing in this application deletes a product row. An order line names the
/// product it sold, and the shop's own history is built on those rows, so a
/// hard delete would rewrite what customers were charged. "Delete" in the panel
/// archives; these tests are what says so.
/// </remarks>
[Collection(ShopDatabaseCollection.Name)]
public class ArchiveAndRestoreTests(ShopDatabase db)
{
    private CatalogCommandRepository Catalog => new(db.Connections);
    private CatalogQueryRepository   Reads   => new(db.Connections);

    private async Task<int> AProductAsync() => await db.AProductAsync(stock: 8);

    private async Task<(DateTime? DeletedAt, string? DeletedBy)> ArchiveStateAsync(int productId)
    {
        await using var connection = await db.OpenAsync();
        return await connection.QuerySingleAsync<(DateTime?, string?)>(
            "SELECT DeletedAt, DeletedByUserId FROM dbo.Products WHERE ProductId = @productId",
            new { productId });
    }

    private static ProductListQuery Archived => new() { ArchivedOnly = true, Page = 1, PageSize = 100 };
    private static ProductListQuery Live     => new() { Page = 1, PageSize = 100 };

    // ── archiving ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_product_leaves_the_row_exactly_where_it_was()
    {
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, null);

        await using var connection = await db.OpenAsync();
        var stillThere = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE ProductId = @productId", new { productId });

        Assert.Equal(1, stillThere);
    }

    [Fact]
    public async Task Archiving_records_when_and_by_whom()
    {
        //  "Who removed the Gulaab set, and when?" is a question the shop will
        //  ask, and IsActive alone cannot answer it.
        var userId    = await db.AUserAsync("Owner");
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, userId);

        var (deletedAt, deletedBy) = await ArchiveStateAsync(productId);

        Assert.NotNull(deletedAt);
        Assert.Equal(userId, deletedBy);
    }

    [Fact]
    public async Task An_archived_product_is_off_the_storefront()
    {
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, null);

        var storefront = await Reads.GetProductAsync(productId, null, includeInactive: false);
        Assert.Equal(ResponseCodes.NotFound, storefront.ResponseCode);
    }

    [Fact]
    public async Task An_archived_product_is_out_of_the_panels_ordinary_list()
    {
        var productId = await AProductAsync();
        await Catalog.DeleteProductAsync(productId, null);

        var live = await Reads.GetProductsAsync(Live, includeInactive: true);

        Assert.DoesNotContain(live.Data.Products, p => p.ProductId == productId);
    }

    [Fact]
    public async Task An_archived_product_is_in_the_archive()
    {
        var productId = await AProductAsync();
        await Catalog.DeleteProductAsync(productId, null);

        var archived = await Reads.GetProductsAsync(Archived, includeInactive: true);

        Assert.Contains(archived.Data.Products, p => p.ProductId == productId);
    }

    [Fact]
    public async Task The_archive_says_who_put_each_thing_there()
    {
        var userId    = await db.AUserAsync("Owner");
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, userId);

        var archived = await Reads.GetProductsAsync(Archived, includeInactive: true);
        var row = archived.Data.Products.Single(p => p.ProductId == productId);

        Assert.NotNull(row.DeletedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.DeletedBy));
    }

    [Fact]
    public async Task A_live_product_is_never_in_the_archive()
    {
        var productId = await AProductAsync();

        var archived = await Reads.GetProductsAsync(Archived, includeInactive: true);

        Assert.DoesNotContain(archived.Data.Products, p => p.ProductId == productId);
    }

    [Fact]
    public async Task A_product_switched_off_in_the_editor_is_not_the_same_as_an_archived_one()
    {
        //  Hidden and archived are different states. Only the second offers to
        //  put the product back, and only the second says who removed it.
        var productId = await AProductAsync();

        await using (var connection = await db.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Products SET IsActive = 0 WHERE ProductId = @productId", new { productId });
        }

        var archived = await Reads.GetProductsAsync(Archived, includeInactive: true);
        Assert.DoesNotContain(archived.Data.Products, p => p.ProductId == productId);

        var live = await Reads.GetProductsAsync(Live, includeInactive: true);
        Assert.Contains(live.Data.Products, p => p.ProductId == productId);
    }

    [Fact]
    public async Task Archiving_twice_is_refused_rather_than_rewriting_who_did_it()
    {
        var first  = await db.AUserAsync("Owner");
        var second = await db.AUserAsync("Owner");
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, first);
        var again = await Catalog.DeleteProductAsync(productId, second);

        Assert.Equal(ResponseCodes.Conflict, again.ResponseCode);
        Assert.Equal(first, (await ArchiveStateAsync(productId)).DeletedBy);
    }

    [Fact]
    public async Task Archiving_says_it_can_be_undone()
    {
        var productId = await AProductAsync();

        var archived = await Catalog.DeleteProductAsync(productId, null);

        Assert.Contains("bring it back", archived.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── restoring ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_archived_product_can_be_brought_back()
    {
        var productId = await AProductAsync();
        await Catalog.DeleteProductAsync(productId, null);

        var restored = await Catalog.RestoreProductAsync(productId, null);

        Assert.True(restored.IsSuccess);
    }

    [Fact]
    public async Task A_restored_product_is_back_on_the_storefront()
    {
        var productId = await AProductAsync();
        await Catalog.DeleteProductAsync(productId, null);
        await Catalog.RestoreProductAsync(productId, null);

        var storefront = await Reads.GetProductAsync(productId, null, includeInactive: false);

        Assert.True(storefront.IsSuccess);
    }

    [Fact]
    public async Task A_restored_product_keeps_its_stock_price_and_cloths()
    {
        //  The point of archiving: nothing about the product was thrown away,
        //  so bringing it back is not re-entering it.
        var productId = await db.AProductAsync(stock: 7, price: 9_250);

        var before = (await Reads.GetProductAsync(productId, null, includeInactive: true)).Data!;

        await Catalog.DeleteProductAsync(productId, null);
        await Catalog.RestoreProductAsync(productId, null);

        var after = (await Reads.GetProductAsync(productId, null, includeInactive: true)).Data!;

        Assert.Equal(7, after.Product!.Stock);
        Assert.Equal(9_250, after.Product.Price);
        Assert.Equal(before.Swatches.Count, after.Swatches.Count);
    }

    [Fact]
    public async Task Restoring_clears_the_archive_record()
    {
        var userId    = await db.AUserAsync("Owner");
        var productId = await AProductAsync();

        await Catalog.DeleteProductAsync(productId, userId);
        await Catalog.RestoreProductAsync(productId, userId);

        var (deletedAt, deletedBy) = await ArchiveStateAsync(productId);

        Assert.Null(deletedAt);
        Assert.Null(deletedBy);
    }

    [Fact]
    public async Task A_restored_product_leaves_the_archive()
    {
        var productId = await AProductAsync();
        await Catalog.DeleteProductAsync(productId, null);
        await Catalog.RestoreProductAsync(productId, null);

        var archived = await Reads.GetProductsAsync(Archived, includeInactive: true);

        Assert.DoesNotContain(archived.Data.Products, p => p.ProductId == productId);
    }

    [Fact]
    public async Task Restoring_a_product_that_was_never_archived_is_refused()
    {
        var productId = await AProductAsync();

        var restored = await Catalog.RestoreProductAsync(productId, null);

        Assert.Equal(ResponseCodes.Conflict, restored.ResponseCode);
    }

    [Fact]
    public async Task Restoring_something_that_does_not_exist_is_a_404()
    {
        var restored = await Catalog.RestoreProductAsync(999_999, null);

        Assert.Equal(ResponseCodes.NotFound, restored.ResponseCode);
    }

    [Fact]
    public async Task A_product_cannot_come_back_onto_a_name_something_else_now_uses()
    {
        //  The owner archived "Gulaab Bridal Set" and made a new one under the
        //  same name. Both live would break the unique index — and the shop
        //  would have two products it cannot tell apart.
        await using var connection = await db.OpenAsync();

        var categoryId = await connection.ExecuteScalarAsync<int>(
            "SELECT TOP 1 CategoryId FROM dbo.Categories WHERE IsActive = 1");

        var name = $"Contested Name {Given.Unique()}";
        var created = await Catalog.CreateProductAsync(new ProductEditorRequest
        {
            Name = name, CategoryId = categoryId, Price = 5_000, Stock = 3,
            SwatchIds = [await db.SwatchIdAsync()],
        }, null);

        var productId = created.Data!.Value;
        await Catalog.DeleteProductAsync(productId, null);

        // The replacement takes the name.
        await connection.ExecuteAsync(
            """
            UPDATE dbo.Products SET Name = @name, Slug = @slug
            WHERE  ProductId = (SELECT MAX(ProductId) FROM dbo.Products WHERE ProductId <> @productId)
            """,
            new { name, slug = $"contested-{Given.Unique()}", productId });

        var restored = await Catalog.RestoreProductAsync(productId, null);

        Assert.Equal(ResponseCodes.Conflict, restored.ResponseCode);
        Assert.Contains("name", restored.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_product_cannot_come_back_into_a_category_the_shop_has_closed()
    {
        //  It would sit on the storefront under a section nobody can browse to.
        await using var connection = await db.OpenAsync();

        var categoryId = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO dbo.Categories (Name, Slug, SortOrder, IsActive)
            OUTPUT INSERTED.CategoryId VALUES (@Name, @Slug, 90, 1)
            """,
            new { Name = $"Temporary {Given.Unique()}", Slug = $"temporary-{Given.Unique()}" });

        var created = await Catalog.CreateProductAsync(new ProductEditorRequest
        {
            Name = $"In A Closing Category {Given.Unique()}", CategoryId = categoryId,
            Price = 5_000, Stock = 3, SwatchIds = [await db.SwatchIdAsync()],
        }, null);

        var productId = created.Data!.Value;
        await Catalog.DeleteProductAsync(productId, null);

        await connection.ExecuteAsync(
            "UPDATE dbo.Categories SET IsActive = 0 WHERE CategoryId = @categoryId", new { categoryId });

        var restored = await Catalog.RestoreProductAsync(productId, null);

        Assert.Equal(ResponseCodes.Conflict, restored.ResponseCode);
        Assert.Contains("category", restored.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Archiving_a_product_never_touches_the_orders_that_named_it()
    {
        //  The whole reason this is an archive: a customer's receipt must go on
        //  saying what they bought.
        var productId = await db.AProductAsync(stock: 5);
        var placed    = await new OrderCommandRepository(db.Connections)
            .CreateAsync(Given.AnOrderFor(productId, quantity: 2), Given.NewPhone());

        await Catalog.DeleteProductAsync(productId, null);

        var order = await new OrderQueryRepository(db.Connections)
            .GetByIdAsync(placed.Data!.Order!.OrderId);

        Assert.True(order.IsSuccess);
        Assert.Equal(2, order.Data!.Lines.Single().Quantity);
        Assert.False(string.IsNullOrWhiteSpace(order.Data.Lines.Single().ProductName));
    }
}
