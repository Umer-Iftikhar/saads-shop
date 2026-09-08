using NSubstitute;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Implementations.Commands;

namespace SaadsShop.UnitTests.Services;

public class CatalogCommandServiceTests
{
    private readonly ICatalogCommandRepository _repository = Substitute.For<ICatalogCommandRepository>();
    private readonly FakeCache _cache = new();

    private CatalogCommandService Service
        => new(_repository, _cache, Given.Log<CatalogCommandService>());

    private static ProductEditorRequest AProduct() => new()
    {
        Name = "Gulaab Bridal Set", CategoryId = 1, Price = 18_500m,
        StitchingDays = 3, Stock = 4, LowStockAt = 2, SwatchIds = [1, 2], IsActive = true
    };

    [Fact]
    public async Task Creating_a_product_returns_its_id_and_invalidates_the_catalogue()
    {
        _repository.CreateProductAsync(Arg.Any<ProductEditorRequest>(), "saad", Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<int?>(42));

        var result = await Service.CreateProductAsync(AProduct(), "saad");

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_refused_create_leaves_the_cache_alone()
    {
        _repository.CreateProductAsync(Arg.Any<ProductEditorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<int?>(409, "A product with that name already exists."));

        var result = await Service.CreateProductAsync(AProduct(), "saad");

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("A product with that name already exists.", result.Message);

        // Bumping on failure would evict a perfectly good cache for nothing.
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_procedure_that_reports_success_but_returns_no_id_is_treated_as_a_failure()
    {
        _repository.CreateProductAsync(Arg.Any<ProductEditorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<int?>(null));

        var result = await Service.CreateProductAsync(AProduct(), "saad");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Updating_a_product_invalidates_the_catalogue()
    {
        _repository.UpdateProductAsync(1, Arg.Any<ProductEditorRequest>(), "saad", Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        Assert.True((await Service.UpdateProductAsync(1, AProduct(), "saad")).IsSuccess);
        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Deleting_a_product_invalidates_the_catalogue()
    {
        _repository.DeleteProductAsync(1, "saad", Arg.Any<CancellationToken>()).Returns(Given.Ok(true));

        Assert.True((await Service.DeleteProductAsync(1, "saad")).IsSuccess);
        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_refused_delete_leaves_the_cache_alone()
    {
        _repository.DeleteProductAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "That product is on past orders."));

        Assert.False((await Service.DeleteProductAsync(1, "saad")).IsSuccess);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Restoring_a_product_invalidates_the_catalogue()
    {
        //  The storefront cached the catalogue without this product in it, so a
        //  restore that skipped the bump would put it back everywhere except
        //  where customers look.
        _repository.RestoreProductAsync(1, "saad", Arg.Any<CancellationToken>()).Returns(Given.Ok(true));

        Assert.True((await Service.RestoreProductAsync(1, "saad")).IsSuccess);
        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_refused_restore_leaves_the_cache_alone()
    {
        _repository.RestoreProductAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "Another product is using that name now."));

        var result = await Service.RestoreProductAsync(1, "saad");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_refused_restore_passes_the_reason_through_for_the_shopkeeper()
    {
        _repository.RestoreProductAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "That product's category is no longer in the shop."));

        var result = await Service.RestoreProductAsync(1, "saad");

        Assert.Equal("That product's category is no longer in the shop.", result.Message);
    }

    [Fact]
    public async Task Restoring_records_who_did_it()
    {
        _repository.RestoreProductAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        await Service.RestoreProductAsync(7, "user-99");

        await _repository.Received(1).RestoreProductAsync(7, "user-99", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_acting_user_reaches_the_procedure_so_the_audit_row_has_a_name_on_it()
    {
        _repository.CreateProductAsync(Arg.Any<ProductEditorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<int?>(1));

        await Service.CreateProductAsync(AProduct(), "user-99");

        await _repository.Received(1).CreateProductAsync(
            Arg.Any<ProductEditorRequest>(), "user-99", Arg.Any<CancellationToken>());
    }
}

public class OrderCommandServiceTests
{
    private readonly IOrderCommandRepository _repository = Substitute.For<IOrderCommandRepository>();
    private readonly FakeCache _cache = new();

    private OrderCommandService Service
        => new(_repository, _cache, Given.Log<OrderCommandService>());

    private static PlaceOrderRequest AnOrder(string phone = "0301 234 5678") => new()
    {
        CustomerName    = "Hina Aslam",
        Phone           = phone,
        DeliveryAddress = "House 4, Street 7, Satellite Town",
        PaymentMethod   = "CashOnDelivery",
        Lines           = [new CartLineRequest { ProductId = 1, Quantity = 1 }]
    };

    [Fact]
    public async Task A_phone_number_is_normalised_before_it_reaches_the_database()
    {
        _repository.CreateAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines { Order = Given.AnOrder(), Lines = [] }));

        await Service.PlaceOrderAsync(AnOrder("+92 301 234 5678"));

        await _repository.Received(1)
            .CreateAsync(Arg.Any<PlaceOrderRequest>(), "03012345678", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unusable_phone_number_is_a_field_error_and_never_reaches_the_database()
    {
        var result = await Service.PlaceOrderAsync(AnOrder("12345"));

        Assert.Equal(400, result.ResponseCode);
        Assert.Contains(nameof(PlaceOrderRequest.Phone), result.Errors!.Keys);

        await _repository.DidNotReceiveWithAnyArgs()
            .CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_placed_order_invalidates_the_catalogue_because_stock_moved()
    {
        _repository.CreateAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines { Order = Given.AnOrder(), Lines = [] }));

        await Service.PlaceOrderAsync(AnOrder());

        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task An_order_refused_for_stock_leaves_the_catalogue_cache_alone()
    {
        _repository.CreateAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<OrderWithLines>(409, "Compact Chhata just went out of stock."));

        var result = await Service.PlaceOrderAsync(AnOrder());

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("Compact Chhata just went out of stock.", result.Message);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task The_confirmation_carries_what_the_database_charged_not_what_was_asked_for()
    {
        var priced = Given.AnOrder(total: 22_000m);
        priced.Subtotal = 21_700m;
        priced.DeliveryCharge = 300m;

        _repository.CreateAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines
                   {
                       Order = priced,
                       Lines = [new OrderLine { OrderLineId = 1, ProductId = 1, ProductName = "Gulaab", UnitPrice = 21_700m, Quantity = 1, LineTotal = 21_700m }]
                   }));

        var result = await Service.PlaceOrderAsync(AnOrder());

        Assert.Equal(22_000m, result.Value!.Total);
        Assert.Equal(21_700m, Assert.Single(result.Value.Lines).UnitPrice);
    }

    [Fact]
    public async Task Cancelling_an_order_returns_stock_so_the_catalogue_is_invalidated()
    {
        _repository.UpdateStatusAsync(1, "Cancelled", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        await Service.UpdateStatusAsync(1, new UpdateOrderStatusRequest { Status = "Cancelled" }, "saad");

        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Theory]
    [InlineData("Measuring")]
    [InlineData("Stitching")]
    [InlineData("Ready")]
    [InlineData("Delivered")]
    public async Task Any_other_status_move_leaves_stock_and_the_cache_untouched(string status)
    {
        _repository.UpdateStatusAsync(1, status, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        await Service.UpdateStatusAsync(1, new UpdateOrderStatusRequest { Status = status }, "saad");

        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task A_refused_cancellation_does_not_invalidate_anything()
    {
        _repository.UpdateStatusAsync(1, "Cancelled", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "A delivered order cannot be reopened."));

        var result = await Service.UpdateStatusAsync(1, new UpdateOrderStatusRequest { Status = "Cancelled" }, "saad");

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Measurements_pass_the_procedures_message_back_up()
    {
        _repository.SaveMeasurementsAsync(1, Arg.Any<SaveMeasurementsRequest>(), "saad", Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true, "Measurements saved."));

        var result = await Service.SaveMeasurementsAsync(1, new SaveMeasurementsRequest { BedWidthIn = 60 }, "saad");

        Assert.True(result.IsSuccess);
        Assert.Equal("Measurements saved.", result.Message);
    }

    [Fact]
    public async Task A_refused_measurement_carries_its_code()
    {
        _repository.SaveMeasurementsAsync(1, Arg.Any<SaveMeasurementsRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(404, "No such order."));

        Assert.Equal(404, (await Service.SaveMeasurementsAsync(1, new SaveMeasurementsRequest(), "saad")).ResponseCode);
    }
}

public class OperationsCommandServiceTests
{
    private readonly IOperationsCommandRepository _repository = Substitute.For<IOperationsCommandRepository>();
    private readonly FakeCache _cache = new();

    private OperationsCommandService Service
        => new(_repository, _cache, Given.Log<OperationsCommandService>());

    [Fact]
    public async Task A_zero_adjustment_is_refused_before_it_reaches_the_database()
    {
        var result = await Service.AdjustStockAsync(
            1, new AdjustStockRequest { Delta = 0, Reason = "Stocktake" }, "saad");

        Assert.Equal(400, result.ResponseCode);
        Assert.Contains(nameof(AdjustStockRequest.Delta), result.Errors!.Keys);

        await _repository.DidNotReceiveWithAnyArgs()
            .AdjustStockAsync(default, default, default!, default, default);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(-3)]
    public async Task A_signed_adjustment_reaches_the_database_and_invalidates_the_catalogue(int delta)
    {
        _repository.AdjustStockAsync(1, delta, "New cloth from the mill", "saad", Arg.Any<CancellationToken>())
                   .Returns(Given.Ok((1, 16)));

        var result = await Service.AdjustStockAsync(
            1, new AdjustStockRequest { Delta = delta, Reason = "New cloth from the mill" }, "saad");

        Assert.True(result.IsSuccess);
        Assert.Equal(16, result.Value!.Stock);
        Assert.Equal(1, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task An_adjustment_the_database_refuses_leaves_the_cache_alone()
    {
        _repository.AdjustStockAsync(1, -99, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<(int, int)>(409, "There are only 4 in stock."));

        var result = await Service.AdjustStockAsync(
            1, new AdjustStockRequest { Delta = -99, Reason = "Damaged" }, "saad");

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Creating_a_stitching_job_returns_its_id()
    {
        _repository.CreateStitchingJobAsync(Arg.Any<StitchingJobCreateRequest>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<int?>(7));

        var result = await Service.CreateStitchingJobAsync(
            new StitchingJobCreateRequest { OrderId = 1, Title = "Bridal bedding" });

        Assert.Equal(7, result.Value);
    }

    [Fact]
    public async Task A_job_the_procedure_declines_to_number_is_a_failure()
    {
        _repository.CreateStitchingJobAsync(Arg.Any<StitchingJobCreateRequest>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<int?>(null));

        Assert.False((await Service.CreateStitchingJobAsync(new StitchingJobCreateRequest())).IsSuccess);
    }

    [Fact]
    public async Task Moving_a_job_along_passes_the_procedures_verdict_through()
    {
        _repository.UpdateStitchingJobAsync(3, Arg.Any<StitchingJobUpdateRequest>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "That stage move is not allowed."));

        var result = await Service.UpdateStitchingJobAsync(3, new StitchingJobUpdateRequest { Stage = "Done" });

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("That stage move is not allowed.", result.Message);
    }
}

public class ShopCommandServiceTests
{
    private readonly IShopCommandRepository _repository = Substitute.For<IShopCommandRepository>();
    private readonly FakeCache _cache = new();

    private ShopCommandService Service
        => new(_repository, _cache, Given.Log<ShopCommandService>());

    private static SettingsUpdateRequest ASettingsUpdate(string whatsApp = "0301 234 5678") => new()
    {
        ShopName = "Saad's Shop", City = "Rawalpindi", AddressLine = "Shop 14, Moti Bazaar",
        WhatsAppNumber = whatsApp, DeliveryCharge = 300m, FreeDeliveryThreshold = 5_000m,
        CashOnDeliveryEnabled = true
    };

    [Fact]
    public async Task The_whatsapp_number_is_normalised_before_it_is_stored()
    {
        _repository.UpdateSettingsAsync(Arg.Any<SettingsUpdateRequest>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        await Service.UpdateSettingsAsync(ASettingsUpdate("+92 301 234 5678"), "saad");

        await _repository.Received(1).UpdateSettingsAsync(
            Arg.Any<SettingsUpdateRequest>(), "03012345678", "saad", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unusable_whatsapp_number_is_a_field_error()
    {
        var result = await Service.UpdateSettingsAsync(ASettingsUpdate("not a number"), "saad");

        Assert.Equal(400, result.ResponseCode);
        Assert.Contains(nameof(SettingsUpdateRequest.WhatsAppNumber), result.Errors!.Keys);

        await _repository.DidNotReceiveWithAnyArgs()
            .UpdateSettingsAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Saved_settings_invalidate_the_public_settings_cache()
    {
        _repository.UpdateSettingsAsync(Arg.Any<SettingsUpdateRequest>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(true));

        await Service.UpdateSettingsAsync(ASettingsUpdate(), "saad");

        Assert.Equal(1, _cache.BumpCount(CacheKeys.SettingsVersion));
        // The catalogue is a different family and must not be disturbed.
        Assert.Equal(0, _cache.BumpCount(CacheKeys.CatalogVersion));
    }

    [Fact]
    public async Task Settings_the_database_refuses_do_not_invalidate_the_cache()
    {
        _repository.UpdateSettingsAsync(Arg.Any<SettingsUpdateRequest>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<bool>(409, "Leave at least one way for customers to pay."));

        var result = await Service.UpdateSettingsAsync(ASettingsUpdate(), "saad");

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(0, _cache.BumpCount(CacheKeys.SettingsVersion));
    }
}
