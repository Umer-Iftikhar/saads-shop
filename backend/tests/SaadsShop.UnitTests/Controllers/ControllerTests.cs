using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Controllers;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.DTOs.Response;
using SaadsShop.Api.Middlewares;
using SaadsShop.Api.Services.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces.Queries;

namespace SaadsShop.UnitTests.Controllers;

file static class Controllers
{
    /// <summary>Puts a real HttpContext on a controller, optionally signed in.</summary>
    public static T With<T>(this T controller, string? userId = null, string path = "/api/orders")
        where T : ControllerBase
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Path = path;

        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId)],
                "test"));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    public static ProblemDetails ProblemOf(IActionResult result)
    {
        var status = Assert.IsType<ObjectResult>(result);
        return Assert.IsType<ProblemDetails>(status.Value);
    }
}

/// <summary>
/// The one place a service result becomes an HTTP response.
/// </summary>
/// <remarks>
/// Every controller in the API funnels through <c>FromResult</c>, so these
/// assertions are about all of them at once. The concrete subclass exists only
/// because the base is abstract — it adds nothing.
/// </remarks>
public class ApiControllerBaseTests
{
    private sealed class Probe : ApiControllerBase
    {
        public IActionResult Map<T>(OperationResult<T> result, int success = StatusCodes.Status200OK)
            => FromResult(result, success);

        public IActionResult MapCreated<T>(OperationResult<T> result, string location)
            => CreatedFromResult(result, location);

        public string? WhoAmI => CurrentUserId;
        public string? Caller => CallerIp;
    }

    private static Probe AProbe(string? userId = null) => new Probe().With(userId);

    [Fact]
    public void Answers_200_with_the_value()
    {
        var result = AProbe().Map(OperationResult<string>.Success("Gulaab Bridal Set"));

        var ok = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("Gulaab Bridal Set", ok.Value);
    }

    [Fact]
    public void Answers_204_with_no_body_at_all()
    {
        // A 204 carrying a body is a 204 some clients will choke on.
        var result = AProbe().Map(OperationResult<bool>.Success(true), StatusCodes.Status204NoContent);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public void Answers_201_when_asked_to()
    {
        var result = AProbe().Map(OperationResult<int>.Success(7), StatusCodes.Status201Created);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(ResponseCodes.ValidationFailed, 400)]
    [InlineData(ResponseCodes.Unauthorised, 401)]
    [InlineData(ResponseCodes.Forbidden, 403)]
    [InlineData(ResponseCodes.NotFound, 404)]
    [InlineData(ResponseCodes.Conflict, 409)]
    [InlineData(ResponseCodes.TooManyRequests, 429)]
    [InlineData(ResponseCodes.ServerError, 500)]
    public void Uses_the_procedures_code_as_the_status(int code, int expected)
    {
        // The stored procedure's response code IS the HTTP status; nothing translates.
        var result = AProbe().Map(OperationResult<string>.Failure(code, "No."));

        Assert.Equal(expected, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Refuses_to_emit_a_status_line_a_procedure_invented()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(9999, "?"));

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Passes_the_failure_message_through_as_the_title()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(
            ResponseCodes.Conflict, "Compact Chhata just went out of stock."));

        Assert.Equal("Compact Chhata just went out of stock.", Controllers.ProblemOf(result).Title);
    }

