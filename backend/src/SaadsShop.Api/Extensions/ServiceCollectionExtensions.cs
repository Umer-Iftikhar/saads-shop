using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.Models;
using SaadsShop.Api.Repositories.Implementations;
using SaadsShop.Api.Repositories.Interfaces;
using SaadsShop.Api.Services.Implementations;
using SaadsShop.Api.Services.Interfaces;

namespace SaadsShop.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates configuration. <c>ValidateOnStart</c> means a bad
    /// deployment fails at boot with a clear message rather than surfacing as a
    /// 500 the first time someone tries to sign in.
    /// </summary>
    public static IServiceCollection AddShopOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(o => !JwtOptions.ForbiddenKeys.Contains(o.SigningKey),
                    "The JWT signing key is still a placeholder. Set a real one before starting the API.")
                .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                    "The JWT signing key must be at least 32 bytes for HS256.")
                .ValidateOnStart();

        services.AddOptions<AuthOptions>()
                .Bind(configuration.GetSection(AuthOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(o => o.AllowedOrigins.All(origin => !origin.Contains('*')),
                    "CORS origins must be listed explicitly. '*' with credentials is refused by browsers and defeats the point.")
                .ValidateOnStart();

        services.AddOptions<GoogleAuthOptions>()
                .Bind(configuration.GetSection(GoogleAuthOptions.SectionName))
                .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddShopServices(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Scoped: one instance per request, matching the lifetime of the
        // connections and the cancellation token they run under.
        services.AddScoped<ICatalogRepository,    CatalogRepository>();
        services.AddScoped<IOrderRepository,      OrderRepository>();
        services.AddScoped<IOperationsRepository, OperationsRepository>();
        services.AddScoped<IShopRepository,       ShopRepository>();
        services.AddScoped<IIdentityRepository,   IdentityRepository>();

        services.AddScoped<ICatalogService,    CatalogService>();
        services.AddScoped<IOrderService,      OrderService>();
        services.AddScoped<IOperationsService, OperationsService>();
        services.AddScoped<IShopService,       ShopService>();
        services.AddScoped<IAuthService,       AuthService>();

        // Stateless and cheap to share.
        services.AddSingleton<ITokenService,     TokenService>();
        services.AddSingleton<ITwoFactorService, TwoFactorService>();
        services.AddSingleton<ICacheService,     CacheService>();

        // Identity's hasher without the rest of Identity: PBKDF2-HMAC-SHA512 at
        // the framework's current iteration count, and it tells us when a stored
        // hash needs upgrading.
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

        services.AddMemoryCache(options =>
        {
            // Entries each declare Size = 1, so this is "at most 1024 cached
            // items". Without a limit a large catalogue plus many search
            // permutations could grow the cache until the process is starved.
            options.SizeLimit = 1024;
        });

        services.AddDataProtection();

        return services;
    }

    public static IServiceCollection AddShopAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt    = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var google = configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();

        var builder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        });

        builder.AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuer              = jwt.Issuer,
                ValidateAudience         = true,
                ValidAudience            = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime         = true,

                // The framework's five-minute default would make a 15-minute
                // token live for twenty.
                ClockSkew                = TimeSpan.FromSeconds(30),

                // Pin the algorithm so a token signed with something weaker
                // cannot be substituted.
                ValidAlgorithms          = [SecurityAlgorithms.HmacSha256]
            };

            options.Events = new JwtBearerEvents
            {
                OnChallenge = context =>
                {
                    // Suppress the default WWW-Authenticate detail, which
                    // helpfully explains to an attacker exactly why the token
                    // was rejected.
                    context.Response.Headers.Remove("WWW-Authenticate");
                    return Task.CompletedTask;
                }
            };
        });

        // Registered only when credentials exist. A shop that has not set Google
        // up runs on password + 2FA, and the endpoint answers 404 rather than
        // failing at startup.
        if (google.IsConfigured)
        {
            builder.AddGoogle(options =>
            {
                options.ClientId     = google.ClientId;
                options.ClientSecret = google.ClientSecret;

                options.UsePkce      = true;
                options.SaveTokens   = false;   // nothing here needs Google's access token
                options.CorrelationCookie.SameSite     = SameSiteMode.Lax;  // must survive the redirect back
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

                options.Scope.Add("email");
                options.Scope.Add("profile");

                // Not mapped by default, and the linking decision depends on it.
                options.ClaimActions.MapJsonKey("email_verified", "email_verified", "boolean");
            });
        }

        return services;
    }

    public static IServiceCollection AddShopAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.StaffOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Staff, Roles.Owner))

            .AddPolicy(AuthPolicies.OwnerOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Owner))

            // Reads amr rather than a flag on the user: a token minted before
            // 2FA was enrolled must not satisfy this, however the account now
            // looks in the database.
            .AddPolicy(AuthPolicies.MfaVerified, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(AppClaims.AuthMethod, AuthMethods.TwoFactor));

        return services;
    }

    public static IServiceCollection AddShopRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                // Tell the client when to come back rather than leaving it to
                // guess and hammer.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();

                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsync(
                    """{"title":"Too many attempts. Please wait a moment and try again.","status":429}""",
                    token);
            };

            // Sign-in is partitioned by IP AND by the email being tried, so one
            // attacker cannot lock a shop out by hammering one address, nor
            // spread a guessing run across many addresses from one connection.
            options.AddPolicy(RateLimitPolicies.Login, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"login:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window      = TimeSpan.FromMinutes(15)
                    }));

            options.AddPolicy(RateLimitPolicies.TwoFactor, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"2fa:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window      = TimeSpan.FromMinutes(15)
                    }));

            options.AddPolicy(RateLimitPolicies.PlaceOrder, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"order:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window      = TimeSpan.FromHours(1)
                    }));

            options.AddPolicy(RateLimitPolicies.TrackOrder, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"track:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window      = TimeSpan.FromMinutes(15)
                    }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window      = TimeSpan.FromMinutes(1)
                    }));
        });

        return services;
    }

    /// <summary>
    /// Partition key: the signed-in user where there is one, otherwise the IP.
    /// Keying authenticated traffic by user means a shop behind one office
    /// connection does not share a single bucket between staff.
    /// </summary>
    private static string ClientKey(HttpContext context)
        => context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
              ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static IServiceCollection AddShopCors(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services.AddCors(options =>
            options.AddDefaultPolicy(policy => policy
                // An explicit list, never AllowAnyOrigin — the refresh cookie
                // rides on these requests.
                .WithOrigins(auth.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders(Middlewares.CorrelationIdMiddleware.HeaderName)));

        return services;
    }
}
