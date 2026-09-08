using NSubstitute;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Repositories.Interfaces.Queries;
using SaadsShop.Api.Services.Implementations.Queries;

namespace SaadsShop.UnitTests.Services;

public class OrderQueryServiceTests
{
    private readonly IOrderQueryRepository _repository = Substitute.For<IOrderQueryRepository>();
    private OrderQueryService Service => new(_repository);

    // ── tracking, and the oracle it must not become ──────────────────────────

    [Fact]
    public async Task An_order_is_found_by_reference_and_the_phone_it_was_placed_with()
    {
        _repository.GetByReferenceAsync("SS-2419", "03012345678", Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines { Order = Given.AnOrder(), Lines = [] }));

        var result = await Service.TrackAsync(new TrackOrderQuery
        {
            Reference = "SS-2419", Phone = "0301 234 5678"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("SS-2419", result.Value!.Reference);
    }

    [Fact]
    public async Task A_malformed_phone_gets_the_same_answer_as_an_unknown_order()
    {
        _repository.GetByReferenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<OrderWithLines>(404, "We could not find that order."));

        var unknown   = await Service.TrackAsync(new TrackOrderQuery { Reference = "SS-9999", Phone = "03012345678" });
        var malformed = await Service.TrackAsync(new TrackOrderQuery { Reference = "SS-2419", Phone = "nonsense" });

        // Identical code AND identical message: any difference would let someone
        // discover which references exist by varying the phone.
        Assert.Equal(unknown.ResponseCode, malformed.ResponseCode);
        Assert.Equal(unknown.Message, malformed.Message);
        Assert.Equal(404, malformed.ResponseCode);
    }

    [Fact]
    public async Task A_malformed_phone_never_reaches_the_database()
    {
        await Service.TrackAsync(new TrackOrderQuery { Reference = "SS-2419", Phone = "12" });

        await _repository.DidNotReceiveWithAnyArgs()
            .GetByReferenceAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_procedure_that_succeeds_with_no_order_is_still_a_not_found()
    {
        _repository.GetByReferenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines { Order = null }));

        var result = await Service.TrackAsync(new TrackOrderQuery { Reference = "SS-1", Phone = "03012345678" });

