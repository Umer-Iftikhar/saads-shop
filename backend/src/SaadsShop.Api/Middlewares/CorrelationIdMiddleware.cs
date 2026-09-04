using Serilog.Context;

namespace SaadsShop.Api.Middlewares;

/// <summary>
/// Gives every request an id, echoes it on the response, and pushes it into the
/// Serilog context so every line logged while handling the request carries it.
/// </summary>
/// <remarks>
/// This is what makes a shopkeeper's screenshot actionable: the id in the error
/// they saw finds the exact log lines behind it.
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // Honour an inbound id so a chain of calls shares one, but cap the
        // length — an unbounded value from a caller ends up in every log line
        // and in a response header.
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();

        var correlationId = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 64
            ? incoming
            : context.TraceIdentifier;

        context.Items[HeaderName] = correlationId;

        // Set on the response before anything writes to the body — headers
        // cannot be added once the response has started.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
