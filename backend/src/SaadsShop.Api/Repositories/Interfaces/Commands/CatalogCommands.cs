using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.Api.Repositories.Interfaces.Commands;

/// <summary>
/// The catalogue's write side. Each call is one audited stored procedure, and
/// every one of them takes the acting user — a product that changed price with
/// nobody's name against it is a question the shop cannot answer later.
/// </summary>
public interface ICatalogCommandRepository
{
    Task<ProcedureResult<int?>> CreateProductAsync(
        ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateProductAsync(
        int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Archives a product. Nothing here deletes one: an order line references
    /// it, and removing the row would rewrite what a customer was charged.
    /// </summary>
    Task<ProcedureResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);

    /// <summary>Puts an archived product back in the shop, exactly as it was.</summary>
    Task<ProcedureResult<bool>> RestoreProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Points a product at a stored photograph, or clears it when both paths are
    /// null. Returns whatever was there before, so the files it replaced can be
    /// removed from disk.
    /// </summary>
    Task<ProcedureResult<ReplacedImage>> SetImageAsync(
        int productId, string? imagePath, string? thumbnailPath, string? actorUserId,
        CancellationToken ct = default);
}

/// <summary>The photo paths a product carried before this write.</summary>
public sealed class ReplacedImage
{
    public string? ImagePath     { get; set; }
    public string? ThumbnailPath { get; set; }
}
