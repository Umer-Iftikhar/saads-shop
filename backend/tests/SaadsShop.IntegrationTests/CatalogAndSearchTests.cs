using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Implementations.Commands;
using SaadsShop.Api.Repositories.Implementations.Queries;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// The catalogue's writes, and the order search — including the date range the
/// panel and the API attribute both check, verified here at the third layer.
/// </summary>
[Collection(ShopDatabaseCollection.Name)]
public class CatalogAndSearchTests(ShopDatabase db)
{
    private CatalogCommandRepository Catalog => new(db.Connections);
    private CatalogQueryRepository   Reads   => new(db.Connections);
    private OrderQueryRepository     Orders  => new(db.Connections);

    private async Task<ProductEditorRequest> AnEditorRequestAsync(string? name = null)
    {
        await using var connection = await db.OpenAsync();
        var categoryId = await connection.ExecuteScalarAsync<int>(
            "SELECT TOP 1 CategoryId FROM dbo.Categories WHERE IsActive = 1");

        return new ProductEditorRequest
        {
            Name = name ?? $"Editor Product {Given.Unique()}",
            CategoryId = categoryId,
            Price = 6_500,
            Stock = 12,
            LowStockAt = 4,
            Pieces = "8 pieces",
            SwatchIds = [await db.SwatchIdAsync()],
        };
    }

    // ── writing the catalogue ────────────────────────────────────────────────

    [Fact]
    public async Task A_new_product_gets_a_slug_made_from_its_name()
    {
        var request = await AnEditorRequestAsync("Gulaab Bridal Set " + Given.Unique());

        var created = await Catalog.CreateProductAsync(request, null);
        Assert.True(created.IsSuccess);

        var read = await Reads.GetProductAsync(created.Data, null, includeInactive: true);

        Assert.StartsWith("gulaab-bridal-set-", read.Data!.Product!.Slug);
    }

    [Fact]
    public async Task Two_products_cannot_share_a_name()
    {
        var request = await AnEditorRequestAsync();

        await Catalog.CreateProductAsync(request, null);
        var again = await Catalog.CreateProductAsync(request, null);

        Assert.Equal(ResponseCodes.Conflict, again.ResponseCode);
    }

    [Fact]
    public async Task A_product_in_a_category_that_does_not_exist_is_refused()
    {
        var request = await AnEditorRequestAsync();
        var broken  = new ProductEditorRequest
        {
            Name = request.Name, CategoryId = 999_999, Price = request.Price,
            Stock = request.Stock, SwatchIds = request.SwatchIds,
        };

        var created = await Catalog.CreateProductAsync(broken, null);

        Assert.False(created.IsSuccess);
    }

    [Fact]
    public async Task Editing_replaces_the_whole_set_of_cloths_rather_than_adding_to_it()
    {
        //  The editor sends what it wants, not a diff — so removing a cloth in
        //  the browser has to remove it here.
        var created = await Catalog.CreateProductAsync(await AnEditorRequestAsync(), null);
        var productId = created.Data!.Value;

        var swatches = (await Reads.GetSwatchesAsync()).Data!;
        var two = swatches.Take(2).Select(s => s.SwatchId).ToList();

        var request = await AnEditorRequestAsync();
        await Catalog.UpdateProductAsync(productId, new ProductEditorRequest
        {
            Name = $"Edited {Given.Unique()}", CategoryId = request.CategoryId,
            Price = 7_000, Stock = 5, SwatchIds = two,
        }, null);

        var afterTwo = await Reads.GetProductAsync(productId, null, includeInactive: true);
        Assert.Equal(2, afterTwo.Data!.Swatches.Count);

        await Catalog.UpdateProductAsync(productId, new ProductEditorRequest
        {
            Name = $"Edited again {Given.Unique()}", CategoryId = request.CategoryId,
            Price = 7_000, Stock = 5, SwatchIds = [two[0]],
        }, null);

        var afterOne = await Reads.GetProductAsync(productId, null, includeInactive: true);
        Assert.Single(afterOne.Data!.Swatches);
    }

    [Fact]
    public async Task Editing_a_product_that_is_gone_is_a_404()
    {
        var updated = await Catalog.UpdateProductAsync(999_999, await AnEditorRequestAsync(), null);

        Assert.Equal(ResponseCodes.NotFound, updated.ResponseCode);
    }

    // ── removing a product ───────────────────────────────────────────────────

    [Fact]
    public async Task Removing_a_product_hides_it_from_the_storefront()
    {
        var created   = await Catalog.CreateProductAsync(await AnEditorRequestAsync(), null);
        var productId = created.Data!.Value;

        await Catalog.DeleteProductAsync(productId, null);

        var storefront = await Reads.GetProductAsync(productId, null, includeInactive: false);
        Assert.Equal(ResponseCodes.NotFound, storefront.ResponseCode);
    }

