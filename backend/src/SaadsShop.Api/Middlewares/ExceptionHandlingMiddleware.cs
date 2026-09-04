using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;

namespace SaadsShop.Api.Middlewares;

/// <summary>
/// Turns anything unhandled into RFC 7807 problem details.
/// </summary>
/// <remarks>
/// Expected failures never reach here — services return them as typed results
/// the controller maps. What arrives is genuinely unexpected, so the response
/// says only "something went wrong" plus a correlation id. Stack traces,
/// SQL text and connection strings stay in the log where they belong.
/// </remarks>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The customer closed the tab. Not an error, and not worth a log
            // entry at Error — it would drown the ones that matter.
            logger.LogDebug("Request cancelled by the client: {Path}", context.Request.Path);

            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception ex)
        {
            var correlationId = context.Items[CorrelationIdMiddleware.HeaderName] as string
                                ?? context.TraceIdentifier;

            logger.LogError(ex,
                "Unhandled exception on {Method} {Path} (correlation {CorrelationId})",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                // Too late to write a body — the status line is already sent.
                // Aborting is more honest than appending JSON to a half-written
                // response the client would fail to parse.
                logger.LogWarning("Response already started; aborting connection for {CorrelationId}", correlationId);
                context.Abort();
                return;
            }

            var problem = new ProblemDetails
            {
                Type   = $"https://saadsshop.pk/errors/{ResponseCodes.ToSlug(ResponseCodes.ServerError)}",
                Title  = "Something went wrong. Please try again.",
                Status = StatusCodes.Status500InternalServerError,
                Instance = context.Request.Path
            };

            problem.Extensions["correlationId"] = correlationId;

            // Only in development, and only when explicitly running locally.
            // A detail leak here hands an attacker the internals for free.
            if (environment.IsDevelopment())
            {
                problem.Extensions["exception"] = ex.GetType().Name;
                problem.Extensions["detail"]    = ex.Message;
            }

            context.Response.Clear();
            context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
