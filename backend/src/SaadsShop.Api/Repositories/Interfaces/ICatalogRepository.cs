using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces;

/// <summary>
/// Reads and writes the catalogue. Every method calls exactly one stored
/// procedure and returns whatever it reported, uninterpreted — deciding what a
/// response code means belongs to the service layer.
/// </summary>
public interface ICatalogRepository
{
    Task<ProcedureResult<IReadOnlyList<Category>>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ProcedureResult<IReadOnlyList<Swatch>>>   GetSwatchesAsync(CancellationToken ct = default);
    Task<ProcedureResult<IReadOnlyList<BedSize>>>  GetBedSizesAsync(CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Product> Products, PageInfo Page)>> GetProductsAsync(
        ProductListQuery query, bool includeInactive, CancellationToken ct = default);

    Task<ProcedureResult<ProductWithSwatches>> GetProductAsync(
        int? productId, string? slug, bool includeInactive, CancellationToken ct = default);

    Task<ProcedureResult<int?>> CreateProductAsync(
        ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateProductAsync(
        int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);
}

/// <summary>A product with the cloths it can be made in and a few related items.</summary>
public sealed class ProductWithSwatches
{
    public Product?                 Product  { get; init; }
    public IReadOnlyList<Swatch>    Swatches { get; init; } = [];
    public IReadOnlyList<Product>   Related  { get; init; } = [];
}
