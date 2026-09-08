using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;

namespace SaadsShop.Api.Repositories.Interfaces.Queries;

/// <summary>Orders, read.</summary>
public interface IOrderQueryRepository
{
    /// <summary>
    /// A customer looking up their own order: the reference alone is not enough,
    /// the phone number on the order must match.
    /// </summary>
    Task<ProcedureResult<OrderWithLines>> GetByReferenceAsync(
        string reference, string normalisedPhone, CancellationToken ct = default);

    Task<ProcedureResult<(IReadOnlyList<Order> Orders, PageInfo Page)>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default);

    Task<ProcedureResult<OrderDetail>> GetByIdAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Prices a set builder combination. A read despite its cost: it writes
    /// nothing, reserves nothing, and the binding price is still the one the
    /// checkout procedure computes under lock.
    /// </summary>
    Task<ProcedureResult<SetBuilderQuote>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default);
}
