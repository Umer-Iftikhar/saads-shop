# Backend — SaadsShop.Api

ASP.NET Core 10 Web API. Dapper over stored procedures, JWT with rotating
refresh tokens, TOTP two-factor, Google OAuth, Serilog, `IMemoryCache`.

Layer boundaries and the reasoning behind them are in
[`../docs/architecture.md`](../docs/architecture.md); the database contract is in
[`../docs/database.md`](../docs/database.md).

## Running it

```bash
# 1. Database first
docker compose up -d mssql                       # from the repo root
MSSQL_PASSWORD='Sh0p!Rawalpindi#2026' ./database/apply.sh --demo

# 2. Secrets — never committed
cd backend/src/SaadsShop.Api
dotnet user-secrets set "ConnectionStrings:SaadsShop" \
  "Server=localhost,1433;Database=SaadsShop;User Id=sa;Password=<password>;TrustServerCertificate=true;Encrypt=true;"
dotnet user-secrets set "Jwt:SigningKey" "$(head -c 48 /dev/urandom | base64)"

# 3. Run
dotnet run          # Swagger at /swagger in development
```

Startup **fails loudly** if the connection string is missing, or if the JWT
signing key is absent, shorter than 32 bytes, or still a known placeholder. A
misconfigured deployment should refuse to start rather than come up quietly
signing tokens with a guessable key.

Google sign-in is optional: leave `Authentication:Google` blank and the shop runs
on password + 2FA, with `/api/auth/google` answering 404.

## Layout

```
Controllers/    thin — bind, authorise, call a service, map the result
DTOs/           Request / Response / Internal
Services/       Interfaces + Implementations — business rules, caching
Repositories/   Interfaces + Implementations — one procedure per method
Models/         POCOs Dapper materialises
Constants/      procedure names, table types, cache keys, roles, policies
Validation/     custom validation attributes
Middlewares/    correlation id, security headers, exception handling
Configuration/  options, validated at startup
Data/           connection factory
Extensions/     DI registration
Common/         enums, phone normalisation
```

## Things worth knowing before changing this

**Table-valued parameters need `DynamicParameters`.** Dapper only honours
`ICustomQueryParameter` when it is added to `DynamicParameters`; nested in an
anonymous object it is treated as an ordinary value and fails at execution with
*"No mapping exists from object type Dapper.TableValuedParameter"*. Use
`RepositoryBase.WithTableParameter`.

**Result sets are read positionally.** Every procedure emits its payload sets in
a fixed order and the status row last, and it keeps that shape even while
failing. Reading a set out of order does not throw — it silently maps the wrong
columns. When adding a set to a procedure, add it to the repository in the same
place.

**Money never comes from the client.** `CartLineRequest` has no price field.
Prices, delivery and totals are recomputed inside the checkout transaction from
`Products` and `ShopSettings`.

**`amr`, not a database flag.** The `MfaVerified` policy reads the token's
authentication-methods claim, so a token minted before 2FA was enrolled cannot
satisfy it however the account now looks.

## Verified behaviour

Run against the live API on SQL Server 2022 with the seeded catalogue:

| Check | Result |
| --- | --- |
| Catalogue, categories, swatches, bed sizes, public settings | ✅ |
| Storefront responses withhold stock counts and disabled payment methods | ✅ |
| Order placed; `+92 345 …` normalised to `0345 …`; free delivery over Rs 5,000 | ✅ |
| Order under the threshold charged Rs 300 | ✅ |
| **Client posting `unitPrice: 1` charged the real Rs 22,000** | ✅ |
| Malformed phone → `400` with a field-keyed error | ✅ |
| Disabled payment method → `409` | ✅ |
| Quantity beyond stock → `409` naming the item | ✅ |
| Quantity beyond the per-line cap → `400` before reaching SQL | ✅ |
| Set builder rejects an umbrella in the parde or cushion slot → `400` | ✅ |
| `/api/admin/*` without a token → `401` | ✅ |

`dotnet build` completes with **0 warnings, 0 errors**.

## Not here yet

Unit and integration tests are the next phase — see the repository's open pull
requests. The concurrency and rotation checks described in
[`../database/README.md`](../database/README.md) were run by hand against the
procedures and become automated tests there.
