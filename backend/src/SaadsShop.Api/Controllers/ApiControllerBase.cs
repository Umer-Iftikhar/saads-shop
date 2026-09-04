using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Middlewares;

namespace SaadsShop.Api.Controllers;

/// <summary>
/// Turns an <see cref="OperationResult{T}"/> into an HTTP response, in one place.
/// </summary>
/// <remarks>
/// Controllers here are deliberately thin: bind, authorise, call a service, hand
/// the result to <see cref="FromResult"/>. Any branching on response codes in a
/// controller would be a second copy of a mapping that already exists.
/// </remarks>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The signed-in user's id, or null for anonymous storefront traffic.</summary>
    protected string? CurrentUserId =>
        User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// The caller's IP, for audit and rate limiting. Behind a reverse proxy this
    /// is the proxy's address unless forwarded headers are configured — see
    /// Program.cs, where they are only trusted from known networks.
    /// </summary>
    protected string? CallerIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected IActionResult FromResult<T>(OperationResult<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatus == StatusCodes.Status204NoContent
                ? NoContent()
                : StatusCode(successStatus, result.Value);
        }

        return Problem(result);
    }

    /// <summary>Success with a Location header, for a newly created resource.</summary>
    protected IActionResult CreatedFromResult<T>(OperationResult<T> result, string location)
        => result.IsSuccess
            ? Created(location, result.Value)
            : Problem(result);

    protected IActionResult Problem<T>(OperationResult<T> result)
    {
        var status = result.HttpStatus;

        var problem = new ProblemDetails
        {
            // The stored procedure's code IS the status, so there is nothing to
            // translate — see docs/database.md.
            Type     = $"https://saadsshop.pk/errors/{ResponseCodes.ToSlug(status)}",
            Title    = result.Message,
            Status   = status,
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["responseCode"]  = result.ResponseCode;
        problem.Extensions["correlationId"] =
            HttpContext.Items[CorrelationIdMiddleware.HeaderName] as string ?? HttpContext.TraceIdentifier;

        if (result.Errors is { Count: > 0 })
            problem.Extensions["errors"] = result.Errors;

        return StatusCode(status, problem);
    }
}
