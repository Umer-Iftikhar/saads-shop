using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;

namespace SaadsShop.Api.Repositories.Interfaces.Commands;

/// <summary>Orders, written.</summary>
public interface IOrderCommandRepository
{
    /// <summary>
    /// Places an order. Everything that matters — stock check, pricing, totals,
    /// the delivery charge — happens inside the procedure's transaction under
    /// row locks, so nothing here needs to guard against a race.
    /// </summary>
    Task<ProcedureResult<OrderWithLines>> CreateAsync(
        PlaceOrderRequest request, string normalisedPhone, CancellationToken ct = default);

    Task<ProcedureResult<bool>> UpdateStatusAsync(
        int orderId, string newStatus, string? note, string? actorUserId, CancellationToken ct = default);

    Task<ProcedureResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default);
}