        Assert.Equal(404, result.ResponseCode);
    }

    [Fact]
    public async Task Tracking_does_not_reveal_the_delivery_address()
    {
        _repository.GetByReferenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderWithLines { Order = Given.AnOrder(), Lines = [] }));

        var result = await Service.TrackAsync(new TrackOrderQuery { Reference = "SS-2419", Phone = "03012345678" });

        Assert.Null(result.Value!.GetType().GetProperty("DeliveryAddress"));
    }

    // ── the panel's search ───────────────────────────────────────────────────

    [Fact]
    public async Task The_order_list_carries_the_needs_attention_count_from_the_database()
    {
        _repository.SearchAsync(Arg.Any<OrderSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Order>)[Given.AnOrder()], Given.Page(1, 1, 25, needsAttention: 3))));

        var result = await Service.SearchAsync(new OrderSearchQuery());

        Assert.Equal(3, result.Value!.NeedsAttentionCount);
    }

    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(50, 25, 2)]
    [InlineData(51, 25, 3)]
    [InlineData(10, 0, 0)]
    public async Task Order_paging_rounds_up_and_survives_a_zero_page_size(int total, int size, int expected)
    {
        _repository.SearchAsync(Arg.Any<OrderSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Order>)[], Given.Page(total, 1, size))));

        Assert.Equal(expected, (await Service.SearchAsync(new OrderSearchQuery())).Value!.TotalPages);
    }

    [Fact]
    public async Task An_order_detail_that_no_longer_exists_reads_as_a_404_not_a_200()
    {
        _repository.GetByIdAsync(99, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderDetail { Order = null }));

        var result = await Service.GetAsync(99);

        Assert.Equal(404, result.ResponseCode);
        Assert.Equal("That order no longer exists.", result.Message);
    }

    [Fact]
    public async Task An_order_detail_carries_its_measurements_and_history()
    {
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new OrderDetail
                   {
                       Order        = Given.AnOrder(),
                       Lines        = [new OrderLine { OrderLineId = 1, ProductName = "Gulaab", Quantity = 1 }],
                       Measurements = [new OrderMeasurement { BedWidthIn = 60, BedLengthIn = 78, TakenBy = "Nasir", TakenAt = DateTime.UtcNow }],
                       History      = [new OrderStatusChange { FromStatus = "Placed", ToStatus = "Measuring", ChangedAt = DateTime.UtcNow }]
                   }));

        var result = await Service.GetAsync(1);

        Assert.Equal(60, Assert.Single(result.Value!.Measurements).BedWidthIn);
        Assert.Equal("Measuring", Assert.Single(result.Value.History).ToStatus);
    }

    // ── the set builder ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_quote_reports_the_servers_price_and_each_slots_stock()
    {
        _repository.QuoteSetAsync(Arg.Any<SetBuilderQuoteRequest>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new SetBuilderQuote
                   {
                       BedSize = "King",
                       Total   = 10_000m,
                       Lines =
                       [
                           new SetBuilderQuoteLine { Slot = "Bistar", ProductId = 1, ProductName = "Sheet", UnitPrice = 6_000m, InStock = true },
                           new SetBuilderQuoteLine { Slot = "Parde",  ProductId = 2, ProductName = "Curtains", UnitPrice = 4_000m, InStock = false }
                       ]
                   }));

        var result = await Service.QuoteSetAsync(new SetBuilderQuoteRequest { BedSize = "King" });

        Assert.Equal(10_000m, result.Value!.Total);
        Assert.Equal("King", result.Value.BedSize);
        Assert.False(result.Value.Lines[1].InStock);
    }

    [Fact]
    public async Task A_quote_the_procedure_refuses_keeps_its_message()
    {
        _repository.QuoteSetAsync(Arg.Any<SetBuilderQuoteRequest>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Failed<SetBuilderQuote>(400, "An umbrella cannot be the parde."));

        var result = await Service.QuoteSetAsync(new SetBuilderQuoteRequest());

        Assert.Equal(400, result.ResponseCode);
        Assert.Equal("An umbrella cannot be the parde.", result.Message);
    }
}

public class OperationsQueryServiceTests
{
    private readonly IOperationsQueryRepository _repository = Substitute.For<IOperationsQueryRepository>();
    private OperationsQueryService Service => new(_repository);

    private static StitchingJob AJob(int id, string stage, bool overdue = false) => new()
    {
        StitchingJobId = id, OrderId = 1, Reference = $"SS-24{id}",
        Title = "Bridal bedding", Stage = stage, IsOverdue = overdue
    };

    // ── the board ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_board_shows_four_columns_in_a_fixed_order_even_when_some_are_empty()
    {
        _repository.GetStitchingQueueAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StitchingJob>>([AJob(1, "Stitching")]));

        var board = (await Service.GetStitchingBoardAsync()).Value!;

