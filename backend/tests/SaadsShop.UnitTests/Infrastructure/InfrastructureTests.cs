using System.Data;
using DapperCustom = Dapper.SqlMapper.ICustomQueryParameter;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.Services.Implementations;

namespace SaadsShop.UnitTests.Infrastructure;

public class CacheServiceTests
{
    private static CacheService Service()
        => new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 }));

    [Fact]
    public async Task A_value_is_produced_once_and_served_thereafter()
    {
        var service = Service();
        var calls = 0;

        Task<string> Factory() { calls++; return Task.FromResult("value"); }

        Assert.Equal("value", await service.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory));
        Assert.Equal("value", await service.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Different_keys_do_not_share_a_value()
    {
        var service = Service();

        Assert.Equal("one", await service.GetOrCreateAsync("a", TimeSpan.FromMinutes(1), () => Task.FromResult("one")));
        Assert.Equal("two", await service.GetOrCreateAsync("b", TimeSpan.FromMinutes(1), () => Task.FromResult("two")));
    }

    [Fact]
    public async Task Removing_a_key_makes_the_next_read_a_miss()
    {
        var service = Service();
        var calls = 0;

        Task<int> Factory() => Task.FromResult(++calls);

        await service.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory);
        service.Remove("k");
        await service.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory);

        Assert.Equal(2, calls);
    }

    [Fact]
    public void A_version_starts_at_one_and_climbs()
    {
        var service = Service();

        Assert.Equal(1, service.GetVersion(CacheKeys.CatalogVersion));
        service.BumpVersion(CacheKeys.CatalogVersion);
        Assert.Equal(2, service.GetVersion(CacheKeys.CatalogVersion));
        service.BumpVersion(CacheKeys.CatalogVersion);
        Assert.Equal(3, service.GetVersion(CacheKeys.CatalogVersion));
    }

    [Fact]
    public void Two_families_of_keys_have_independent_versions()
    {
        var service = Service();

        service.BumpVersion(CacheKeys.CatalogVersion);

        Assert.Equal(2, service.GetVersion(CacheKeys.CatalogVersion));
        Assert.Equal(1, service.GetVersion(CacheKeys.SettingsVersion));
    }

    [Fact]
    public async Task Concurrent_misses_on_one_key_run_the_factory_once()
    {
        var service = Service();
        var calls = 0;

        async Task<int> Slow()
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(40);
            return 1;
        }

        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => service.GetOrCreateAsync("hot", TimeSpan.FromMinutes(1), Slow)));

        // A cold cache under load must not stampede the database.
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_misses_on_different_keys_do_not_wait_on_each_other()
    {
        var service = Service();
        var started = 0;

        async Task<int> Slow()
        {
            Interlocked.Increment(ref started);
            await Task.Delay(60);
            return 1;
        }

        var work = Task.WhenAll(
            service.GetOrCreateAsync("a", TimeSpan.FromMinutes(1), Slow),
            service.GetOrCreateAsync("b", TimeSpan.FromMinutes(1), Slow));

        await Task.Delay(25);
        Assert.Equal(2, started);   // both in flight, not serialised
        await work;
    }
}

public class CacheKeyTests
{
    [Fact]
    public void A_version_bump_orphans_every_key_built_from_the_old_one()
    {
        Assert.NotEqual(CacheKeys.Categories(1), CacheKeys.Categories(2));
        Assert.NotEqual(CacheKeys.Swatches(1), CacheKeys.Swatches(2));
        Assert.NotEqual(CacheKeys.Product(1, "gulaab"), CacheKeys.Product(2, "gulaab"));
        Assert.NotEqual(CacheKeys.PublicSettings(1), CacheKeys.PublicSettings(2));
    }

    [Fact]
    public void Two_families_never_collide()
    {
        Assert.NotEqual(CacheKeys.Categories(1), CacheKeys.Swatches(1));
        Assert.NotEqual(CacheKeys.Categories(1), CacheKeys.BedSizes(1));
    }

