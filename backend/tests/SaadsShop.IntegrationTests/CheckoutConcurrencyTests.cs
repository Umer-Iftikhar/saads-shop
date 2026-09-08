using Dapper;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Repositories.Implementations.Commands;
using SaadsShop.Api.Repositories.Implementations.Queries;

namespace SaadsShop.IntegrationTests;

/// <summary>
/// The last piece of cloth, and several people trying to buy it at once.
/// </summary>
/// <remarks>
/// This is the check <c>database/README.md</c> describes and that was until now
/// run by hand. It cannot be written as a unit test: what it exercises is
/// SQL Server's locking under <c>usp_Order_Create</c>'s transaction, and a
/// substitute has no locks to take.
/// </remarks>
[Collection(ShopDatabaseCollection.Name)]
public class CheckoutConcurrencyTests(ShopDatabase db)
{
    private OrderCommandRepository Orders => new(db.Connections);

    [Fact]
    public async Task Eight_people_race_for_one_piece_and_exactly_one_gets_it()
    {
        var productId = await db.AProductAsync(stock: 1);

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone()));

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(7, results.Count(r => r.ResponseCode == ResponseCodes.Conflict));
    }

    [Fact]
    public async Task Stock_never_goes_below_zero_however_hard_it_is_pushed()
    {
        // Twenty buyers, five pieces. Overselling here is money the shop does
        // not have in cloth, and a customer told "yes" who will be told "no".
        var productId = await db.AProductAsync(stock: 5);

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone())));

        Assert.Equal(0, await db.StockOfAsync(productId));
    }

    [Fact]
    public async Task Sells_exactly_the_stock_there_was_and_no_more()
    {
        var productId = await db.AProductAsync(stock: 5);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Orders.CreateAsync(Given.AnOrderFor(productId), Given.NewPhone())));

        Assert.Equal(5, results.Count(r => r.IsSuccess));
    }

    [Fact]
    public async Task A_multi_piece_order_takes_all_its_pieces_or_none()
    {
        // Three left, four wanted: the whole order fails rather than shipping
        // three and quietly dropping one.
        var productId = await db.AProductAsync(stock: 3);

        var result = await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 4), Given.NewPhone());

        Assert.Equal(ResponseCodes.Conflict, result.ResponseCode);
        Assert.Equal(3, await db.StockOfAsync(productId));
    }

    [Fact]
    public async Task An_order_that_fails_on_its_second_item_leaves_the_first_ones_stock_alone()
    {
        var plenty = await db.AProductAsync(stock: 10);
        var none   = await db.AProductAsync(stock: 0);

        var request = Given.AnOrderFor(plenty);
        var twoItems = new SaadsShop.Api.DTOs.Request.PlaceOrderRequest
        {
            CustomerName    = request.CustomerName,
            Phone           = request.Phone,
            DeliveryAddress = request.DeliveryAddress,
            Area            = request.Area,
            PaymentMethod   = request.PaymentMethod,
            Lines =
            [
                new SaadsShop.Api.DTOs.Request.CartLineRequest { ProductId = plenty, Quantity = 1 },
                new SaadsShop.Api.DTOs.Request.CartLineRequest { ProductId = none,   Quantity = 1 },
            ],
        };

        var result = await Orders.CreateAsync(twoItems, Given.NewPhone());

        Assert.False(result.IsSuccess);
        // The transaction rolled back, so the first product is untouched.
        Assert.Equal(10, await db.StockOfAsync(plenty));
    }

    [Fact]
    public async Task Two_orders_from_the_same_phone_become_one_customer_not_two()
    {
        // Placed at the same instant, which is where a get-or-create races.
        var productId = await db.AProductAsync(stock: 10);
        var phone     = Given.NewPhone();

        await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Orders.CreateAsync(Given.AnOrderFor(productId, phone: phone), phone)));

        await using var connection = await db.OpenAsync();
        var customers = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Customers WHERE Phone = @phone", new { phone });

        Assert.Equal(1, customers);
    }

    [Fact]
    public async Task Every_sale_leaves_a_movement_behind_that_explains_the_stock()
    {
        var productId = await db.AProductAsync(stock: 10);

        await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 3), Given.NewPhone());

        await using var connection = await db.OpenAsync();
        var movement = await connection.QuerySingleAsync<(int Delta, int ResultingStock)>(
            """
            SELECT TOP 1 Delta, ResultingStock
            FROM   dbo.InventoryAdjustments
            WHERE  ProductId = @productId
            ORDER BY InventoryAdjustmentId DESC
            """,
            new { productId });

        Assert.Equal(-3, movement.Delta);
        Assert.Equal(7, movement.ResultingStock);
    }

    [Fact]
    public async Task Cancelling_an_order_puts_the_cloth_back_on_the_shelf()
    {
        var productId = await db.AProductAsync(stock: 10);
        var placed    = await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 4), Given.NewPhone());

        Assert.True(placed.IsSuccess);
        Assert.Equal(6, await db.StockOfAsync(productId));

        await Orders.UpdateStatusAsync(placed.Data!.Order!.OrderId, "Cancelled", "Customer changed their mind", null);

        Assert.Equal(10, await db.StockOfAsync(productId));
    }

    [Fact]
    public async Task Cancelling_twice_does_not_return_the_stock_twice()
    {
        var productId = await db.AProductAsync(stock: 10);
        var placed    = await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 4), Given.NewPhone());
        var orderId   = placed.Data!.Order!.OrderId;

        await Orders.UpdateStatusAsync(orderId, "Cancelled", null, null);
        await Orders.UpdateStatusAsync(orderId, "Cancelled", null, null);

        Assert.Equal(10, await db.StockOfAsync(productId));
    }

    [Fact]
    public async Task An_order_reserves_stock_the_moment_it_is_placed()
    {
        var productId = await db.AProductAsync(stock: 10);

        await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 2), Given.NewPhone());

        Assert.Equal(8, await db.StockOfAsync(productId));
    }

    [Fact]
    public async Task The_order_that_wins_can_be_read_back_whole()
    {
        var productId = await db.AProductAsync(stock: 4, price: 7_500);
        var placed    = await Orders.CreateAsync(Given.AnOrderFor(productId, quantity: 2), Given.NewPhone());

        var reads = new OrderQueryRepository(db.Connections);
        var read  = await reads.GetByIdAsync(placed.Data!.Order!.OrderId);

        Assert.True(read.IsSuccess);
        Assert.Equal(placed.Data.Order.Reference, read.Data!.Order!.Reference);
        Assert.Equal(15_000, read.Data.Lines.Single().LineTotal);
    }
}
