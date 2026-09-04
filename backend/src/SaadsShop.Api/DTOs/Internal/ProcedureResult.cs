using SaadsShop.Api.Constants;

namespace SaadsShop.Api.DTOs.Internal;

/*  Types that never cross the wire. They carry a stored procedure's answer from
    a repository up to a service, and a service's answer up to a controller.   */

/// <summary>The status row every stored procedure returns as its last result set.</summary>
public sealed class ProcedureStatus
{
    public int    ResponseCode    { get; set; }
    public string ResponseMessage { get; set; } = string.Empty;
}

/// <summary>The count row the paging procedures return alongside their page.</summary>
public sealed class PageInfo
{
    public int TotalCount          { get; set; }
    public int Page                { get; set; }
    public int PageSize            { get; set; }
    public int NeedsAttentionCount { get; set; }
}

/// <summary>A stored procedure's payload plus the status it reported.</summary>
/// <remarks>
/// Repositories return this without interpreting it. Deciding what a code
/// <em>means</em> is the service layer's job, kept in one place so the mapping
/// cannot drift between call sites.
/// </remarks>
public sealed class ProcedureResult<T>
{
    public T?     Data            { get; init; }
    public int    ResponseCode    { get; init; }
    public string ResponseMessage { get; init; } = string.Empty;

    public bool IsSuccess => ResponseCodes.IsSuccess(ResponseCode);

    public static ProcedureResult<T> From(T? data, ProcedureStatus status) => new()
    {
        Data            = data,
        ResponseCode    = status.ResponseCode,
        ResponseMessage = status.ResponseMessage
    };
}

/// <summary>
/// What a service hands a controller: either a value, or a failure carrying the
/// code and message the database (or a validator) produced.
/// </summary>
/// <remarks>
/// Expected failures travel this way rather than as exceptions. An order that
/// cannot be placed because an item sold out is an ordinary outcome of
/// checkout, not an exceptional one, and throwing would mean paying for a stack
/// trace on a path that happens all through shaadi season.
/// </remarks>
public sealed class OperationResult<T>
{
    public T?     Value        { get; }
    public int    ResponseCode { get; }
    public string Message      { get; }

    /// <summary>Field-keyed errors, when the failure was per-field.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    private OperationResult(T? value, int code, string message,
                            IReadOnlyDictionary<string, string[]>? errors = null)
    {
        Value        = value;
        ResponseCode = code;
        Message      = message;
        Errors       = errors;
    }

    public bool IsSuccess  => ResponseCodes.IsSuccess(ResponseCode);
    public int  HttpStatus => ResponseCodes.ToHttpStatus(ResponseCode);

    public static OperationResult<T> Success(T value, string message = "OK")
        => new(value, ResponseCodes.Success, message);

    public static OperationResult<T> Failure(int code, string message,
                                             IReadOnlyDictionary<string, string[]>? errors = null)
        => new(default, code, message, errors);

    public static OperationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(default, ResponseCodes.ValidationFailed,
               "Please check the highlighted fields.", errors);

    /// <summary>Carries a failure across a type boundary without restating it.</summary>
    public static OperationResult<T> FromFailure<TOther>(OperationResult<TOther> other)
        => new(default, other.ResponseCode, other.Message, other.Errors);

    public static OperationResult<T> FromProcedure(ProcedureResult<T> result)
        => result.IsSuccess
            ? new OperationResult<T>(result.Data, ResponseCodes.Success, result.ResponseMessage)
            : new OperationResult<T>(default, result.ResponseCode, result.ResponseMessage);
}

/// <summary>A page of rows plus the total the database counted.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items      { get; init; } = [];
    public int              TotalCount { get; init; }
    public int              Page       { get; init; } = 1;
    public int              PageSize   { get; init; } = 24;

    public int  TotalPages      => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage     => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