    [Fact]
    public void A_product_key_distinguishes_the_thing_it_names()
        => Assert.NotEqual(CacheKeys.Product(1, "gulaab"), CacheKeys.Product(1, "jahez"));

    [Fact]
    public void A_dashboard_key_is_one_per_day()
    {
        Assert.Equal("dashboard:2026-09-08", CacheKeys.Dashboard(new DateOnly(2026, 9, 8)));
        Assert.NotEqual(CacheKeys.Dashboard(new DateOnly(2026, 9, 8)),
                        CacheKeys.Dashboard(new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void The_dashboard_is_cached_briefly_and_reference_data_for_much_longer()
    {
        Assert.True(CacheKeys.Lifetimes.Dashboard < CacheKeys.Lifetimes.Catalog);
        Assert.True(CacheKeys.Lifetimes.Catalog   < CacheKeys.Lifetimes.Reference);
    }
}

/// <summary>
/// The table-valued-parameter plumbing. SQL Server binds TVP columns
/// positionally, so a reordering here silently swaps values rather than failing.
/// </summary>
public class RepositoryBaseTests
{
    /// <summary>Exposes the protected helpers. The connection factory is never used.</summary>
    private sealed class Probe() : SaadsShop.Api.Repositories.Implementations.RepositoryBase(null!)
    {
        public static DataTable Lines(IEnumerable<(int, int, int?, string?)> rows) => BuildOrderLinesTable(rows);
        public static DataTable Ints(IEnumerable<int> values) => BuildIntListTable(values);
        public static DynamicParameters WithTable(object? scalars, string name, DataTable table, string type)
            => WithTableParameter(scalars, name, table, type);
    }

    [Fact]
    public void The_order_line_table_keeps_the_column_order_the_type_declares()
    {
        var table = Probe.Lines([(1, 2, 3, "King")]);

        Assert.Equal(["ProductId", "Quantity", "SwatchId", "BedSize"],
                     table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
    }

    [Fact]
    public void An_order_line_carries_its_values_in_that_order()
    {
        var row = Probe.Lines([(7, 2, 3, "King")]).Rows[0];

        Assert.Equal(7, row["ProductId"]);
        Assert.Equal(2, row["Quantity"]);
        Assert.Equal(3, row["SwatchId"]);
        Assert.Equal("King", row["BedSize"]);
    }

    [Fact]
    public void An_optional_swatch_or_bed_size_becomes_a_database_null_not_a_zero()
    {
        var row = Probe.Lines([(7, 1, null, null)]).Rows[0];

        Assert.Equal(DBNull.Value, row["SwatchId"]);
        Assert.Equal(DBNull.Value, row["BedSize"]);
    }

    [Fact]
    public void Several_cart_lines_all_reach_the_table()
        => Assert.Equal(3, Probe.Lines([(1, 1, null, null), (2, 2, null, null), (3, 1, null, null)]).Rows.Count);

    [Fact]
    public void An_empty_cart_produces_an_empty_table_rather_than_throwing()
        => Assert.Empty(Probe.Lines([]).Rows);

    [Fact]
    public void The_int_list_drops_duplicates_because_the_type_has_a_primary_key()
    {
        var table = Probe.Ints([1, 2, 2, 3, 1]);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal([1, 2, 3], table.Rows.Cast<DataRow>().Select(r => (int)r["Value"]));
    }

    [Fact]
    public void The_int_list_has_the_single_column_the_type_declares()
    {
        var table = Probe.Ints([1]);

        Assert.Single(table.Columns);
        Assert.Equal("Value", table.Columns[0].ColumnName);
    }

    /// <summary>
    /// The regression test for a runtime failure: a TVP nested in an anonymous
    /// object is treated as an ordinary value and fails with "No mapping exists
    /// from object type Dapper.TableValuedParameter". It has to go through
    /// DynamicParameters.
    /// </summary>
    [Fact]
    public void A_table_parameter_is_added_to_dynamic_parameters_alongside_the_scalars()
    {
        var parameters = Probe.WithTable(
            new { CustomerName = "Hina", Phone = "03012345678" },
            "Lines", Probe.Lines([(1, 1, null, null)]), TableTypes.OrderLine);

        //  The scalars are held as a template and are not enumerated until
        //  Dapper builds the command, so ParameterNames shows only what was
        //  added explicitly. What matters is that the table went in through
        //  Add as an ICustomQueryParameter — nested in an anonymous object it
        //  is treated as an ordinary value and fails at execution with
        //  "No mapping exists from object type Dapper.TableValuedParameter".
        Assert.Contains("Lines", parameters.ParameterNames);
        Assert.IsAssignableFrom<DapperCustom>(parameters.Get<object>("Lines"));
    }

    [Fact]
    public void A_table_parameter_works_with_no_scalars_at_all()
    {
        var parameters = Probe.WithTable(null, "Ids", Probe.Ints([1, 2]), TableTypes.IntList);

        Assert.Equal(["Ids"], parameters.ParameterNames);
    }
}

public class DateOnlyTypeHandlerTests
{
    private static readonly DateOnlyTypeHandler Handler = new();

    /// <summary>
    /// The regression test for a 500 on the dashboard and the order date filter:
    /// SqlClient refuses a DateOnly parameter outright.
    /// </summary>
    [Fact]
    public void A_date_is_sent_as_a_DATE_parameter_at_midnight()
    {
        var parameter = new Microsoft.Data.SqlClient.SqlCommand().CreateParameter();

        Handler.SetValue(parameter, new DateOnly(2026, 9, 8));

        Assert.Equal(DbType.Date, parameter.DbType);
        Assert.Equal(new DateTime(2026, 9, 8, 0, 0, 0), parameter.Value);
    }

    [Fact]
    public void A_datetime_read_back_from_SQL_becomes_a_date()
        => Assert.Equal(new DateOnly(2026, 9, 8), Handler.Parse(new DateTime(2026, 9, 8, 13, 45, 0)));

    [Fact]
    public void A_date_read_back_is_left_alone()
        => Assert.Equal(new DateOnly(2026, 9, 8), Handler.Parse(new DateOnly(2026, 9, 8)));

    [Fact]
    public void A_string_is_parsed()
        => Assert.Equal(new DateOnly(2026, 9, 8), Handler.Parse("2026-09-08"));

    [Fact]
    public void Anything_else_fails_loudly_rather_than_guessing()
        => Assert.Throws<DataException>(() => Handler.Parse(42));
}

public class AuthOptionsTests
{
    [Theory]
    [InlineData("https://saadsshop.pk")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("https://api.saadsshop.pk:8443")]
    public void A_bare_scheme_host_and_port_is_a_well_formed_origin(string origin)
        => Assert.True(AuthOptions.IsWellFormedOrigin(origin));

    [Theory]
    // The classic footgun: a trailing slash matches nothing, and the only
    // symptom is a CORS error in a browser console the server never sees.
    [InlineData("https://saadsshop.pk/")]
    [InlineData("http://localhost:5173/")]
    // A path is not part of an Origin header.
    [InlineData("https://saadsshop.pk/app")]
    [InlineData("https://saadsshop.pk/?x=1")]
    [InlineData("https://saadsshop.pk/#fragment")]
    // Not an absolute URL at all.
    [InlineData("saadsshop.pk")]
    [InlineData("//saadsshop.pk")]
    [InlineData("")]
    [InlineData("   ")]
    // Wrong scheme.
    [InlineData("ftp://saadsshop.pk")]
    [InlineData("file:///etc/passwd")]
    public void Anything_else_is_refused_at_startup(string origin)
        => Assert.False(AuthOptions.IsWellFormedOrigin(origin));

    [Fact]
    public void The_defaults_are_the_safe_ones()
    {
        var options = new AuthOptions();

        Assert.Empty(options.AllowedOrigins);          // same-origin only
        Assert.True(options.RefreshCookieSameSiteStrict);
        Assert.Equal(5, options.MaxFailedAccessAttempts);
        Assert.Equal(15, options.LockoutMinutes);
    }
}

public class JwtOptionsTests
{
    [Fact]
    public void The_access_token_is_short_and_the_refresh_token_is_not()
    {
        var options = new JwtOptions();

        Assert.Equal(15, options.AccessTokenMinutes);
        Assert.Equal(14, options.RefreshTokenDays);
    }

    [Fact]
    public void The_rotation_window_defaults_to_twenty_seconds()
        => Assert.Equal(20, new JwtOptions().RefreshRotationGraceSeconds);

    [Fact]
    public void The_challenge_token_is_measured_in_minutes()
        => Assert.Equal(5, new JwtOptions().TwoFactorChallengeMinutes);

    [Theory]
    [InlineData("change-me")]
    [InlineData("CHANGE_ME")]
    [InlineData("your-256-bit-secret")]
    [InlineData("supersecretkey")]
    [InlineData("development-only-signing-key-change-me")]
    public void A_key_copied_from_a_sample_is_a_known_placeholder(string key)
        => Assert.Contains(key, JwtOptions.ForbiddenKeys);
}

public class TwoFactorServiceTests
{
    private static TwoFactorService Service()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:TotpIssuer"] = "Saad's Shop" })
            .Build());

    [Fact]
    public void A_secret_is_base32_and_long_enough_for_the_algorithm()
    {
        var secret = Service().GenerateSecret();

        Assert.Equal(32, secret.Length);   // 20 bytes, base32
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void Two_secrets_differ()
    {
        var service = Service();
        Assert.NotEqual(service.GenerateSecret(), service.GenerateSecret());
    }

    [Fact]
    public void The_authenticator_uri_names_the_shop_and_the_account()
    {
        var uri = Service().BuildAuthenticatorUri("saad@saadsshop.pk", "JBSWY3DPEHPK3PXP");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("saad%40saadsshop.pk", uri);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri);
        Assert.Contains("issuer=", uri);
    }

    [Fact]
    public void Ten_recovery_codes_are_generated_by_default_and_all_differ()
    {
        var codes = Service().GenerateRecoveryCodes();

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
    }

    [Fact]
    public void The_number_of_recovery_codes_is_configurable()
        => Assert.Equal(4, Service().GenerateRecoveryCodes(4).Count);

    [Fact]
    public void A_recovery_code_is_readable_enough_to_write_on_paper()
    {
        var code = Service().GenerateRecoveryCodes(1)[0];

        Assert.Contains('-', code);
        Assert.DoesNotContain(' ', code);
    }

    [Fact]
    public void Hashing_a_recovery_code_is_deterministic_and_case_insensitive()
    {
        var service = Service();

        Assert.Equal(service.HashRecoveryCode("abcd-efgh"), service.HashRecoveryCode("abcd-efgh"));
        Assert.Equal(service.HashRecoveryCode("abcd-efgh"), service.HashRecoveryCode("ABCD-EFGH"));
        Assert.NotEqual(service.HashRecoveryCode("abcd-efgh"), service.HashRecoveryCode("ijkl-mnop"));
    }

    [Fact]
    public void A_recovery_code_hash_is_a_sha256()
        => Assert.Equal(32, Service().HashRecoveryCode("abcd-efgh").Length);

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("0000000")]
    public void A_code_that_is_not_six_digits_is_refused(string code)
        => Assert.False(Service().VerifyCode("JBSWY3DPEHPK3PXP", code));

    [Fact]
    public void A_code_for_the_wrong_secret_is_refused()
        => Assert.False(Service().VerifyCode("JBSWY3DPEHPK3PXP", "000000"));
}
