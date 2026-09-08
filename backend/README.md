# Backend — SaadsShop.Api

ASP.NET Core 10 Web API. Dapper over stored procedures, JWT with rotating
refresh tokens, TOTP two-factor, Google OAuth, Serilog, `IMemoryCache`.

Reads and writes are separate interfaces at every layer — the storefront's
catalogue controller holds a type with no method that can change a product. Layer
boundaries and the reasoning behind them, including why this is not MediatR, are
in [`../docs/architecture.md`](../docs/architecture.md); the database contract is
in [`../docs/database.md`](../docs/database.md).

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
Services/       Interfaces + Implementations, each split Queries / Commands
Repositories/   Interfaces + Implementations, each split Queries / Commands
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

## Two bugs the shop panel found

Both were invisible to the compiler and to reading, and both turned up the first
time a browser drove the whole flow:

**Every 2FA sign-in answered "that sign-in attempt has expired."**
`JwtSecurityTokenHandler` ships with an inbound claim-type map that quietly
renames the registered claims — `sub` arrives as `ClaimTypes.NameIdentifier`, a
WS-Federation URI. `ValidateTwoFactorChallengeToken` then looked for `sub`, found
nothing, and reported no user on a perfectly valid token. `TokenService` now
clears that map, so a token reads back exactly as it was written. The controllers
were unaffected because `CurrentUserId` already fell back to the mapped name —
which is precisely why nothing else caught it.

**`DateOnly` cannot be a SQL parameter.** Microsoft.Data.SqlClient throws
*"The member Today of type System.DateOnly cannot be used as a parameter value"*
when the command is built, so the dashboard and the order date filter both failed
at runtime with a 500. `Data/DateOnlyTypeHandler` teaches Dapper the conversion
in one place, which keeps `DateOnly` in the repository signatures where it
belongs — a date filter is a date, not an instant.

## Tests

```bash
dotnet test                     # 422 tests, ~3s
dotnet test --filter "FullyQualifiedName~Services"
```

`tests/SaadsShop.UnitTests` covers every layer that holds a decision:

| Area | What is asserted |
| --- | --- |
| `Controllers/` | The one mapping from `OperationResult` to HTTP — status, problem+json shape, correlation id, `Location` on a create — plus the refresh cookie's flags, and that a dead refresh token is cleared from the browser |
| `Middlewares/` | Correlation ids in and out (including a 65-character one from a caller), the security headers, and that an unhandled exception becomes a 500 saying nothing about SQL |
| `Services/` | The whole layer: response-code mapping, cache invalidation on write, order and stock rules, sign-in, rotation and reuse detection, TOTP and recovery codes |
| `Validation/` | `[DateRange]`, `[NotFutureDate]`, `[ReasonableDate]` — the server half of the date rules the panel also applies |
| `Infrastructure/` | `OperationResult` / `ProcedureResult` / `PagedResult`, the versioned cache, `RepositoryBase`'s status-row reading, the `DateOnly` handler, options binding |
| `Common/` | Phone normalisation, which decides whether two customers are one |

Doubles are NSubstitute for repositories and a real in-memory `FakeCache` — the
caching behaviour under test *is* the interaction between reads and version
bumps, so a mock returning canned values would assert nothing.

**One product bug came out of writing these.** Fifteen services guarded a
procedure call as `if (!result.IsSuccess || result.Data is null)` and then built
the failure from `result.ResponseCode`. That is right when the procedure failed
and wrong when it returned 200 with no rows: the "failure" carried a success
code, `IsSuccess` read it back as success, and the controller answered 200 with
an empty body. `OperationResult.FromProcedureFailure` now turns that case into a
500 that says so, and every call site uses it.

## Integration tests

```bash
dotnet test tests/SaadsShop.IntegrationTests    # 110 tests
```

The unit suite proves the C# reads a procedure's answer correctly. Nothing in
it can prove the procedure gives the right answer, because there is no
database — and in this project the rules live in the procedures. So this suite
starts SQL Server in a container, applies the real files from `database/` in
the order `apply.sh` uses, and calls the real repositories.

| Area | What is asserted |
| --- | --- |
| Checkout concurrency | Eight buyers race for one piece and exactly one wins; twenty buyers against five pieces sell exactly five and stock never goes negative; a part-available order rolls back whole; six simultaneous orders from one phone make one customer, not six |
| Refresh tokens | Rotation, reuse detection, and that a replay revokes the whole family while leaving other devices signed in — plus ten tabs refreshing at once never leaving two usable tokens |
| Order lifecycle | References unique under concurrency, prices taken from the shop and not the browser, a line keeping the name and price it sold at after the product is renamed, status moves recorded with who made them, and tracking that needs the phone as well as the reference |
| Catalogue | Slugs, name conflicts, cloth sets replaced wholesale rather than merged |
| Archive and restore | That "delete" never removes a row, records who archived it and when, keeps it off the storefront and out of the ordinary list, and can be undone — including the two ways a restore is refused |
| The date range | The third layer of the rule the browser and `[DateRange]` also apply |
| Deployment | The scripts apply to an empty server; every name in `StoredProcedures` exists; nothing in the database is uncalled; no procedure returns a code the API cannot map; no procedure catches an error without logging it |

Docker is required. Set `SAADSSHOP_TEST_SQL` to a connection string to run
against a server you already have instead, and no container is started.

**Two defects came out of writing these**, both invisible without a database:

1. **Refresh-token reuse could never be reported.** On the replay path
   `usp_RefreshToken_Redeem` sets `@UserId = NULL` and then selects the reuse
   flag `FROM dbo.Users WHERE Id = @UserId` — which returns *no row*. The flag
   never reached the API, so the `LogWarning` in `AuthCommandService` that
   exists precisely to record a stolen token was unreachable. The family was
   still revoked correctly, so the hole was in the alerting, not the defence.
2. **The date range was validated twice, not three times.** The procedure
   checked only that the start was before the end. Nothing in it refused a
   future date or a range longer than a year, though both READMEs and
   `docs/database.md` describe three layers applying the same rules.

## Not covered here

The API's HTTP surface. These tests call repositories, which is where SQL
begins; routing, model binding and the auth middleware are covered by the unit
suite instead. An end-to-end suite over `WebApplicationFactory` would close
that gap and does not exist yet.
