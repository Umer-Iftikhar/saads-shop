using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Commands;

namespace SaadsShop.Api.Repositories.Implementations.Commands;

public sealed class OrderCommandRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IOrderCommandRepository
{
    public Task<ProcedureResult<OrderWithLines>> CreateAsync(
        PlaceOrderRequest request, string normalisedPhone, CancellationToken ct = default)
    {
        var lines = BuildOrderLinesTable(
            request.Lines.Select(l => (l.ProductId, l.Quantity, l.SwatchId, l.BedSize)));

        return ExecuteAsync(
            StoredProcedures.OrderCreate,
            WithTableParameter(
                new
                {
                    request.CustomerName,
                    Phone = normalisedPhone,
                    request.DeliveryAddress,
                    request.Area,
                    request.PaymentMethod,
                    request.Notes
                },
                "Lines", lines, TableTypes.OrderLine),
            async grid => new OrderWithLines
            {
                Order = await grid.ReadSingleOrDefaultAsync<Order>(),
                Lines = (await grid.ReadAsync<OrderLine>()).AsList()
            },
            ct);
    }

    public Task<ProcedureResult<bool>> UpdateStatusAsync(
        int orderId, string newStatus, string? note, string? actorUserId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.OrderUpdateStatus,
            new { OrderId = orderId, NewStatus = newStatus, Note = note, ActorUserId = actorUserId },
            ct);

    public Task<ProcedureResult<bool>> SaveMeasurementsAsync(
        int orderId, SaveMeasurementsRequest request, string? actorUserId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.OrderSaveMeasurements,
            new
            {
                OrderId = orderId,
                request.BedWidthIn,
                request.BedLengthIn,
                request.WindowDropIn,
                request.WindowCount,
                request.Notes,
                request.TakenBy,
                ActorUserId = actorUserId
            },
            ct);
}
