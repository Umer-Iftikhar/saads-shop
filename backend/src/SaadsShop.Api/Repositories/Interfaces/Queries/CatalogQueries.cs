using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Queries;

/// <summary>
/// The catalogue's read side. Every method calls exactly one stored procedure
/// and returns whatever it reported, uninterpreted — deciding what a response
/// code means belongs to the service layer.
/// </summary>
/// <remarks>
/// Reads and writes are separate interfaces throughout this project. A screen
/// that only shows the catalogue takes this one and is then incapable of
/// changing it, which is a stronger statement than a comment saying so; and the
/// two sides have genuinely different shapes — reads are cached, paged and
/// projected for a particular audience, writes are single, audited and
/// cache-invalidating.
/// </remarks>
public interface ICatalogQueryRepository
{
    Task<ProcedureResult<IReadOnlyList<Category>>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ProcedureResult<IReadOnlyList<Swatch>>>   GetSwatchesAsync(CancellationToken ct = default);
    Task<ProcedureResult<IReadOnlyList<BedSize>>>  GetBedSizesAsync(CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Product> Products, PageInfo Page)>> GetProductsAsync(
        ProductListQuery query, bool includeInactive, CancellationToken ct = default);

    Task<ProcedureResult<ProductWithSwatches>> GetProductAsync(
        int? productId, string? slug, bool includeInactive, CancellationToken ct = default);
}
