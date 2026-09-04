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

## Backend projects

| Project | Depends on | Contains |
| --- | --- | --- |
| `SaadsShop.Domain` | — | POCO entities, enums, domain constants. No attributes, no EF, no framework types. |
| `SaadsShop.Application` | Domain | Service interfaces + implementations, DTOs, FluentValidation validators, repository *interfaces*, caching policy, mapping. |
| `SaadsShop.Infrastructure` | Application, Domain | Dapper repository implementations, `IDbConnectionFactory`, Identity stores, JWT/refresh-token services, TOTP, memory-cache adapter. |
| `SaadsShop.Api` | all | Controllers, middleware, DI wiring, Serilog, auth configuration, Swagger. |

Dependencies point **inward**. `Application` never references `Infrastructure`; the API
composes them at startup. That keeps services unit-testable with a faked repository and no
database.

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