        Assert.Equal(["Measuring", "Cutting", "Stitching", "Ready"], board.Columns.Select(c => c.Stage));
        Assert.Equal(0, board.Columns[0].Count);
        Assert.Equal(1, board.Columns[2].Count);
    }

    [Fact]
    public async Task An_empty_floor_still_shows_every_column()
    {
        _repository.GetStitchingQueueAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StitchingJob>>([]));

        var board = (await Service.GetStitchingBoardAsync()).Value!;

        Assert.Equal(4, board.Columns.Count);
        Assert.All(board.Columns, c => Assert.Empty(c.Jobs));
    }

    [Fact]
    public async Task Jobs_are_grouped_into_their_stage_and_counted()
    {
        _repository.GetStitchingQueueAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StitchingJob>>(
                       [AJob(1, "Cutting"), AJob(2, "Cutting"), AJob(3, "Ready", overdue: true)]));

        var board = (await Service.GetStitchingBoardAsync()).Value!;

        Assert.Equal(2, board.Columns.Single(c => c.Stage == "Cutting").Count);
        Assert.True(board.Columns.Single(c => c.Stage == "Ready").Jobs[0].IsOverdue);
    }

    [Fact]
    public async Task A_job_in_a_stage_the_board_does_not_show_is_left_off_rather_than_creating_a_column()
    {
        _repository.GetStitchingQueueAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StitchingJob>>([AJob(1, "Done"), AJob(2, "Ready")]));

        var board = (await Service.GetStitchingBoardAsync()).Value!;

        Assert.Equal(4, board.Columns.Count);
        Assert.DoesNotContain(board.Columns, c => c.Stage == "Done");
        Assert.Equal(1, board.Columns.Sum(c => c.Count));
    }

    // ── inventory ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inventory_carries_the_databases_stock_label_rather_than_recomputing_one()
    {
        _repository.GetInventoryAsync(Arg.Any<InventorySearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(new InventorySnapshot
                   {
                       Items = [new InventoryItem { ProductId = 1, Name = "Chhata", Stock = 1, LowStockAt = 6, StockLabel = "Low — reorder", CategoryName = "Umbrellas", Price = 900m }],
                       ProductCount = 12,
                       LowStockCount = 3
                   }));

        var result = await Service.GetInventoryAsync(new InventorySearchQuery());

        Assert.Equal("Low — reorder", Assert.Single(result.Value!.Items).StockLabel);
        Assert.Equal(12, result.Value.ProductCount);
        Assert.Equal(3, result.Value.LowStockCount);
    }

    // ── customers, and the date filter applied in the service ────────────────

    private static Customer ACustomer(int id, DateTime? lastOrder) => new()
    {
        CustomerId = id, Name = $"Customer {id}", Phone = "03012345678",
        OrderCount = 2, TotalSpent = 30_000m, LastOrderAt = lastOrder
    };

    [Fact]
    public async Task Customers_last_ordering_before_the_start_date_are_filtered_out()
    {
        _repository.SearchCustomersAsync(Arg.Any<CustomerSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Customer>)
                   [
                       ACustomer(1, new DateTime(2026, 1, 10)),
                       ACustomer(2, new DateTime(2025, 1, 10))
                   ], Given.Page(2))));

        var result = await Service.SearchCustomersAsync(new CustomerSearchQuery
        {
            FromDate = new DateOnly(2026, 1, 1)
        });

        Assert.Equal(1, Assert.Single(result.Value!.Items).CustomerId);
    }

    [Fact]
    public async Task Customers_last_ordering_after_the_end_date_are_filtered_out()
    {
        _repository.SearchCustomersAsync(Arg.Any<CustomerSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Customer>)
                   [
                       ACustomer(1, new DateTime(2026, 6, 10)),
                       ACustomer(2, new DateTime(2026, 1, 10))
                   ], Given.Page(2))));

        var result = await Service.SearchCustomersAsync(new CustomerSearchQuery
        {
            ToDate = new DateOnly(2026, 3, 1)
        });

        Assert.Equal(2, Assert.Single(result.Value!.Items).CustomerId);
    }

    [Fact]
    public async Task The_range_is_inclusive_at_both_ends()
    {
        _repository.SearchCustomersAsync(Arg.Any<CustomerSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Customer>)
                   [
                       ACustomer(1, new DateTime(2026, 1, 1)),
                       ACustomer(2, new DateTime(2026, 3, 1))
                   ], Given.Page(2))));

        var result = await Service.SearchCustomersAsync(new CustomerSearchQuery
        {
            FromDate = new DateOnly(2026, 1, 1),
            ToDate   = new DateOnly(2026, 3, 1)
        });

        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task A_customer_who_has_never_ordered_is_excluded_by_any_date_filter()
    {
        _repository.SearchCustomersAsync(Arg.Any<CustomerSearchQuery>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(((IReadOnlyList<Customer>)[ACustomer(1, null)], Given.Page(1))));

        var filtered = await Service.SearchCustomersAsync(new CustomerSearchQuery
        {
            FromDate = new DateOnly(2020, 1, 1)
        });
        var unfiltered = await Service.SearchCustomersAsync(new CustomerSearchQuery());

        Assert.Empty(filtered.Value!.Items);
        Assert.Single(unfiltered.Value!.Items);
    }
}

public class ShopQueryServiceTests
{
    private readonly IShopQueryRepository _repository = Substitute.For<IShopQueryRepository>();
    private readonly FakeCache _cache = new();

    private ShopQueryService Service => new(_repository, _cache);

