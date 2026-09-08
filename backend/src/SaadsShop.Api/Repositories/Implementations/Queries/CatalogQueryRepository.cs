using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;

namespace SaadsShop.Api.Repositories.Implementations.Queries;

public sealed class CatalogQueryRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), ICatalogQueryRepository
{
    public Task<ProcedureResult<IReadOnlyList<Category>>> GetCategoriesAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<Category>>(
            StoredProcedures.CategoryGetAll,
            parameters: null,
            async grid => (await grid.ReadAsync<Category>()).AsList(),
            ct);

    public Task<ProcedureResult<IReadOnlyList<Swatch>>> GetSwatchesAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<Swatch>>(
            StoredProcedures.SwatchGetAll,
            parameters: null,
            async grid => (await grid.ReadAsync<Swatch>()).AsList(),
            ct);

    public Task<ProcedureResult<IReadOnlyList<BedSize>>> GetBedSizesAsync(CancellationToken ct = default)
        => ExecuteAsync<IReadOnlyList<BedSize>>(
            StoredProcedures.BedSizeGetAll,
            parameters: null,
            async grid => (await grid.ReadAsync<BedSize>()).AsList(),
            ct);

    public Task<ProcedureResult<(IReadOnlyList<Product> Products, PageInfo Page)>> GetProductsAsync(
        ProductListQuery query, bool includeInactive, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductGetList,
            new
            {
                CategorySlug    = string.IsNullOrWhiteSpace(query.Category) ? null : query.Category,
                Search          = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                IncludeInactive = includeInactive,
                SortBy          = query.SortBy,
                Page            = query.Page,
                PageSize        = query.PageSize
            },
            async grid =>
            {
                var products = (await grid.ReadAsync<Product>()).AsList();
                var page     = await grid.ReadSingleAsync<PageInfo>();
                return ((IReadOnlyList<Product>)products, page);
            },
            ct);

    public Task<ProcedureResult<ProductWithSwatches>> GetProductAsync(
        int? productId, string? slug, bool includeInactive, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductGetById,
            new { ProductId = productId, Slug = slug, IncludeInactive = includeInactive },
            async grid => new ProductWithSwatches
            {
                // Order matters and is fixed by the procedure: product, then its
                // cloths, then related items. Reading out of order would not
                // fail loudly — it would silently map the wrong columns.
                Product  = await grid.ReadSingleOrDefaultAsync<Product>(),
                Swatches = (await grid.ReadAsync<Swatch>()).AsList(),
                Related  = (await grid.ReadAsync<Product>()).AsList()
            },
            ct);
}
