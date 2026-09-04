using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Services.Implementations;

public sealed class OperationsService(
    IOperationsRepository repository,
    ICacheService cache,
    ILogger<OperationsService> logger) : IOperationsService
{
    /// <summary>
    /// The order the board's columns appear in. Fixed here rather than derived
    /// from the data so an empty column still shows — a floor with nothing at
    /// the cutting table should show an empty Cutting column, not skip it.
    /// </summary>
    private static readonly string[] BoardStages =
    [
        nameof(StitchingStage.Measuring),
        nameof(StitchingStage.Cutting),
        nameof(StitchingStage.Stitching),
        nameof(StitchingStage.Ready)
    ];

    public async Task<OperationResult<InventoryResponse>> GetInventoryAsync(
        InventorySearchQuery query, CancellationToken ct = default)
    {
        var result = await repository.GetInventoryAsync(query, ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<InventoryResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        return OperationResult<InventoryResponse>.Success(new InventoryResponse
        {
            Items = result.Data.Items.Select(i => new InventoryRowResponse
            {
                ProductId        = i.ProductId,
                Name             = i.Name,
                CategoryName     = i.CategoryName,
                Price            = i.Price,
                Stock            = i.Stock,
                LowStockAt       = i.LowStockAt,
                StockLabel       = i.StockLabel,
                DefaultSwatchId  = i.DefaultSwatchId,
                SwatchColorValue = i.SwatchColorValue,
                SwatchWeave      = i.SwatchWeave
            }).ToList(),
            ProductCount  = result.Data.ProductCount,
            LowStockCount = result.Data.LowStockCount
        });
    }

    public async Task<OperationResult<StockAdjustedResponse>> AdjustStockAsync(
        int productId, AdjustStockRequest request, string? actorUserId, CancellationToken ct = default)
    {
        // Caught here as well as in the procedure: [Range] permits zero because
        // it has to span negative and positive, so zero needs its own rule.
        if (request.Delta == 0)
        {
            return OperationResult<StockAdjustedResponse>.Invalid(new Dictionary<string, string[]>
            {
                [nameof(AdjustStockRequest.Delta)] = ["Enter how many pieces to add or remove."]
            });
        }

        var result = await repository.AdjustStockAsync(productId, request.Delta, request.Reason, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<StockAdjustedResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        cache.BumpVersion(CacheKeys.CatalogVersion);

        logger.LogInformation(
            "Stock for product {ProductId} adjusted by {Delta} to {Stock} by {ActorUserId}: {Reason}",
            productId, request.Delta, result.Data.Stock, actorUserId, request.Reason);

        return OperationResult<StockAdjustedResponse>.Success(
            new StockAdjustedResponse { ProductId = result.Data.ProductId, Stock = result.Data.Stock },
            result.ResponseMessage);
    }

    public async Task<OperationResult<StitchingBoardResponse>> GetStitchingBoardAsync(CancellationToken ct = default)
    {
        var result = await repository.GetStitchingQueueAsync(ct);

        if (!result.IsSuccess || result.Data is null)
            return OperationResult<StitchingBoardResponse>.Failure(result.ResponseCode, result.ResponseMessage);

        var byStage = result.Data
            .GroupBy(j => j.Stage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var columns = BoardStages.Select(stage =>
        {
            var jobs = byStage.TryGetValue(stage, out var list) ? list : [];

            return new StitchingColumnResponse
            {
                Stage = stage,
                Count = jobs.Count,
                Jobs  = jobs.Select(j => new StitchingJobResponse
                {
                    StitchingJobId   = j.StitchingJobId,
                    OrderId          = j.OrderId,
                    Reference        = j.Reference,
                    Title            = j.Title,
                    Stage            = j.Stage,
                    AssignedTo       = j.AssignedTo,
                    SwatchColorValue = j.SwatchColorValue,
                    SwatchWeave      = j.SwatchWeave,
                    DueDate          = j.DueDate,
                    IsOverdue        = j.IsOverdue
                }).ToList()
            };
        }).ToList();

        return OperationResult<StitchingBoardResponse>.Success(
            new StitchingBoardResponse { Columns = columns });
    }

    public async Task<OperationResult<int>> CreateStitchingJobAsync(
        StitchingJobCreateRequest request, CancellationToken ct = default)
    {
        var result = await repository.CreateStitchingJobAsync(request, ct);

        return result.IsSuccess && result.Data is not null
            ? OperationResult<int>.Success(result.Data.Value, result.ResponseMessage)
            : OperationResult<int>.Failure(result.ResponseCode, result.ResponseMessage);
    }

    public async Task<OperationResult<bool>> UpdateStitchingJobAsync(
        int jobId, StitchingJobUpdateRequest request, CancellationToken ct = default)
    {
        var result = await repository.UpdateStitchingJobAsync(jobId, request, ct);

        return result.IsSuccess
            ? OperationResult<bool>.Success(true, result.ResponseMessage)
            : OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);
    }

    public async Task<OperationResult<PagedResponse<CustomerResponse>>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default)
    {
        var result = await repository.SearchCustomersAsync(query, ct);

        if (!result.IsSuccess || result.Data.Customers is null)
            return OperationResult<PagedResponse<CustomerResponse>>
                .Failure(result.ResponseCode, result.ResponseMessage);

        var customers = result.Data.Customers.AsEnumerable();

        // The date filter is applied here rather than in SQL: it filters on the
        // MAX(PlacedAt) aggregate, which the procedure computes per row after
        // paging. Pushing it down would mean paging over a filtered aggregate
        // and a second, slower query shape for a screen that lists a few
        // hundred customers at most.
        if (query.FromDate is { } from)
            customers = customers.Where(c => c.LastOrderAt is { } d && DateOnly.FromDateTime(d) >= from);

        if (query.ToDate is { } to)
            customers = customers.Where(c => c.LastOrderAt is { } d && DateOnly.FromDateTime(d) <= to);

        var items = customers.Select(c => new CustomerResponse
        {
            CustomerId  = c.CustomerId,
            Name        = c.Name,
            Phone       = c.Phone,
            Area        = c.Area,
            OrderCount  = c.OrderCount,
            TotalSpent  = c.TotalSpent,
            LastOrderAt = c.LastOrderAt
        }).ToList();

        var page = result.Data.Page;

        return OperationResult<PagedResponse<CustomerResponse>>.Success(new PagedResponse<CustomerResponse>
        {
            Items      = items,
            TotalCount = page.TotalCount,
            Page       = page.Page,
            PageSize   = page.PageSize,
            TotalPages = page.PageSize <= 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize)
        });
    }
}
