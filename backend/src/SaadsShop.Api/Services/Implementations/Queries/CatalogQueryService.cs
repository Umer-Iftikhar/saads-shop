using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Queries;

namespace SaadsShop.Api.Services.Implementations.Queries;

/// <summary>
/// The read side of the catalogue — and the only side that caches.
/// </summary>
/// <remarks>
/// Every entry here is keyed by the catalogue's version counter, which the
/// command service bumps after a successful write. Nothing on this side has to
/// know what invalidates what: a write happened, the version moved, and these
/// keys stopped matching.
/// </remarks>
public sealed class CatalogQueryService(
    ICatalogQueryRepository repository,
    ICacheService cache) : ICatalogQueryService
{
    public Task<OperationResult<IReadOnlyList<CategoryResponse>>> GetCategoriesAsync(CancellationToken ct = default)
        => CachedListAsync(
            CacheKeys.Categories(CatalogVersion),
            CacheKeys.Lifetimes.Reference,
            () => repository.GetCategoriesAsync(ct),
            c => new CategoryResponse { CategoryId = c.CategoryId, Name = c.Name, Slug = c.Slug });

    public Task<OperationResult<IReadOnlyList<SwatchResponse>>> GetSwatchesAsync(CancellationToken ct = default)
        => CachedListAsync(
            CacheKeys.Swatches(CatalogVersion),
            CacheKeys.Lifetimes.Reference,
            () => repository.GetSwatchesAsync(ct),
            ToSwatchResponse);

    public Task<OperationResult<IReadOnlyList<BedSizeResponse>>> GetBedSizesAsync(CancellationToken ct = default)
        => CachedListAsync(
            CacheKeys.BedSizes(CatalogVersion),
            CacheKeys.Lifetimes.Reference,
            () => repository.GetBedSizesAsync(ct),
            b => new BedSizeResponse
            {
                BedSizeCode     = b.BedSizeCode,
                Name            = b.Name,
                PriceAdjustment = b.PriceAdjustment
            });

    public async Task<OperationResult<PagedResponse<ProductSummaryResponse>>> GetStorefrontProductsAsync(
        ProductListQuery query, CancellationToken ct = default)
    {
        var key = CacheKeys.ProductList(CatalogVersion, Fingerprint(query, storefront: true));

        var cached = await cache.GetOrCreateAsync(key, CacheKeys.Lifetimes.Catalog, async () =>
        {
            var result = await repository.GetProductsAsync(query, includeInactive: false, ct);

            if (!result.IsSuccess || result.Data.Products is null)
                return OperationResult<PagedResponse<ProductSummaryResponse>>.FromProcedureFailure(result);

            var page = new PagedResponse<ProductSummaryResponse>
            {
                Items      = result.Data.Products.Select(ToSummary).ToList(),
                TotalCount = result.Data.Page.TotalCount,
                Page       = result.Data.Page.Page,
                PageSize   = result.Data.Page.PageSize,
                TotalPages = TotalPages(result.Data.Page)
            };

            return OperationResult<PagedResponse<ProductSummaryResponse>>.Success(page);
        });

        return cached;
    }

    public async Task<OperationResult<ProductDetailResponse>> GetProductAsync(
        int? productId, string? slug, CancellationToken ct = default)
    {
        var key = CacheKeys.Product(CatalogVersion, productId?.ToString() ?? slug ?? "?");

        return await cache.GetOrCreateAsync(key, CacheKeys.Lifetimes.Catalog, async () =>
        {
            var result = await repository.GetProductAsync(productId, slug, includeInactive: false, ct);

            if (!result.IsSuccess || result.Data?.Product is null)
                return OperationResult<ProductDetailResponse>.Failure(
                    result.IsSuccess ? ResponseCodes.NotFound : result.ResponseCode,
                    result.IsSuccess ? "That product is no longer in the shop." : result.ResponseMessage);

            var p = result.Data.Product;

            var detail = new ProductDetailResponse
            {
                ProductId       = p.ProductId,
                Name            = p.Name,
                Slug            = p.Slug,
                CategoryName    = p.CategoryName ?? string.Empty,
                CategorySlug    = p.CategorySlug ?? string.Empty,
                Kicker          = p.Kicker,
                Blurb           = p.Blurb,
                LongDescription = p.LongDescription,
                Price           = p.Price,
                Pieces          = p.Pieces,
                StitchingDays   = p.StitchingDays,
                InStock         = p.Stock > 0,
                DefaultSwatchId = p.DefaultSwatchId,
                Swatches        = result.Data.Swatches.Select(ToSwatchResponse).ToList(),
                Related         = result.Data.Related.Select(ToSummary).ToList()
            };

            return OperationResult<ProductDetailResponse>.Success(detail);
        });
    }

    /// <summary>
    /// The admin listing is deliberately not cached: it shows live stock, and a
    /// shopkeeper who has just adjusted a count and sees the old one will not
    /// trust the screen again.
    /// </summary>
    public async Task<OperationResult<PagedResponse<ProductAdminResponse>>> GetAdminProductsAsync(
        ProductListQuery query, CancellationToken ct = default)
    {
        var result = await repository.GetProductsAsync(query, includeInactive: true, ct);

        if (!result.IsSuccess || result.Data.Products is null)
            return OperationResult<PagedResponse<ProductAdminResponse>>.FromProcedureFailure(result);

        var page = new PagedResponse<ProductAdminResponse>
        {
            Items = result.Data.Products.Select(p => new ProductAdminResponse
            {
                ProductId        = p.ProductId,
                Name             = p.Name,
                Slug             = p.Slug,
                CategoryId       = p.CategoryId,
                CategoryName     = p.CategoryName ?? string.Empty,
                Kicker           = p.Kicker,
                Blurb            = p.Blurb,
                Price            = p.Price,
                Pieces           = p.Pieces,
                StitchingDays    = p.StitchingDays,
                Stock            = p.Stock,
                LowStockAt       = p.LowStockAt,
                SoldCount        = p.SoldCount,
                IsActive         = p.IsActive,
                DefaultSwatchId  = p.DefaultSwatchId,
                SwatchColorValue = p.SwatchColorValue,
                SwatchWeave      = p.SwatchWeave,
                DeletedAt        = p.DeletedAt,
                DeletedBy        = p.DeletedBy
            }).ToList(),
            TotalCount = result.Data.Page.TotalCount,
            Page       = result.Data.Page.Page,
            PageSize   = result.Data.Page.PageSize,
            TotalPages = TotalPages(result.Data.Page)
        };

        return OperationResult<PagedResponse<ProductAdminResponse>>.Success(page);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private long CatalogVersion => cache.GetVersion(CacheKeys.CatalogVersion);

    private async Task<OperationResult<IReadOnlyList<TOut>>> CachedListAsync<TIn, TOut>(
        string key, TimeSpan lifetime,
        Func<Task<ProcedureResult<IReadOnlyList<TIn>>>> fetch,
        Func<TIn, TOut> map)
        => await cache.GetOrCreateAsync(key, lifetime, async () =>
        {
            var result = await fetch();

            return result.IsSuccess && result.Data is not null
                ? OperationResult<IReadOnlyList<TOut>>.Success(result.Data.Select(map).ToList())
                : OperationResult<IReadOnlyList<TOut>>.FromProcedureFailure(result);
        });

    private static int TotalPages(PageInfo page)
        => page.PageSize <= 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize);

    private static SwatchResponse ToSwatchResponse(Swatch s) => new()
    {
        SwatchId   = s.SwatchId,
        Name       = s.Name,
        ColorValue = s.ColorValue,
        Weave      = s.Weave,
        ImagePath  = s.ImagePath
    };

    private static ProductSummaryResponse ToSummary(Product p) => new()
    {
        ProductId        = p.ProductId,
        Name             = p.Name,
        Slug             = p.Slug,
        CategoryName     = p.CategoryName ?? string.Empty,
        Kicker           = p.Kicker,
        Blurb            = p.Blurb,
        Price            = p.Price,
        Pieces           = p.Pieces,
        // The storefront learns whether it can buy, not how many are left.
        InStock          = p.Stock > 0,
        DefaultSwatchId  = p.DefaultSwatchId,
        SwatchColorValue = p.SwatchColorValue,
        SwatchWeave      = p.SwatchWeave
    };

    /// <summary>
    /// Collapses a query into a cache key fragment. Every field that changes the
    /// result must appear, or two different searches would share an entry.
    /// </summary>
    private static string Fingerprint(ProductListQuery q, bool storefront)
        => string.Join('|',
            storefront ? "s" : "a",
            q.Category ?? "-",
            q.Search?.ToLowerInvariant() ?? "-",
            q.SortBy,
            q.Page,
            q.PageSize);
}
