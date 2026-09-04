namespace SaadsShop.Api.Constants;

/// <summary>
/// User-defined table types, used when passing a set of rows to a procedure in
/// a single call.
/// </summary>
/// <remarks>
/// SQL Server rejects a table-valued parameter whose type name it cannot
/// resolve, and the failure message points at the parameter rather than the
/// typo — so these are worth naming once.
/// </remarks>
public static class TableTypes
{
    /// <summary>A whole cart, so checkout opens its transaction exactly once.</summary>
    public const string OrderLine = "dbo.OrderLineTableType";

    /// <summary>A set of ids — the cloths attached to a product, for instance.</summary>
    public const string IntList = "dbo.IntListTableType";
}
