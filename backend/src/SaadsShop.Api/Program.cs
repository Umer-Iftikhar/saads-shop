using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Extensions;
using SaadsShop.Api.Middlewares;
using Serilog;
using Serilog.Events;

// A bootstrap logger so failures during startup — a missing connection string,
// a placeholder signing key — are logged rather than vanishing into a silent
// non-zero exit.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "SaadsShop.Api")
        // Framework noise at Information would bury the events that matter.
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/saadsshop-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true));

    builder.Services
        .AddShopOptions(builder.Configuration)
        .AddShopServices()
        .AddShopAuthentication(builder.Configuration)
        .AddShopAuthorization()
        .AddShopRateLimiting()
        .AddShopCors(builder.Configuration);

    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            // Omit nulls: most responses carry several optional fields, and
            // sending them as null is bytes for nothing.
            options.JsonSerializerOptions.DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull;

            // Enums cross the wire as names. An ordinal would silently change
            // meaning the day someone inserts a value into the middle.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    // Model-binding and DataAnnotations failures come back in the same
    // problem+json shape as everything else, rather than ASP.NET's default —
    // one error format for the client to handle.
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    e => e.Key,
                    e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

            var problem = new ValidationProblemDetails(errors)
            {
                Type   = "https://saadsshop.pk/errors/validation-failed",
                Title  = "Please check the highlighted fields.",
                Status = StatusCodes.Status400BadRequest,
                Instance = context.HttpContext.Request.Path
            };

            problem.Extensions["responseCode"]  = StatusCodes.Status400BadRequest;
            problem.Extensions["correlationId"] =
                context.HttpContext.Items[CorrelationIdMiddleware.HeaderName] as string
                ?? context.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // ── pipeline ─────────────────────────────────────────────────────────────
    // Order matters. The exception handler wraps everything downstream, and the
    // correlation id must exist before anything logs.

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
            ex is not null                             ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 500   ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 400   ? LogEventLevel.Warning
            // Health checks every few seconds would otherwise fill the log.
            : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // A year, and only in production — a mistaken HSTS header on localhost
        // makes a developer's browser refuse plain HTTP for months.
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Liveness only — deliberately no database check. A probe that queries SQL
    // turns a slow database into a restart loop that makes the outage worse.
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
       .AllowAnonymous()
       .ExcludeFromDescription();

    Log.Information("Saad's Shop API starting in {Environment}", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "The API failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Exposed so the integration tests can drive the real pipeline with
/// WebApplicationFactory rather than a hand-assembled approximation of it.
/// </summary>
public partial class Program;
