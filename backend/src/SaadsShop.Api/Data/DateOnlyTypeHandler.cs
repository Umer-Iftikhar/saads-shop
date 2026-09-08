using System.Data;
using Dapper;

namespace SaadsShop.Api.Data;

/// <summary>
/// Teaches Dapper to pass a <see cref="DateOnly"/> to SQL Server and read one back.
/// </summary>
/// <remarks>
/// Microsoft.Data.SqlClient does not accept a <c>DateOnly</c> parameter — it
/// throws <c>"The member X of type System.DateOnly cannot be used as a parameter
/// value"</c> at the point the command is built, which is to say at runtime, on
/// whichever screen happens to use a date first. The dashboard and the order
/// date filter both did.
///
/// The conversion is to midnight and back, so nothing is lost: the columns
/// behind these parameters are <c>DATE</c>, and the procedures do their own
/// end-of-day handling on the range. Registered once at startup, this keeps
/// <c>DateOnly</c> in the repository signatures where it belongs — a date filter
/// is a date, not an instant — rather than pushing DateTime up through the
/// services to work around a driver limitation.
/// </remarks>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value  = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateOnly date     => date,
        string text       => DateOnly.Parse(text),
        _ => throw new DataException($"Cannot convert {value.GetType()} to DateOnly.")
    };
}

/// <summary>
/// Registers the Dapper type handlers the repositories rely on.
/// </summary>
/// <remarks>
/// This exists so the application and the integration tests cannot register
/// different sets. Registering by hand in a test harness looks harmless until
/// the app adds a handler the harness does not have, and the suite goes on
/// passing against behaviour production no longer has.
///
/// Idempotent: Dapper keeps one handler per type, so calling this twice — as a
/// test host and the API both starting in one process would — replaces rather
/// than duplicates.
/// </remarks>
public static class DapperTypeHandlers
{
    public static void Register() => SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
}