    [Fact]
    public void Keeps_the_original_response_code_alongside_the_status()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(ResponseCodes.Conflict, "No."));

        Assert.Equal(ResponseCodes.Conflict,
                     Controllers.ProblemOf(result).Extensions["responseCode"]);
    }

    [Fact]
    public void Points_at_a_stable_problem_type_per_kind_of_failure()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(ResponseCodes.NotFound, "No."));

        Assert.Equal("https://saadsshop.pk/errors/not-found", Controllers.ProblemOf(result).Type);
    }

    [Fact]
    public void Names_the_path_that_failed()
    {
        var probe = new Probe().With(path: "/api/admin/orders/7");

        var result = probe.Map(OperationResult<string>.Failure(ResponseCodes.NotFound, "No."));

        Assert.Equal("/api/admin/orders/7", Controllers.ProblemOf(result).Instance);
    }

    [Fact]
    public void Carries_field_errors_so_a_form_can_place_each_message()
    {
        var result = AProbe().Map(OperationResult<string>.Invalid(
            new Dictionary<string, string[]> { ["Phone"] = ["That phone number does not look right."] }));

        var errors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(
            Controllers.ProblemOf(result).Extensions["errors"]);

        Assert.Equal("That phone number does not look right.", errors["Phone"].Single());
    }

    [Fact]
    public void Omits_the_errors_key_entirely_when_the_failure_was_not_per_field()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(ResponseCodes.NotFound, "No."));

        Assert.False(Controllers.ProblemOf(result).Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void Carries_the_correlation_id_onto_every_failure()
    {
        var probe = AProbe();
        probe.HttpContext.Items[CorrelationIdMiddleware.HeaderName] = "0HNOBGDA8BRSN";

        var result = probe.Map(OperationResult<string>.Failure(ResponseCodes.ServerError, "No."));

        Assert.Equal("0HNOBGDA8BRSN", Controllers.ProblemOf(result).Extensions["correlationId"]);
    }

    [Fact]
    public void Falls_back_to_the_trace_id_for_the_correlation_id()
    {
        var result = AProbe().Map(OperationResult<string>.Failure(ResponseCodes.ServerError, "No."));

        Assert.Equal("trace-1", Controllers.ProblemOf(result).Extensions["correlationId"]);
    }

    [Fact]
    public void Sets_a_Location_header_for_something_newly_created()
    {
        var result = AProbe().MapCreated(OperationResult<string>.Success("SS-2419"), "/api/orders/SS-2419");

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal("/api/orders/SS-2419", created.Location);
        Assert.Equal("SS-2419", created.Value);
    }

    [Fact]
    public void Does_not_pretend_something_was_created_when_it_was_not()
    {
        var result = AProbe().MapCreated(
            OperationResult<string>.Failure(ResponseCodes.Conflict, "Out of stock."), "/api/orders/x");

        Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Reads_the_signed_in_user_from_the_token()
    {
        Assert.Equal("user-1", AProbe("user-1").WhoAmI);
    }

    [Fact]
    public void Has_no_user_for_anonymous_storefront_traffic()
    {
        // Placing an order needs no account; the shop takes cash on delivery.
        Assert.Null(AProbe().WhoAmI);
    }

    [Fact]
    public void Reads_the_user_from_the_name_identifier_claim_as_well()
    {
        //  The two names are the same claim: JwtSecurityTokenHandler renames
        //  "sub" on the way in unless the inbound map is cleared. Reading both
        //  is what keeps a token issued either way working.
        var probe = new Probe();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-2")], "test")),
        };
        probe.ControllerContext = new ControllerContext { HttpContext = context };

        Assert.Equal("user-2", probe.WhoAmI);
    }

    [Fact]
    public void Has_no_caller_ip_when_the_connection_does_not_report_one()
    {
        Assert.Null(AProbe().Caller);
    }

    [Fact]
    public void Reports_the_caller_ip_for_audit_and_rate_limiting()
    {
        var probe = AProbe();
        probe.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", probe.Caller);
    }
}

public class AuthControllerTests
{
    private readonly IAuthCommandService _auth  = Substitute.For<IAuthCommandService>();
    private readonly IAuthQueryService   _reads = Substitute.For<IAuthQueryService>();

    private static readonly AuthOptions Auth = new()
    {
        RefreshCookieName = "saadsshop_rt",
        RefreshCookieSameSiteStrict = true,
    };

    private AuthController AController(GoogleAuthOptions? google = null)
        => new AuthController(
            _reads, _auth,
            Options.Create(Auth),
            Options.Create(google ?? new GoogleAuthOptions()),
            NullLogger<AuthController>.Instance).With(path: "/api/auth/refresh");

    private static SessionIssued ASession(string refreshToken = "refresh-1") => new()
    {
        Auth = new AuthResponse { AccessToken = "access-1", Email = "saad@saadsshop.pk" },
        RefreshToken = refreshToken,
        RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(14),
    };

    /// <summary>The Set-Cookie header the controller wrote, if any.</summary>
    private static string? CookieHeader(ControllerBase controller)
        => controller.Response.Headers.SetCookie.FirstOrDefault();

    // ── login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_never_sets_a_refresh_cookie_because_the_password_alone_is_not_a_session()
    {
        _auth.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<LoginChallengeResponse>.Success(
                 new LoginChallengeResponse { MfaToken = "mfa-1", IsTwoFactorEnrolled = true }));

        var controller = AController();
        var result = await controller.Login(new LoginRequest(), default);

        Assert.Equal(200, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(CookieHeader(controller));
    }

    [Fact]
    public async Task Login_passes_the_callers_ip_down_for_the_audit_trail()
    {
        _auth.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<LoginChallengeResponse>.Failure(ResponseCodes.Unauthorised, "No."));

        var controller = AController();
        controller.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        await controller.Login(new LoginRequest(), default);

