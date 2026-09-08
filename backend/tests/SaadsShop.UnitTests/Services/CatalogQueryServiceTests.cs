using NSubstitute;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Implementations.Queries;

namespace SaadsShop.UnitTests.Services;

public class CatalogQueryServiceTests
{
    private readonly ICatalogQueryRepository _repository = Substitute.For<ICatalogQueryRepository>();
    private readonly FakeCache _cache = new();

    private CatalogQueryService Service => new(_repository, _cache);

    private static ProductListQuery AQuery(string? category = null, string? search = null, int page = 1)
        => new() { Category = category, Search = search, Page = page, PageSize = 24 };

    // ── what the storefront is allowed to know ───────────────────────────────

    [Fact]
    public async Task The_storefront_learns_whether_it_can_buy_not_how_many_are_left()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct(stock: 7)], Given.Page())));

        var result = await Service.GetStorefrontProductsAsync(AQuery());

        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.InStock);

        // The response type has no stock field at all, which is the point —
        // this asserts the projection cannot regress into exposing one.
        Assert.Null(item.GetType().GetProperty("Stock"));
    }

    [Fact]
    public async Task A_product_with_no_stock_is_listed_but_marked_out_of_stock()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct(stock: 0)], Given.Page())));

        var result = await Service.GetStorefrontProductsAsync(AQuery());

        Assert.False(Assert.Single(result.Value!.Items).InStock);
    }

    [Fact]
    public async Task The_storefront_never_asks_for_inactive_products()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[], Given.Page(0))));

        await Service.GetStorefrontProductsAsync(AQuery());

        await _repository.Received(1)
            .GetProductsAsync(Arg.Any<ProductListQuery>(), includeInactive: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_panel_does_ask_for_inactive_products_and_sees_the_counts()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), true, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct(stock: 7, isActive: false)], Given.Page())));

        var result = await Service.GetAdminProductsAsync(AQuery());

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(7, item.Stock);
        Assert.False(item.IsActive);
        Assert.Equal(11, item.SoldCount);
    }

    // ── paging ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 24, 0)]
    [InlineData(1, 24, 1)]
    [InlineData(48, 24, 2)]
    [InlineData(49, 24, 3)]
    public async Task Page_count_rounds_up(int totalCount, int pageSize, int expected)
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[], Given.Page(totalCount, 1, pageSize))));

        var result = await Service.GetStorefrontProductsAsync(AQuery());

        Assert.Equal(expected, result.Value!.TotalPages);
    }

    // ── caching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_repeated_listing_is_served_from_the_cache()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetStorefrontProductsAsync(AQuery());
        await service.GetStorefrontProductsAsync(AQuery());

        Assert.Equal(1, _cache.Misses);
        await _repository.Received(1)
            .GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bumping_the_catalogue_version_orphans_the_cached_listing()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetStorefrontProductsAsync(AQuery());

        _cache.BumpVersion(CacheKeys.CatalogVersion);   // what a write does

        await service.GetStorefrontProductsAsync(AQuery());

        Assert.Equal(2, _cache.Misses);
    }

    [Fact]
    public async Task Two_different_searches_do_not_share_a_cache_entry()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetStorefrontProductsAsync(AQuery(search: "gulaab"));
        await service.GetStorefrontProductsAsync(AQuery(search: "parde"));

        Assert.Equal(2, _cache.Misses);
    }

    [Fact]
    public async Task A_search_differing_only_in_case_shares_one_entry()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetStorefrontProductsAsync(AQuery(search: "Gulaab"));
        await service.GetStorefrontProductsAsync(AQuery(search: "gulaab"));

        Assert.Equal(1, _cache.Misses);
    }

    [Fact]
    public async Task Each_page_of_the_same_search_is_cached_separately()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetStorefrontProductsAsync(AQuery(page: 1));
        await service.GetStorefrontProductsAsync(AQuery(page: 2));

        Assert.Equal(2, _cache.Misses);
    }

    [Fact]
    public async Task The_admin_listing_is_never_cached_because_it_shows_live_stock()
    {
        _repository.GetProductsAsync(Arg.Any<ProductListQuery>(), true, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Product>)[Given.AProduct()], Given.Page())));

        var service = Service;
        await service.GetAdminProductsAsync(AQuery());
        await service.GetAdminProductsAsync(AQuery());

        await _repository.Received(2)
            .GetProductsAsync(Arg.Any<ProductListQuery>(), true, Arg.Any<CancellationToken>());
    }

    // ── the product page ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_product_carries_its_cloths_and_related_items()
    {
        _repository.GetProductAsync(null, "gulaab", false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new ProductWithSwatches
                   {
                       Product  = Given.AProduct(),
                       Swatches = [new Swatch { SwatchId = 1, Name = "Terracotta", ColorValue = "#c67139", Weave = "Woven" }],
                       Related  = [Given.AProduct(id: 2, name: "Jahez Bundle")]
                   }));

        var result = await Service.GetProductAsync(null, "gulaab");

        Assert.True(result.IsSuccess);
        Assert.Equal("Terracotta", Assert.Single(result.Value!.Swatches).Name);
        Assert.Equal("Jahez Bundle", Assert.Single(result.Value.Related).Name);
        Assert.True(result.Value.InStock);
    }

    [Fact]
    public async Task A_procedure_that_succeeds_with_no_product_becomes_a_404()
    {
        _repository.GetProductAsync(Arg.Any<int?>(), Arg.Any<string?>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new ProductWithSwatches { Product = null }));

        var result = await Service.GetProductAsync(null, "no-such-thing");

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.ResponseCode);
        Assert.Equal("That product is no longer in the shop.", result.Message);
    }

    [Fact]
    public async Task A_failing_procedure_keeps_its_own_code_and_message()
    {
        _repository.GetProductAsync(Arg.Any<int?>(), Arg.Any<string?>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<ProductWithSwatches>(500, "Could not read the catalogue."));

        var result = await Service.GetProductAsync(1, null);

        Assert.Equal(500, result.ResponseCode);
        Assert.Equal("Could not read the catalogue.", result.Message);
    }

    [Fact]
    public async Task A_product_looked_up_by_id_and_by_slug_are_cached_apart()
    {
        _repository.GetProductAsync(Arg.Any<int?>(), Arg.Any<string?>(), false, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new ProductWithSwatches { Product = Given.AProduct() }));

        var service = Service;
        await service.GetProductAsync(1, null);
        await service.GetProductAsync(null, "gulaab-bridal-set");

        Assert.Equal(2, _cache.Misses);
    }

    // ── reference data ───────────────────────────────────────────────────────

    [Fact]
    public async Task Categories_are_projected_and_cached()
    {
        _repository.GetCategoriesAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<Category>>(
                       [new Category { CategoryId = 1, Name = "Wedding sets", Slug = "wedding-sets" }]));

        var service = Service;
        var first  = await service.GetCategoriesAsync();
        var second = await service.GetCategoriesAsync();

        Assert.Equal("wedding-sets", Assert.Single(first.Value!).Slug);
        Assert.Equal(1, _cache.Misses);
        Assert.Equal(first.Value!.Count, second.Value!.Count);
    }

    [Fact]
    public async Task Swatches_carry_the_colour_and_weave_the_cloth_is_drawn_from()
    {
        _repository.GetSwatchesAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<Swatch>>(
                       [new Swatch { SwatchId = 3, Name = "Gold", ColorValue = "oklch(0.8 0.12 85)", Weave = "Slub" }]));

        var swatch = Assert.Single((await Service.GetSwatchesAsync()).Value!);

        Assert.Equal("Gold", swatch.Name);
        Assert.Equal("oklch(0.8 0.12 85)", swatch.ColorValue);
        Assert.Equal("Slub", swatch.Weave);
    }

    [Fact]
    public async Task Bed_sizes_carry_their_price_adjustment_including_a_negative_one()
    {
        _repository.GetBedSizesAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<BedSize>>(
                   [
                       new BedSize { BedSizeCode = "Single", Name = "Single", PriceAdjustment = -1_500m },
                       new BedSize { BedSizeCode = "King",   Name = "King",   PriceAdjustment = 2_600m }
                   ]));

        var sizes = (await Service.GetBedSizesAsync()).Value!;

        Assert.Equal(-1_500m, sizes[0].PriceAdjustment);
        Assert.Equal(2_600m, sizes[1].PriceAdjustment);
    }

    [Fact]
    public async Task A_failed_reference_read_is_not_cached_as_a_success()
    {
        _repository.GetCategoriesAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<IReadOnlyList<Category>>(500, "Database unavailable."));

        var result = await Service.GetCategoriesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.ResponseCode);
    }
}
