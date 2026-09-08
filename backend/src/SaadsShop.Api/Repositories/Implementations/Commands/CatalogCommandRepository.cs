using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Interfaces.Commands;

namespace SaadsShop.Api.Repositories.Implementations.Commands;

public sealed class CatalogCommandRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), ICatalogCommandRepository
{
    public Task<ProcedureResult<int?>> CreateProductAsync(
        ProductEditorRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var swatches = BuildIntListTable(request.SwatchIds);

        return ExecuteAsync<int?>(
            StoredProcedures.ProductCreate,
            WithTableParameter(
                new
                {
                    request.Name,
                    request.CategoryId,
                    request.Price,
                    request.Kicker,
                    request.Blurb,
                    request.LongDescription,
                    request.Pieces,
                    request.StitchingDays,
                    request.Stock,
                    request.LowStockAt,
                    request.DefaultSwatchId,
                    ActorUserId = actorUserId
                },
                "SwatchIds", swatches, TableTypes.IntList),
            async grid =>
            {
                var created = await grid.ReadSingleOrDefaultAsync<CreatedProduct>();
                return created?.ProductId;
            },
            ct);
    }

    public Task<ProcedureResult<bool>> UpdateProductAsync(
        int productId, ProductEditorRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var swatches = BuildIntListTable(request.SwatchIds);

        return ExecuteAsync(
            StoredProcedures.ProductUpdate,
            WithTableParameter(
                new
                {
                    ProductId = productId,
                    request.Name,
                    request.CategoryId,
                    request.Price,
                    request.Kicker,
                    request.Blurb,
                    request.LongDescription,
                    request.Pieces,
                    request.StitchingDays,
                    request.LowStockAt,
                    request.DefaultSwatchId,
                    request.IsActive,
                    ActorUserId = actorUserId
                },
                "SwatchIds", swatches, TableTypes.IntList),
            ct);
    }

    public Task<ProcedureResult<bool>> DeleteProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductDelete,
            new { ProductId = productId, ActorUserId = actorUserId },
            ct);

    public Task<ProcedureResult<bool>> RestoreProductAsync(
        int productId, string? actorUserId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductRestore,
            new { ProductId = productId, ActorUserId = actorUserId },
            ct);

    public Task<ProcedureResult<ReplacedImage>> SetImageAsync(
        int productId, string? imagePath, string? thumbnailPath, string? actorUserId,
        CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.ProductSetImage,
            new
            {
                ProductId     = productId,
                ImagePath     = imagePath,
                ThumbnailPath = thumbnailPath,
                ActorUserId   = actorUserId
            },
            //  What was there before, so the caller can delete the files it
            //  just replaced rather than leaving them on disk forever.
            async grid => await grid.ReadSingleOrDefaultAsync<ReplacedImage>() ?? new ReplacedImage(),
            ct);

    private sealed class CreatedProduct
    {
        public int?    ProductId { get; set; }
        public string? Slug      { get; set; }
    }
}
