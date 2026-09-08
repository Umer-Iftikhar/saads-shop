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

    Task<ProcedureResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default);
}
