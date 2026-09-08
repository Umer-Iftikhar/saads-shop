using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SaadsShop.Api.Middlewares;

namespace SaadsShop.UnitTests.Middlewares;

/// <summary>A host environment with nothing in it but a name.</summary>
file sealed class Env(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "SaadsShop.Api";
    public string ContentRootPath { get; set; } = "/";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}

/// <summary>
/// A response feature that actually remembers its OnStarting callbacks.
/// </summary>
/// <remarks>
/// The framework's default one drops them on the floor, so a middleware that
/// sets a header from OnStarting — which is the only correct place to set one —
/// would look like it did nothing. Kestrel runs those callbacks when the
/// response starts; <see cref="FireOnStartingAsync"/> is that moment.
/// </remarks>
file sealed class RecordingResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = new MemoryStream();
    public bool HasStarted { get; set; }

    public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
    public void OnCompleted(Func<object, Task> callback, object state) { }

    public async Task FireOnStartingAsync()
    {
        HasStarted = true;
        foreach (var (callback, state) in _onStarting) await callback(state);
    }
}

file static class Http
{
    /// <summary>A context whose response body can be read back as a string.</summary>
    public static DefaultHttpContext Context(string path = "/api/orders", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(
            new RecordingResponseFeature());

        context.Request.Path   = path;
        context.Request.Method = method;
        context.Response.Body  = new MemoryStream();
        return context;
    }

    public static async Task<string> ReadBodyAsync(this HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    /// <summary>Stands in for the server flushing the response headers.</summary>
    public static Task StartResponseAsync(this HttpContext context)
        => ((RecordingResponseFeature)context.Features
            .Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>()!).FireOnStartingAsync();
}

public class CorrelationIdMiddlewareTests
{
    private static async Task<HttpContext> RunAsync(
        Action<HttpContext>? arrange = null, RequestDelegate? next = null)
    {
        var context = Http.Context();
        arrange?.Invoke(context);

        //  DefaultHttpContext does not fire OnStarting by itself, so the
        //  terminal delegate stands in for the server flushing headers.
        var middleware = new CorrelationIdMiddleware(async ctx =>
        {
            if (next is not null) await next(ctx);
            await ctx.StartResponseAsync();
        });

        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task Gives_a_request_without_an_id_one_of_its_own()
    {
        var context = await RunAsync(ctx => ctx.TraceIdentifier = "0HNOBGDA8BRSN");

        Assert.Equal("0HNOBGDA8BRSN", context.Items[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Echoes_the_id_on_the_response_so_a_screenshot_carries_it()
    {
        var context = await RunAsync(ctx => ctx.TraceIdentifier = "0HNOBGDA8BRSN");

        Assert.Equal("0HNOBGDA8BRSN", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Honours_an_inbound_id_so_a_chain_of_calls_shares_one()
    {
        var context = await RunAsync(ctx =>
            ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = "from-the-caller");

        Assert.Equal("from-the-caller", context.Items[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Refuses_an_absurdly_long_inbound_id()
    {
        // It would otherwise land in every log line and in a response header.
        var context = await RunAsync(ctx =>
        {
            ctx.TraceIdentifier = "ours";
            ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = new string('x', 65);
        });

        Assert.Equal("ours", context.Items[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Accepts_an_inbound_id_right_at_the_limit()
    {
        var sixtyFour = new string('x', 64);

        var context = await RunAsync(ctx =>
            ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = sixtyFour);

        Assert.Equal(sixtyFour, context.Items[CorrelationIdMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ignores_a_blank_inbound_id(string incoming)
    {
        var context = await RunAsync(ctx =>
        {
            ctx.TraceIdentifier = "ours";
            ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;
        });

        Assert.Equal("ours", context.Items[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Passes_the_request_on()
    {
        var reached = false;

        await RunAsync(next: _ => { reached = true; return Task.CompletedTask; });

        Assert.True(reached);
    }
}

public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpContext> RunAsync(Action<HttpContext>? arrange = null)
    {
        var context = Http.Context();
        arrange?.Invoke(context);

        await new SecurityHeadersMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'")]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    public async Task Sets_the_header(string name, string expected)
    {
        var context = await RunAsync();

        Assert.Equal(expected, context.Response.Headers[name]);
    }

    [Fact]
    public async Task Denies_the_browser_features_an_API_has_no_use_for()
    {
        var context = await RunAsync();

        var policy = context.Response.Headers["Permissions-Policy"].ToString();
        Assert.Contains("geolocation=()", policy);
        Assert.Contains("camera=()", policy);
        Assert.Contains("payment=()", policy);
    }

    [Fact]
    public async Task Strips_the_headers_that_advertise_what_is_running()
    {
        // Free reconnaissance: the version tells an attacker which advisories to try.
        var context = await RunAsync(ctx =>
        {
            ctx.Response.Headers["Server"] = "Kestrel";
            ctx.Response.Headers["X-Powered-By"] = "ASP.NET";
        });

        Assert.False(context.Response.Headers.ContainsKey("Server"));
        Assert.False(context.Response.Headers.ContainsKey("X-Powered-By"));
    }

    [Fact]
    public async Task Runs_before_the_rest_of_the_pipeline_so_a_thrown_request_still_carries_them()
    {
        var context = Http.Context();

        var middleware = new SecurityHeadersMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
    }
}

public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware Middleware(RequestDelegate next, string environment = "Production")
        => new(next, NullLogger<ExceptionHandlingMiddleware>.Instance, new Env(environment));

    private static async Task<JsonElement> ProblemFrom(HttpContext context)
        => JsonDocument.Parse(await context.ReadBodyAsync()).RootElement;

    [Fact]
    public async Task Lets_a_request_that_does_not_throw_alone()
    {
        var context = Http.Context();

        await Middleware(ctx => { ctx.Response.StatusCode = 201; return Task.CompletedTask; })
            .InvokeAsync(context);

        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal(string.Empty, await context.ReadBodyAsync());
    }

    [Fact]
    public async Task Turns_anything_unhandled_into_a_500()
    {
        var context = Http.Context();

        await Middleware(_ => throw new InvalidOperationException("connection string is wrong"))
            .InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task Says_nothing_about_what_actually_broke()
    {
        var context = Http.Context();

        await Middleware(_ => throw new InvalidOperationException(
            "Login failed for user 'sa' at Data Source=10.0.0.4")).InvokeAsync(context);

        var body = await context.ReadBodyAsync();

        // Stack traces, SQL text and connection strings stay in the log.
        Assert.DoesNotContain("Data Source", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.Contains("Something went wrong", body);
    }

    [Fact]
    public async Task Carries_the_correlation_id_so_the_report_can_be_traced()
    {
        var context = Http.Context();
        context.Items[CorrelationIdMiddleware.HeaderName] = "0HNOBGDA8BRSN";

        await Middleware(_ => throw new Exception("boom")).InvokeAsync(context);

        Assert.Equal("0HNOBGDA8BRSN", (await ProblemFrom(context)).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Falls_back_to_the_trace_id_when_the_correlation_middleware_did_not_run()
    {
        var context = Http.Context();
        context.TraceIdentifier = "trace-1";

        await Middleware(_ => throw new Exception("boom")).InvokeAsync(context);

        Assert.Equal("trace-1", (await ProblemFrom(context)).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Names_the_path_that_failed()
    {
        var context = Http.Context("/api/admin/dashboard");

        await Middleware(_ => throw new Exception("boom")).InvokeAsync(context);

        Assert.Equal("/api/admin/dashboard", (await ProblemFrom(context)).GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Points_at_a_stable_problem_type()
    {
        var context = Http.Context();

        await Middleware(_ => throw new Exception("boom")).InvokeAsync(context);

        Assert.Equal("https://saadsshop.pk/errors/server-error",
                     (await ProblemFrom(context)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Adds_the_detail_only_in_development()
    {
        var context = Http.Context();

        await Middleware(_ => throw new InvalidOperationException("the actual reason"), "Development")
            .InvokeAsync(context);

        var problem = await ProblemFrom(context);
        Assert.Equal("InvalidOperationException", problem.GetProperty("exception").GetString());
        Assert.Equal("the actual reason", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Withholds_the_detail_everywhere_else()
    {
        var context = Http.Context();

        await Middleware(_ => throw new InvalidOperationException("the actual reason"), "Staging")
            .InvokeAsync(context);

        var problem = await ProblemFrom(context);
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Treats_a_closed_tab_as_the_non_event_it_is()
    {
        var context = Http.Context();
        var aborted = new CancellationTokenSource();
        aborted.Cancel();
        context.RequestAborted = aborted.Token;

        await Middleware(_ => throw new OperationCanceledException()).InvokeAsync(context);

        // 499, and no problem body: nobody is listening for it.
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(string.Empty, await context.ReadBodyAsync());
    }

    [Fact]
    public async Task Still_reports_a_cancellation_the_client_did_not_ask_for()
    {
        // A timeout inside the shop's own code is a real failure, not a closed tab.
        var context = Http.Context();

        await Middleware(_ => throw new OperationCanceledException()).InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
    }

    [Fact]
    public async Task Clears_a_buffered_half_written_body_before_writing_the_problem()
    {
        var context = Http.Context();

        await Middleware(async ctx =>
        {
            await ctx.Response.WriteAsync("{\"partial\":");
            throw new Exception("boom");
        }).InvokeAsync(context);

        var body = await context.ReadBodyAsync();
        Assert.DoesNotContain("partial", body);
        Assert.Contains("Something went wrong", body);
    }

    [Fact]
    public async Task Aborts_rather_than_appending_JSON_to_a_response_already_on_the_wire()
    {
        //  Once the status line is sent there is nothing to clear, and a problem
        //  document glued to the end of a half-sent body is one the client
        //  cannot parse. Dropping the connection is the honest answer.
        var context = Http.Context();
        var aborted = false;

        var feature = (RecordingResponseFeature)context.Features
            .Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>()!;

        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(
            new AbortRecordingLifetime(() => aborted = true));

        await Middleware(async ctx =>
        {
            await ctx.Response.WriteAsync("{\"partial\":");
            feature.HasStarted = true;
            throw new Exception("boom");
        }).InvokeAsync(context);

        Assert.True(aborted);
        Assert.DoesNotContain("Something went wrong", await context.ReadBodyAsync());
    }

    private sealed class AbortRecordingLifetime(Action onAbort)
        : Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => onAbort();
    }
}