        await _auth.Received().LoginAsync(Arg.Any<LoginRequest>(), "203.0.113.7", Arg.Any<CancellationToken>());
    }

    // ── two-factor ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Verifying_the_second_factor_sets_the_refresh_cookie()
    {
        _auth.VerifyTwoFactorAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession()));

        var controller = AController();
        await controller.VerifyTwoFactor(new TwoFactorRequest(), default);

        var cookie = CookieHeader(controller)!;
        Assert.Contains("saadsshop_rt=refresh-1", cookie);
    }

    [Fact]
    public async Task The_refresh_cookie_is_out_of_JavaScripts_reach_and_off_plain_HTTP()
    {
        _auth.VerifyTwoFactorAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession()));

        var controller = AController();
        await controller.VerifyTwoFactor(new TwoFactorRequest(), default);

        var cookie = CookieHeader(controller)!;
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        // Scoped, so it is not attached to every ordinary API call.
        Assert.Contains("path=/api/auth", cookie.ToLowerInvariant());
    }

    [Fact]
    public async Task The_refresh_token_itself_never_appears_in_the_body()
    {
        _auth.VerifyTwoFactorAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession()));

        var result = await AController().VerifyTwoFactor(new TwoFactorRequest(), default);

        // A token JavaScript can read is a token XSS can steal.
        var body = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("access-1", body.AccessToken);
    }

    [Fact]
    public async Task A_failed_second_factor_sets_no_cookie()
    {
        _auth.VerifyTwoFactorAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Failure(
                 ResponseCodes.Unauthorised, "That code is not right."));

        var controller = AController();
        var result = await controller.VerifyTwoFactor(new TwoFactorRequest(), default);

        Assert.Equal(401, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(CookieHeader(controller));
    }

    [Fact]
    public async Task Relaxes_SameSite_only_when_the_SPA_genuinely_lives_elsewhere()
    {
        _auth.VerifyTwoFactorAsync(Arg.Any<TwoFactorRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession()));

        var controller = new AuthController(
            _reads, _auth,
            Options.Create(new AuthOptions { RefreshCookieSameSiteStrict = false }),
            Options.Create(new GoogleAuthOptions()),
            NullLogger<AuthController>.Instance).With();

        await controller.VerifyTwoFactor(new TwoFactorRequest(), default);

        Assert.Contains("samesite=none", CookieHeader(controller)!.ToLowerInvariant());
    }

    // ── refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_without_a_cookie_is_a_401_and_never_reaches_the_service()
    {
        var controller = AController();

        var result = await controller.Refresh(default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _auth.DidNotReceive().RefreshAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_reads_the_token_from_the_cookie_and_not_from_the_body()
    {
        _auth.RefreshAsync("presented-token", Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession("rotated")));

        var controller = AController();
        controller.HttpContext.Request.Headers.Cookie = "saadsshop_rt=presented-token";

        var result = await controller.Refresh(default);

        Assert.IsType<OkObjectResult>(result);
        await _auth.Received().RefreshAsync("presented-token", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_writes_the_rotated_token_back()
    {
        _auth.RefreshAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Success(ASession("rotated")));

        var controller = AController();
        controller.HttpContext.Request.Headers.Cookie = "saadsshop_rt=presented-token";

        await controller.Refresh(default);

        Assert.Contains("saadsshop_rt=rotated", CookieHeader(controller)!);
    }

    [Fact]
    public async Task A_dead_refresh_token_is_cleared_from_the_browser()
    {
        //  Otherwise every page load retries with a token that can never work —
        //  and after reuse detection, keeps re-reporting the same theft.
        _auth.RefreshAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<SessionIssued>.Failure(
                 ResponseCodes.Unauthorised, "Please sign in again."));

        var controller = AController();
        controller.HttpContext.Request.Headers.Cookie = "saadsshop_rt=stale";

        var result = await controller.Refresh(default);

        Assert.Equal(401, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", CookieHeader(controller)!);
    }

    // ── logout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_answers_204_and_clears_the_cookie()
    {
        _auth.LogoutAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<bool>.Success(true));

        var controller = AController().With("user-1");
        controller.HttpContext.Request.Headers.Cookie = "saadsshop_rt=live";

        var result = await controller.Logout(default);

        Assert.IsType<NoContentResult>(result);
        Assert.Contains("expires=Thu, 01 Jan 1970", CookieHeader(controller)!);
    }

    [Fact]
    public async Task Logout_clears_the_cookie_even_when_the_server_side_revoke_fails()
    {
        // Leaving a cookie behind after someone pressed "sign out" is worse.
        _auth.LogoutAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<bool>.Failure(ResponseCodes.ServerError, "Database is down."));

        var controller = AController().With("user-1");
        controller.HttpContext.Request.Headers.Cookie = "saadsshop_rt=live";

        await controller.Logout(default);

        Assert.Contains("expires=Thu, 01 Jan 1970", CookieHeader(controller)!);
    }

    // ── Google ───────────────────────────────────────────────────────────────

    [Fact]
    public void Google_sign_in_is_a_404_when_the_shop_has_not_set_it_up()
    {
        // Not a 500, and not a challenge that would fail confusingly at Google.
        var result = AController(new GoogleAuthOptions()).GoogleChallenge(null);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}