    [Fact]
    public async Task Removing_a_product_never_actually_deletes_the_row()
    {
        //  A hard delete would take the order lines that reference it with it,
        //  and rewrite what customers were charged.
        var created   = await Catalog.CreateProductAsync(await AnEditorRequestAsync(), null);
        var productId = created.Data!.Value;

        await Catalog.DeleteProductAsync(productId, null);

        await using var connection = await db.OpenAsync();
        var stillThere = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE ProductId = @productId", new { productId });

        Assert.Equal(1, stillThere);
    }

    [Fact]
    public async Task A_product_on_past_orders_says_why_it_was_archived_rather_than_deleted()
    {
        var productId = await db.AProductAsync(stock: 5);
        await new OrderCommandRepository(db.Connections)
            .CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());

        var removed = await Catalog.DeleteProductAsync(productId, null);

        Assert.True(removed.IsSuccess);
        Assert.Contains("past orders", removed.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Removing_a_product_that_is_gone_is_a_404()
    {
        var removed = await Catalog.DeleteProductAsync(999_999, null);

        Assert.Equal(ResponseCodes.NotFound, removed.ResponseCode);
    }

    // ── the order search, third layer of the date rules ──────────────────────

    private static OrderSearchQuery Range(DateOnly? from, DateOnly? to) =>
        new() { FromDate = from, ToDate = to, Page = 1, PageSize = 25 };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_sensible_range_is_accepted()
    {
        var found = await Orders.SearchAsync(Range(Today.AddDays(-30), Today));

        Assert.True(found.IsSuccess);
    }

    [Fact]
    public async Task A_range_that_starts_after_it_ends_is_refused_by_the_database_too()
    {
        //  The browser checks this, the [DateRange] attribute checks it, and so
        //  does the procedure — because the procedure is reachable by anything
        //  holding a connection, not only by this API.
        var found = await Orders.SearchAsync(Range(Today, Today.AddDays(-30)));

        Assert.Equal(ResponseCodes.ValidationFailed, found.ResponseCode);
    }

    [Fact]
    public async Task A_range_in_the_future_is_refused_by_the_database_too()
    {
        var found = await Orders.SearchAsync(Range(Today.AddDays(30), Today.AddDays(60)));

        Assert.Equal(ResponseCodes.ValidationFailed, found.ResponseCode);
    }

    [Fact]
    public async Task A_range_longer_than_a_year_is_refused_by_the_database_too()
    {
        var found = await Orders.SearchAsync(Range(Today.AddYears(-3), Today));

        Assert.Equal(ResponseCodes.ValidationFailed, found.ResponseCode);
    }

    [Fact]
    public async Task An_open_range_means_no_date_filter_rather_than_an_error()
    {
        var found = await Orders.SearchAsync(Range(null, null));

        Assert.True(found.IsSuccess);
    }

    [Fact]
    public async Task The_search_finds_an_order_by_its_reference()
    {
        var productId = await db.AProductAsync(stock: 5);
        var placed    = await new OrderCommandRepository(db.Connections)
            .CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());

        var found = await Orders.SearchAsync(new OrderSearchQuery
        {
            Search = placed.Data!.Order!.Reference, Page = 1, PageSize = 25,
        });

        Assert.Equal(placed.Data.Order.OrderId, found.Data.Orders.Single().OrderId);
    }

    [Fact]
    public async Task The_search_finds_an_order_by_the_customers_phone()
    {
        var productId = await db.AProductAsync(stock: 5);
        var phone     = Given.NewPhone();
        await new OrderCommandRepository(db.Connections)
            .CreateAsync(Given.AnOrderFor(productId, phone: phone), phone);

        var found = await Orders.SearchAsync(new OrderSearchQuery
        {
            Search = phone, Page = 1, PageSize = 25,
        });

        Assert.Single(found.Data.Orders);
    }

    [Fact]
    public async Task A_wildcard_typed_into_the_search_box_is_a_character_not_a_pattern()
    {
        //  Someone typing % must not get every order in the shop back.
        var found = await Orders.SearchAsync(new OrderSearchQuery
        {
            Search = "%", Page = 1, PageSize = 25,
        });

        Assert.True(found.IsSuccess);
        Assert.Empty(found.Data.Orders);
    }

    [Fact]
    public async Task Paging_reports_a_total_larger_than_the_page()
    {
        var productId = await db.AProductAsync(stock: 20);

        for (var i = 0; i < 3; i++)
            await new OrderCommandRepository(db.Connections)
                .CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());

        var found = await Orders.SearchAsync(new OrderSearchQuery { Page = 1, PageSize = 2 });

        Assert.Equal(2, found.Data.Orders.Count);
        Assert.True(found.Data.Page.TotalCount > 2);
    }
}