    private static ShopSettings Settings(
        bool cod = true, bool whatsApp = true, bool reserve = true, bool card = false) => new()
    {
        ShopName = "Saad's Shop", City = "Rawalpindi", AddressLine = "Shop 14, Moti Bazaar",
        WhatsAppNumber = "03012345678", DeliveryCharge = 300m, FreeDeliveryThreshold = 5_000m,
        CashOnDeliveryEnabled = cod, WhatsAppOrdersEnabled = whatsApp,
        ReserveInShopEnabled = reserve, CardPaymentEnabled = card
    };

    // ── what the storefront may know about payment ───────────────────────────

    [Fact]
    public async Task Only_the_payment_methods_actually_switched_on_reach_the_storefront()
    {
        _repository.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(Given.Ok(Settings()));

        var result = await Service.GetPublicSettingsAsync();

        Assert.Equal(["CashOnDelivery", "WhatsApp", "ReserveInShop"], result.Value!.PaymentMethods);
        Assert.DoesNotContain("Card", result.Value.PaymentMethods);
    }

    [Fact]
    public async Task A_shop_with_only_cash_on_delivery_offers_only_that()
    {
        _repository.GetPublicSettingsAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(Settings(whatsApp: false, reserve: false)));

        Assert.Equal(["CashOnDelivery"], (await Service.GetPublicSettingsAsync()).Value!.PaymentMethods);
    }

    [Fact]
    public async Task The_public_settings_carry_no_flags_for_a_stale_page_to_misread()
    {
        _repository.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(Given.Ok(Settings()));

        var response = (await Service.GetPublicSettingsAsync()).Value!;

        Assert.Null(response.GetType().GetProperty("CashOnDeliveryEnabled"));
        Assert.Null(response.GetType().GetProperty("CardPaymentEnabled"));
    }

    [Fact]
    public async Task The_owners_settings_do_carry_the_flags()
    {
        _repository.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Given.Ok(Settings(card: true)));

        var response = (await Service.GetSettingsAsync()).Value!;

        Assert.True(response.CardPaymentEnabled);
        Assert.True(response.CashOnDeliveryEnabled);
    }

    [Fact]
    public async Task Public_settings_are_cached_until_the_version_moves()
    {
        _repository.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(Given.Ok(Settings()));

        var service = Service;
        await service.GetPublicSettingsAsync();
        await service.GetPublicSettingsAsync();
        Assert.Equal(1, _cache.Misses);

        _cache.BumpVersion(SaadsShop.Api.Constants.CacheKeys.SettingsVersion);
        await service.GetPublicSettingsAsync();
        Assert.Equal(2, _cache.Misses);
    }

    [Fact]
    public async Task The_owners_settings_are_never_cached()
    {
        _repository.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Given.Ok(Settings()));

        var service = Service;
        await service.GetSettingsAsync();
        await service.GetSettingsAsync();

        await _repository.Received(2).GetSettingsAsync(Arg.Any<CancellationToken>());
    }

    // ── the dashboard's one piece of arithmetic ──────────────────────────────

    private static DashboardData Dashboard(decimal today, decimal lastWeek) => new()
    {
        Stats = new DashboardStats
        {
            SalesToday = today, SalesSameDayLastWeek = lastWeek,
            OrdersOpen = 4, OrdersAwaitingMeasurements = 2, JobsOnFloor = 6,
            JobsDueTomorrow = 1, MonthToDateSales = 250_000m
        },
        SalesChart   = [new SalesWeek { WeekStart = new DateTime(2026, 8, 31), Sales = 40_000m, OrderCount = 6 }],
        BestSellers  = [new BestSeller { ProductId = 1, Name = "Gulaab", SoldCount = 11, Revenue = 200_000m }],
        LatestOrders = [Given.AnOrder()]
    };

    [Theory]
    [InlineData(120, 100, 20.0)]
    [InlineData(50,  100, -50.0)]
    [InlineData(100, 100, 0.0)]
    [InlineData(133, 100, 33.0)]
    public async Task The_week_on_week_change_is_a_percentage(decimal today, decimal lastWeek, double expected)
    {
        _repository.GetDashboardAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(Dashboard(today, lastWeek)));

        var result = await Service.GetDashboardAsync(new DashboardQuery());

        Assert.Equal((decimal)expected, result.Value!.Stats.SalesChangePercent);
    }

    [Fact]
    public async Task A_week_with_no_sales_gives_null_rather_than_dividing_by_zero()
    {
        _repository.GetDashboardAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(Dashboard(today: 5_000m, lastWeek: 0m)));

        var result = await Service.GetDashboardAsync(new DashboardQuery());

        Assert.Null(result.Value!.Stats.SalesChangePercent);
    }

    [Fact]
    public async Task The_change_is_rounded_to_one_decimal_place()
    {
        _repository.GetDashboardAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(Dashboard(today: 1_000m, lastWeek: 3_000m)));

        Assert.Equal(-66.7m, (await Service.GetDashboardAsync(new DashboardQuery())).Value!.Stats.SalesChangePercent);
    }

    [Fact]
    public async Task Two_different_days_are_cached_apart()
    {
        _repository.GetDashboardAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok(Dashboard(1m, 1m)));

        var service = Service;
        await service.GetDashboardAsync(new DashboardQuery { AsAt = new DateOnly(2026, 9, 1) });
        await service.GetDashboardAsync(new DashboardQuery { AsAt = new DateOnly(2026, 9, 2) });

        Assert.Equal(2, _cache.Misses);
    }
}

