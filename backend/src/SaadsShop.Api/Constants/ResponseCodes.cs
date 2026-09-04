namespace SaadsShop.Api.Constants;

/// <summary>
/// The response codes stored procedures return — which are HTTP status codes.
/// </summary>
/// <remarks>
/// A procedure's <c>ResponseCode</c> <em>is</em> the status the API returns, so
/// there is no translation table between the database and the wire and no way
/// for the two to disagree. What a procedure decides is what the caller sees.
///
/// The trade-off is that the code alone no longer identifies which rule failed:
/// an out-of-stock line and a duplicate product name are both <c>409</c>. The
/// <c>ResponseMessage</c> carries that detail, and it is written to be shown to
/// a shopkeeper or a customer as-is — "Compact Chhata just went out of stock."
/// names the offending line precisely enough for the cart to act on it.
///
/// An integration test asserts every code a procedure can return appears here.
/// </remarks>
public static class ResponseCodes
{
    /// <summary>The operation succeeded.</summary>
    public const int Success = StatusCodes.Status200OK;             // 200

    /// <summary>Input failed validation — a missing field, a bad format, a value out of range.</summary>
    public const int ValidationFailed = StatusCodes.Status400BadRequest;   // 400

    /// <summary>The caller is not authenticated, or the session is no longer valid.</summary>
    public const int Unauthorised = StatusCodes.Status401Unauthorized;     // 401

    /// <summary>Authenticated, but not permitted.</summary>
    public const int Forbidden = StatusCodes.Status403Forbidden;           // 403

    /// <summary>No such product, order, customer, cloth or settings row.</summary>
    public const int NotFound = StatusCodes.Status404NotFound;             // 404

    /// <summary>
    /// A business rule refused: insufficient stock, a duplicate name, an
    /// illegal status move, a disabled payment method.
    /// </summary>
    public const int Conflict = StatusCodes.Status409Conflict;             // 409

    /// <summary>Too many attempts — sign-in, 2FA, or order placement.</summary>
    public const int TooManyRequests = StatusCodes.Status429TooManyRequests; // 429

    /// <summary>Caught inside the procedure, logged there, details not exposed.</summary>
    public const int ServerError = StatusCodes.Status500InternalServerError; // 500

    public static bool IsSuccess(int code) => code is >= 200 and < 300;

    /// <summary>
    /// The code is already an HTTP status, so this is the identity — with a
    /// guard so a procedure returning something nonsensical cannot make the API
    /// emit an invalid status line.
    /// </summary>
    public static int ToHttpStatus(int code)
        => code is >= 100 and < 600 ? code : StatusCodes.Status500InternalServerError;

    /// <summary>A stable slug for the problem+json "type" URI.</summary>
    public static string ToSlug(int code) => code switch
    {
        ValidationFailed => "validation-failed",
        Unauthorised     => "unauthorised",
        Forbidden        => "forbidden",
        NotFound         => "not-found",
        Conflict         => "conflict",
        TooManyRequests  => "too-many-requests",
        _                => "server-error"
    };
}
