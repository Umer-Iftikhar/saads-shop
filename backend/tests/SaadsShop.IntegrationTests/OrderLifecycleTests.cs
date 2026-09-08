using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Implementations.Commands;
using SaadsShop.Api.Repositories.Implementations.Queries;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// An order from the counter to delivery, through the real procedures.
/// </summary>
[Collection(ShopDatabaseCollection.Name)]
public class OrderLifecycleTests(ShopDatabase db)
{
    private OrderCommandRepository Orders => new(db.Connections);
    private OrderQueryRepository   Reads  => new(db.Connections);

    private async Task<(int OrderId, string Reference, string Phone)> APlacedOrderAsync(int stock = 10)
    {
        var productId = await db.AProductAsync(stock);
        var phone     = Given.NewPhone();
        var placed    = await Orders.CreateAsync(Given.AnOrderFor(productId, phone: phone), phone);

        Assert.True(placed.IsSuccess);
        return (placed.Data!.Order!.OrderId, placed.Data.Order.Reference, phone);
    }

    // ── placing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_order_gets_a_reference_a_customer_can_read_out()
    {
        var (_, reference, _) = await APlacedOrderAsync();

        Assert.StartsWith("SS-", reference);
    }

    [Fact]
    public async Task Every_order_gets_its_own_reference()
    {
        var productId = await db.AProductAsync(stock: 50);

        var references = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                var placed = await Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());
                return placed.Data!.Order!.Reference;
            }));

        Assert.Equal(10, references.Distinct().Count());
    }

    [Fact]
    public async Task The_price_charged_is_the_shops_price_not_one_sent_by_the_browser()
    {
        // The request carries no prices at all; the procedure looks them up.
        var productId = await db.AProductAsync(stock: 5, price: 12_345);

        var placed = await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 2), Given.NewPhone());

        Assert.Equal(12_345, placed.Data!.Lines.Single().UnitPrice);
        Assert.Equal(24_690, placed.Data.Order!.Subtotal);
    }

    [Fact]
    public async Task An_order_line_keeps_the_name_and_price_it_was_sold_at()
    {
        //  An order is a historical record. Renaming the product next season
        //  must not rewrite what this customer bought.
        var productId = await db.AProductAsync(stock: 5, price: 4_000);
        var placed    = await Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());

        await using (var connection = await db.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Products SET Name = N'Renamed Later', Price = 9999 WHERE ProductId = @productId",
                new { productId });
        }

        var read = await Reads.GetByIdAsync(placed.Data!.Order!.OrderId);
        var line = read.Data!.Lines.Single();

        Assert.DoesNotContain("Renamed Later", line.ProductName);
        Assert.Equal(4_000, line.UnitPrice);
    }

    [Fact]
    public async Task An_order_for_a_product_that_does_not_exist_is_refused()
    {
        var result = await Orders.CreateAsync(Given.AnOrderFor(productId: 999_999), Given.NewPhone());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task An_order_for_a_delisted_product_is_refused()
    {
        var productId = await db.AProductAsync(stock: 10);

        await using (var connection = await db.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Products SET IsActive = 0 WHERE ProductId = @productId", new { productId });
        }

        var result = await Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone());

        Assert.False(result.IsSuccess);
    }

    // ── moving it along ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_order_starts_as_Placed()
    {
        var (orderId, _, _) = await APlacedOrderAsync();

        var read = await Reads.GetByIdAsync(orderId);

        Assert.Equal("Placed", read.Data!.Order!.Status);
    }

    [Theory]
    [InlineData("Measuring")]
    [InlineData("Stitching")]
    [InlineData("Ready")]
    [InlineData("Delivered")]
    public async Task The_shop_can_move_an_order_forward(string status)
    {
        var (orderId, _, _) = await APlacedOrderAsync();

        var moved = await Orders.UpdateStatusAsync(orderId, status, null, null);

        Assert.True(moved.IsSuccess);
        Assert.Equal(status, (await Reads.GetByIdAsync(orderId)).Data!.Order!.Status);
    }

    [Fact]
    public async Task A_status_that_is_not_one_of_the_shops_is_refused()
    {
        var (orderId, _, _) = await APlacedOrderAsync();

        var moved = await Orders.UpdateStatusAsync(orderId, "Posted", null, null);

        Assert.False(moved.IsSuccess);
    }

    [Fact]
    public async Task Moving_an_order_that_does_not_exist_is_a_404()
    {
        var moved = await Orders.UpdateStatusAsync(999_999, "Ready", null, null);

        Assert.Equal(ResponseCodes.NotFound, moved.ResponseCode);
    }

    [Fact]
    public async Task A_delivered_order_cannot_be_reopened()
    {
        var (orderId, _, _) = await APlacedOrderAsync();
        await Orders.UpdateStatusAsync(orderId, "Delivered", null, null);

        var reopened = await Orders.UpdateStatusAsync(orderId, "Stitching", null, null);

        Assert.False(reopened.IsSuccess);
    }

    [Fact]
    public async Task Every_move_is_written_down_with_who_made_it()
    {
        var userId = await db.AUserAsync();
        var (orderId, _, _) = await APlacedOrderAsync();

        await Orders.UpdateStatusAsync(orderId, "Measuring", "Taking measurements Thursday", userId);

        var history = (await Reads.GetByIdAsync(orderId)).Data!.History;
        var latest  = history.First();

        Assert.Equal("Measuring", latest.ToStatus);
        Assert.Equal("Taking measurements Thursday", latest.Note);
    }

    // ── measurements ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Measurements_are_saved_against_the_order()
    {
        var (orderId, _, _) = await APlacedOrderAsync();

        var saved = await Orders.SaveMeasurementsAsync(orderId, new SaveMeasurementsRequest
        {
            BedWidthIn = 60, BedLengthIn = 78, WindowDropIn = 90, WindowCount = 2,
            Notes = "Corner window is narrower", TakenBy = "Nadia",
        }, null);

        Assert.True(saved.IsSuccess);

        var measurement = (await Reads.GetByIdAsync(orderId)).Data!.Measurements.Single();
        Assert.Equal(60, measurement.BedWidthIn);
        Assert.Equal(2,  measurement.WindowCount);
    }

    [Fact]
    public async Task Measuring_again_keeps_both_readings()
    {
        // The tailor re-measured; the first reading is still evidence of what
        // the job was quoted from.
        var (orderId, _, _) = await APlacedOrderAsync();

        await Orders.SaveMeasurementsAsync(orderId, new SaveMeasurementsRequest { BedWidthIn = 60 }, null);
        await Orders.SaveMeasurementsAsync(orderId, new SaveMeasurementsRequest { BedWidthIn = 62 }, null);

        var measurements = (await Reads.GetByIdAsync(orderId)).Data!.Measurements;

        Assert.Equal(2, measurements.Count);
        Assert.Equal(62, measurements.First().BedWidthIn);   // newest first
    }

    // ── the customer looking their own order up ──────────────────────────────

    [Fact]
    public async Task A_customer_finds_their_order_with_the_reference_and_their_phone()
    {
        var (_, reference, phone) = await APlacedOrderAsync();

        var tracked = await Reads.GetByReferenceAsync(reference, phone);

        Assert.True(tracked.IsSuccess);
        Assert.Equal(reference, tracked.Data!.Order!.Reference);
    }

    [Fact]
    public async Task The_reference_alone_is_not_enough_to_read_someone_elses_order()
    {
        //  References are sequential — SS-2419 sits next to SS-2418 — so
        //  without the phone anyone could walk the shop's whole order book.
        var (_, reference, _) = await APlacedOrderAsync();

        var tracked = await Reads.GetByReferenceAsync(reference, Given.NewPhone());

        Assert.False(tracked.IsSuccess);
        Assert.Equal(ResponseCodes.NotFound, tracked.ResponseCode);
    }

    [Fact]
    public async Task A_wrong_phone_and_a_wrong_reference_fail_the_same_way()
    {
        // Otherwise the difference tells an attacker which references exist.
        var (_, reference, _) = await APlacedOrderAsync();

        var wrongPhone     = await Reads.GetByReferenceAsync(reference, Given.NewPhone());
        var wrongReference = await Reads.GetByReferenceAsync("SS-000000", Given.NewPhone());

        Assert.Equal(wrongReference.ResponseCode, wrongPhone.ResponseCode);
        Assert.Equal(wrongReference.ResponseMessage, wrongPhone.ResponseMessage);
    }
}