public class AuthQueryServiceTests
{
    private readonly IIdentityQueryRepository _repository = Substitute.For<IIdentityQueryRepository>();
    private AuthQueryService Service => new(_repository);

    [Fact]
    public async Task The_current_user_carries_their_roles_and_2fa_state()
    {
        _repository.FindUserAsync("user-1", null, null, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<AppUser?>(Given.AUser(roles: ["Owner", "Staff"])));

        var result = await Service.GetCurrentUserAsync("user-1");

        Assert.Equal(["Owner", "Staff"], result.Value!.Roles);
        Assert.True(result.Value.TwoFactorEnabled);
    }

    [Fact]
    public async Task The_current_user_response_carries_no_password_hash()
    {
        _repository.FindUserAsync("user-1", null, null, Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<AppUser?>(Given.AUser()));

        var response = (await Service.GetCurrentUserAsync("user-1")).Value!;

        Assert.Null(response.GetType().GetProperty("PasswordHash"));
        Assert.Null(response.GetType().GetProperty("SecurityStamp"));
    }

    [Fact]
    public async Task An_unknown_user_is_a_404()
    {
        _repository.FindUserAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<AppUser?>(null));

        var result = await Service.GetCurrentUserAsync("nobody");

        Assert.Equal(404, result.ResponseCode);
    }

    [Fact]
    public async Task A_staff_list_splits_the_comma_joined_roles()
    {
        _repository.GetStaffAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StaffAccount>>(
                   [
                       new StaffAccount { Id = "1", FullName = "Saad", Email = "saad@saadsshop.pk", Roles = "Owner, Staff", IsActive = true }
                   ]));

        var staff = Assert.Single((await Service.GetStaffAsync()).Value!);

        Assert.Equal(["Owner", "Staff"], staff.Roles);
    }

    [Fact]
    public async Task A_staff_member_with_no_roles_gets_an_empty_list_not_a_blank_entry()
    {
        _repository.GetStaffAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StaffAccount>>(
                       [new StaffAccount { Id = "1", FullName = "New", Email = "new@saadsshop.pk", Roles = "" }]));

        Assert.Empty(Assert.Single((await Service.GetStaffAsync()).Value!).Roles);
    }

    [Fact]
    public async Task Lockout_is_reported_only_while_it_is_still_in_force()
    {
        _repository.GetStaffAsync(Arg.Any<CancellationToken>())
                   .Returns(Given.Ok<IReadOnlyList<StaffAccount>>(
                   [
                       new StaffAccount { Id = "1", FullName = "Locked",  Email = "a@b.pk", LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10) },
                       new StaffAccount { Id = "2", FullName = "Expired", Email = "c@d.pk", LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-10) },
                       new StaffAccount { Id = "3", FullName = "Never",   Email = "e@f.pk", LockoutEnd = null }
                   ]));

        var staff = (await Service.GetStaffAsync()).Value!;

        Assert.True(staff[0].IsLockedOut);
        Assert.False(staff[1].IsLockedOut);
        Assert.False(staff[2].IsLockedOut);
    }
}
