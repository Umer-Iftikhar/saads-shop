using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Repositories.Implementations.Commands;
using SaadsShop.Api.Repositories.Implementations.Queries;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// A product's photograph, through the procedure that stores it.
/// </summary>
/// <remarks>
/// The files themselves are ProductImageService's business and are covered by
/// the unit suite. What is here is the half that needs a database: that the two
/// paths move together, that the previous ones come back so the caller can
/// delete what it replaced, and that a product with no photo reads as one.
/// </remarks>
[Collection(ShopDatabaseCollection.Name)]
public class ProductImageTests(ShopDatabase db)
{
    private CatalogCommandRepository Catalog => new(db.Connections);
    private CatalogQueryRepository   Reads   => new(db.Connections);

    private const string APhoto = "/media/2026-09/abc123.webp";
    private const string AThumb = "/media/2026-09/abc123-thumb.webp";

    [Fact]
    public async Task A_new_product_has_no_photo()
    {
        var productId = await db.AProductAsync();

        var read = await Reads.GetProductAsync(productId, null, includeInactive: true);

        Assert.Null(read.Data!.Product!.ImagePath);
        Assert.Null(read.Data.Product.ThumbnailPath);
    }

    [Fact]
    public async Task Setting_a_photo_stores_both_sizes()
    {
        var productId = await db.AProductAsync();

        var written = await Catalog.SetImageAsync(productId, APhoto, AThumb, null);
        Assert.True(written.IsSuccess);

        var read = await Reads.GetProductAsync(productId, null, includeInactive: true);

        Assert.Equal(APhoto, read.Data!.Product!.ImagePath);
        Assert.Equal(AThumb, read.Data.Product.ThumbnailPath);
    }

    [Fact]
    public async Task Setting_a_photo_returns_nothing_when_there_was_nothing_before()
    {
        var productId = await db.AProductAsync();

        var written = await Catalog.SetImageAsync(productId, APhoto, AThumb, null);

        Assert.Null(written.Data!.ImagePath);
    }

    [Fact]
    public async Task Replacing_a_photo_hands_back_the_one_it_replaced()
    {
        //  This is how the files that are no longer referenced get deleted. If
        //  the procedure did not return them the disk would fill with every
        //  photo the shop ever replaced.
        var productId = await db.AProductAsync();

        await Catalog.SetImageAsync(productId, APhoto, AThumb, null);
        var replaced = await Catalog.SetImageAsync(
            productId, "/media/2026-10/def.webp", "/media/2026-10/def-thumb.webp", null);

        Assert.Equal(APhoto, replaced.Data!.ImagePath);
        Assert.Equal(AThumb, replaced.Data.ThumbnailPath);
    }

    [Fact]
    public async Task Clearing_a_photo_hands_back_what_was_removed()
    {
        var productId = await db.AProductAsync();
        await Catalog.SetImageAsync(productId, APhoto, AThumb, null);

        var cleared = await Catalog.SetImageAsync(productId, null, null, null);

        Assert.Equal(APhoto, cleared.Data!.ImagePath);

        var read = await Reads.GetProductAsync(productId, null, includeInactive: true);
        Assert.Null(read.Data!.Product!.ImagePath);
    }

    [Fact]
    public async Task A_photo_without_its_thumbnail_is_refused()
    {
        //  The cards read the thumbnail and the page reads the full size. One
        //  without the other means the cards fall back to the drawn cloth while
        //  the page shows a photograph.
        var productId = await db.AProductAsync();

        var written = await Catalog.SetImageAsync(productId, APhoto, null, null);

        Assert.Equal(ResponseCodes.ValidationFailed, written.ResponseCode);
    }

    [Fact]
    public async Task A_thumbnail_without_its_photo_is_refused()
    {
        var productId = await db.AProductAsync();

        var written = await Catalog.SetImageAsync(productId, null, AThumb, null);

        Assert.Equal(ResponseCodes.ValidationFailed, written.ResponseCode);
    }

    [Fact]
    public async Task Setting_a_photo_on_a_product_that_does_not_exist_is_a_404()
    {
        var written = await Catalog.SetImageAsync(999_999, APhoto, AThumb, null);

        Assert.Equal(ResponseCodes.NotFound, written.ResponseCode);
    }

    [Fact]
    public async Task The_photo_reaches_the_listing_so_the_cards_can_show_it()
    {
        var productId = await db.AProductAsync();
        await Catalog.SetImageAsync(productId, APhoto, AThumb, null);

        var listed = await Reads.GetProductsAsync(
            new SaadsShop.Api.DTOs.Request.ProductListQuery { Page = 1, PageSize = 100 },
            includeInactive: true);

        var row = listed.Data.Products.Single(p => p.ProductId == productId);
        Assert.Equal(AThumb, row.ThumbnailPath);
    }

    [Fact]
    public async Task Archiving_a_product_leaves_its_photo_alone()
    {
        //  So that bringing it back brings the photograph back with it, rather
        //  than asking the shop to take it again.
        var productId = await db.AProductAsync();
        await Catalog.SetImageAsync(productId, APhoto, AThumb, null);

        await Catalog.DeleteProductAsync(productId, null);
        await Catalog.RestoreProductAsync(productId, null);

        var read = await Reads.GetProductAsync(productId, null, includeInactive: true);
        Assert.Equal(APhoto, read.Data!.Product!.ImagePath);
    }

    [Fact]
    public async Task Setting_a_photo_marks_the_product_as_changed()
    {
        var productId = await db.AProductAsync();

        await Catalog.SetImageAsync(productId, APhoto, AThumb, null);

        await using var connection = await db.OpenAsync();
        var updatedAt = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT UpdatedAt FROM dbo.Products WHERE ProductId = @productId", new { productId });

        Assert.NotNull(updatedAt);
    }
}