public class ThinControllerTests
{
    /*  These read as trivial, and that is the assertion: a controller here binds,
        calls one service method, and hands the result to the base. Anything else
        appearing in one — a second branch on a response code, a mapping, a
        rule — is the thing these tests are here to make visible.               */

    [Fact]
    public async Task The_catalogue_passes_a_query_straight_through()
    {
        var catalog = Substitute.For<ICatalogQueryService>();
        var query   = new ProductListQuery { Page = 2 };

        catalog.GetStorefrontProductsAsync(query, Arg.Any<CancellationToken>())
               .Returns(OperationResult<PagedResponse<ProductSummaryResponse>>.Success(new PagedResponse<ProductSummaryResponse>()));

        var result = await new CatalogController(catalog).With().GetProducts(query, default);

        Assert.Equal(200, Assert.IsType<ObjectResult>(result).StatusCode);
        await catalog.Received().GetStorefrontProductsAsync(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Looking_a_product_up_by_slug_asks_by_slug_and_not_by_id()
    {
        var catalog = Substitute.For<ICatalogQueryService>();
        catalog.GetProductAsync(null, "gulaab-bridal-set", Arg.Any<CancellationToken>())
               .Returns(OperationResult<ProductDetailResponse>.Success(new ProductDetailResponse()));

        await new CatalogController(catalog).With().GetProductBySlug("gulaab-bridal-set", default);

        await catalog.Received().GetProductAsync(null, "gulaab-bridal-set", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_product_is_a_404_rather_than_an_empty_200()
    {
        var catalog = Substitute.For<ICatalogQueryService>();
        catalog.GetProductAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
               .Returns(OperationResult<ProductDetailResponse>.Failure(
                   ResponseCodes.NotFound, "That product is no longer listed."));

        var result = await new CatalogController(catalog).With().GetProduct(404, default);

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Placing_an_order_answers_201_with_a_Location_the_shopper_can_follow()
    {
        var reads  = Substitute.For<IOrderQueryService>();
        var writes = Substitute.For<IOrderCommandService>();

        writes.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult<OrderConfirmationResponse>.Success(
                  new OrderConfirmationResponse { Reference = "SS-2419" }));

        var result = await new OrdersController(reads, writes).With().Place(new PlaceOrderRequest(), default);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal("/api/orders/SS-2419", created.Location);
    }

    [Fact]
    public async Task An_order_that_cannot_be_placed_is_not_dressed_up_as_created()
    {
        var reads  = Substitute.For<IOrderQueryService>();
        var writes = Substitute.For<IOrderCommandService>();

        writes.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
              .Returns(OperationResult<OrderConfirmationResponse>.Failure(
                  ResponseCodes.Conflict, "Compact Chhata just went out of stock."));

        var result = await new OrdersController(reads, writes).With().Place(new PlaceOrderRequest(), default);

        Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Tracking_an_order_needs_the_phone_it_was_placed_with()
    {
        //  References are sequential — SS-2419 sits next to SS-2418 — so the
        //  phone is the shared secret that stops anyone walking the shop's orders.
        var reads  = Substitute.For<IOrderQueryService>();
        var writes = Substitute.For<IOrderCommandService>();

        reads.TrackAsync(Arg.Any<TrackOrderQuery>(), Arg.Any<CancellationToken>())
             .Returns(OperationResult<OrderTrackingResponse>.Success(new OrderTrackingResponse()));

        await new OrdersController(reads, writes).With().Track("SS-2419", "03012345678", default);

        await reads.Received().TrackAsync(
            Arg.Is<TrackOrderQuery>(q => q.Reference == "SS-2419" && q.Phone == "03012345678"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_status_change_records_who_made_it()
    {
        var reads  = Substitute.For<IOrderQueryService>();
        var writes = Substitute.For<IOrderCommandService>();

        writes.UpdateStatusAsync(7, Arg.Any<UpdateOrderStatusRequest>(), "user-1", Arg.Any<CancellationToken>())
              .Returns(OperationResult<bool>.Success(true));

        var controller = new AdminOrdersController(reads, writes).With("user-1");
        var result = await controller.UpdateStatus(7, new UpdateOrderStatusRequest(), default);

        Assert.IsType<NoContentResult>(result);
        await writes.Received().UpdateStatusAsync(
            7, Arg.Any<UpdateOrderStatusRequest>(), "user-1", Arg.Any<CancellationToken>());
    }
}
