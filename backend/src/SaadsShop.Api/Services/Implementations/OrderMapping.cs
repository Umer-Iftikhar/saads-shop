using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Services.Implementations;

/// <summary>
/// Order projections shared by the read and write sides.
/// </summary>
/// <remarks>
/// Placing an order returns the same lines that looking one up does, so the
/// mapping sits here rather than being copied into both services — two copies
/// of a projection drift apart one field at a time, and the field that goes
/// missing is always the one somebody needed.
/// </remarks>
internal static class OrderMapping
{
    public static OrderLineResponse ToLineResponse(OrderLine l) => new()
    {
        OrderLineId      = l.OrderLineId,
        ProductId        = l.ProductId,
        ProductName      = l.ProductName,
        SwatchName       = l.SwatchName,
        SwatchColorValue = l.SwatchColorValue,
        SwatchWeave      = l.SwatchWeave,
        BedSize          = l.BedSize,
        UnitPrice        = l.UnitPrice,
        Quantity         = l.Quantity,
        LineTotal        = l.LineTotal
    };

    public static OrderSummaryResponse ToSummaryResponse(Order o) => new()
    {
        OrderId       = o.OrderId,
        Reference     = o.Reference,
        PlacedAt      = o.PlacedAt,
        CustomerName  = o.CustomerName,
        Phone         = o.Phone,
        ItemSummary   = o.ItemSummary,
        LineCount     = o.LineCount,
        Total         = o.Total,
        PaymentMethod = o.PaymentMethod,
        Status        = o.Status
    };
}
