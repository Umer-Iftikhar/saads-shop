# Architecture

## Shape of the system

```
┌─────────────────────────────┐        ┌──────────────────────────────────────┐
│  React + TypeScript (Vite)  │  HTTPS │        ASP.NET Core Web API          │
│                             │ ─────► │                                      │
│  storefront/  shop panel/   │  JWT   │  Controllers → Services → Repos      │
└─────────────────────────────┘        └───────────────┬──────────────────────┘
                                                       │ Dapper, stored procs only
                                                       ▼
                                              ┌─────────────────┐
                                              │  SQL Server     │
                                              │  validation +   │
                                              │  transactions   │
                                              └─────────────────┘
```

## Backend layout

One ASP.NET Core project, organised by layer:

```
backend/src/SaadsShop.Api/
├── Controllers/       thin — bind, authorise, call a service, map the result
├── DTOs/
│   ├── Request/       what comes in, with validation attributes
│   ├── Response/      what goes out; never a model straight from the database
│   └── Internal/      ProcedureResult, OperationResult, PagedResult — never on the wire
├── Services/
│   ├── Interfaces/
│   └── Implementations/   business rules, caching, orchestration
├── Repositories/
│   ├── Interfaces/
│   └── Implementations/   one stored procedure per method, via Dapper
├── Models/            POCOs Dapper materialises from result sets
├── Constants/         procedure names, table types, cache keys, roles, policies
├── Validation/        custom validation attributes (DateRange, NotFutureDate…)
├── Middlewares/       correlation id, security headers, exception handling
├── Configuration/     strongly-typed options, validated at startup
├── Data/              connection factory
├── Extensions/        DI registration
└── Common/            enums, phone-number normalisation
```

### Why the DTO split

`Request` and `Response` are separate types even where they look alike, because
they answer to different pressures. A request carries validation attributes and
accepts only what a client is allowed to set — `CartLineRequest` has no price
field at all, so a tampered cart has nothing to tamper with. A response carries
only what a caller should see: `ProductSummaryResponse` exposes `InStock` rather
than the stock count, because how many are left is the shop's business.

Reusing one type for both is how a model ends up quietly serialising a password
hash. `Internal` holds the types that never cross the wire at all.

### Layer rules

- Controllers never touch a repository, and never branch on a response code —
  `ApiControllerBase.FromResult` owns that mapping so it exists once.
- Services never open a connection or name a procedure.
- Repositories never make a decision: they run one procedure and hand back what
  it reported, code and all.
- Nothing outside `Constants/` contains a procedure name or a table-type name.

Services take repository *interfaces*, so a service can be unit-tested with a
fake and no database at all.

## Request flow

1. **Controller** — model binding, `[Authorize]` policy check, nothing else. Thin by design.
2. **Validator** — FluentValidation runs on the request DTO before the service is touched.
   Failures return `400` with a field-keyed error map.
3. **Service** — business rules, cache reads, orchestration. Owns the "what".
4. **Repository** — opens a connection, calls exactly one stored procedure through Dapper's
   `QueryMultipleAsync`, maps the payload plus the `(ResponseCode, ResponseMessage)` status
   row into a `ProcedureResult<T>`.
5. **Stored procedure** — re-validates every input, enforces invariants, runs inside a
   transaction where more than one table changes, and always returns a status row.

Validation happens at **three** layers on purpose: the browser for feedback, the API for
correctness, and the database for truth. The database layer is the one that cannot be
bypassed — an operator with `sqlcmd` gets the same rules the API does.

## Caching

`IMemoryCache` fronts the read-heavy, rarely-written data:

| Cache key | Holds | TTL | Invalidated by |
| --- | --- | --- | --- |
| `catalog:products:v{n}` | Active product list | 10 min | product create/update/delete, stock change |
| `catalog:product:{id}:v{n}` | Single product + swatches | 10 min | same |
| `settings:shop` | Shop settings, delivery charge, payment toggles | 30 min | settings update |
| `dashboard:stats:{date}` | Overview tiles + chart | 2 min | order state change |

Invalidation is **explicit and versioned**: writes bump a monotonic version counter, so a
stale entry can never be served after a successful write, and there is no key-scanning.
Entries carry a size and the cache has a bounded `SizeLimit`, so a large catalogue cannot
exhaust memory. Prices, stock counts and order state are **never** served from cache
during checkout — that path reads through to the database under lock.

## Concurrency and checkout

Stock is the one place two customers genuinely collide. The checkout procedure:

1. Opens a `SERIALIZABLE`-equivalent scope via `UPDLOCK, HOLDLOCK` hints on the product
   rows being purchased, taken **in a deterministic order** (ascending `ProductId`) so two
   concurrent checkouts can never deadlock by grabbing rows in opposite order.
2. Re-reads live stock inside the lock — never trusting the quantity the client sent.
3. Fails the whole order with a specific response code if any line is short.
4. Decrements stock, writes the order and its lines, and commits as one transaction.

Details and the exact procedure in [`database.md`](database.md).

## Logging

Serilog writes structured events to console (dev) and a rolling daily file (prod), with
request logging and a correlation id per request. Log levels: `Information` for state
changes (order placed, stock adjusted), `Warning` for rejected input and failed auth,
`Error` for unhandled faults. **Never logged:** passwords, tokens, TOTP secrets, full phone
numbers, or connection strings.

## Errors

A global exception middleware turns anything unhandled into RFC 7807
`application/problem+json` with a correlation id — and no stack traces or SQL text in the
response body. Expected failures (validation, not-found, conflict) never reach it; services
return them as typed results the controller maps to the right status code.
