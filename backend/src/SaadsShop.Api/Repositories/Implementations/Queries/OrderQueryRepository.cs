using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;

namespace SaadsShop.Api.Repositories.Implementations.Queries;

public sealed class OrderQueryRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IOrderQueryRepository
{
    public Task<ProcedureResult<OrderWithLines>> GetByReferenceAsync(
        string reference, string normalisedPhone, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.OrderGetByReference,
            new { Reference = reference, Phone = normalisedPhone },
            async grid => new OrderWithLines
            {
                Order = await grid.ReadSingleOrDefaultAsync<Order>(),
                Lines = (await grid.ReadAsync<OrderLine>()).AsList()
            },
            ct);

    public Task<ProcedureResult<(IReadOnlyList<Order> Orders, PageInfo Page)>> SearchAsync(
        OrderSearchQuery query, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.OrderGetList,
            new
            {
                Status   = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                Search   = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                // DateOnly stays a date all the way to SQL Server's DATE
                // parameter — see Data/DateOnlyTypeHandler. Converting to
                // DateTime here would reintroduce a time component and make the
                // inclusive end-of-day handling in the procedure wrong.
                FromDate = query.FromDate,
                ToDate   = query.ToDate,
                query.Page,
                query.PageSize
            },
            async grid =>
            {
                var orders = (await grid.ReadAsync<Order>()).AsList();
                var page   = await grid.ReadSingleAsync<PageInfo>();
                return ((IReadOnlyList<Order>)orders, page);
            },
            ct);

    public Task<ProcedureResult<OrderDetail>> GetByIdAsync(int orderId, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.OrderGetById,
            new { OrderId = orderId },
            async grid => new OrderDetail
            {
                Order        = await grid.ReadSingleOrDefaultAsync<Order>(),
                Lines        = (await grid.ReadAsync<OrderLine>()).AsList(),
                Measurements = (await grid.ReadAsync<OrderMeasurement>()).AsList(),
                History      = (await grid.ReadAsync<OrderStatusChange>()).AsList()
            },
            ct);

    public Task<ProcedureResult<SetBuilderQuote>> QuoteSetAsync(
        SetBuilderQuoteRequest request, CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.SetBuilderQuote,
            new
            {
                request.SheetProductId,
                request.CurtainProductId,
                request.CushionProductId,
                request.BedSize
            },
            async grid =>
            {
                var lines = (await grid.ReadAsync<SetBuilderQuoteLine>()).AsList();
                var total = await grid.ReadSingleAsync<QuoteTotal>();

                return new SetBuilderQuote
                {
                    Lines   = lines,
                    Total   = total.Total,
                    BedSize = total.BedSize
                };
            },
            ct);

    private sealed class QuoteTotal
    {
        public decimal Total   { get; set; }
        public string  BedSize { get; set; } = string.Empty;
    }
}
