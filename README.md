# Saad's Shop

Storefront and shop panel for **Saad's Shop** — a bed sheet, curtain, wedding set and
umbrella shop at Shop 14, Moti Bazaar, Raja Bazaar, **Rawalpindi**.

Customers browse bridal bedding and room packages, build a matched set (bistar + parde +
cushions) in a live room preview, and order cash-on-delivery, over WhatsApp, or by
reserving cloth to pay in the shop. Saad and the stitching floor run the whole thing from
the shop panel: orders, inventory, the stitching queue, customers and settings.

Prices are in **PKR (Rs)**. The copy is English with Urdu accents (*bistar*, *parde*,
*chhata*, *jahez*, *shaadi*) — that is deliberate; keep it.

> Built from a Claude Design handoff. The original prototype, the design-system tokens and
> the full design conversation are preserved in [`docs/design-handoff/`](docs/design-handoff/).

---

## Stack

| Layer | Choice | Why |
| --- | --- | --- |
| Frontend | React 19 + TypeScript + Vite | Typed components, fast dev loop, no SSR needed for a shop this size |
| Backend | ASP.NET Core 9 Web API | Requested; services + repositories, POCO models |
| Data access | **Dapper**, stored procedures only | No inline SQL anywhere in C# — see [`docs/database.md`](docs/database.md) |
| Database | SQL Server 2022 (MSSQL) | Validation and transactions live in the procs |
| Auth | ASP.NET Identity + JWT + rotating refresh tokens + Google OAuth + TOTP 2FA | Staff only today, customer-ready schema |
| Logging | Serilog (console + rolling file, request logging) | |
| Caching | `IMemoryCache` with explicit invalidation on write | |
| Tests | xUnit — unit + integration (Testcontainers MSSQL) | Integration tests run the *real* stored procedures |

## Repository layout

```
backend/          ASP.NET Core solution (Api / Application / Domain / Infrastructure + tests)
database/         Schema, stored procedures, seed data — the source of truth for the DB
frontend/         React + TypeScript single-page app (storefront + shop panel)
docs/             Architecture, database contract, API reference, security notes
docs/design-handoff/  Original Claude Design prototype, tokens and chat transcript
```

## Documentation

| Doc | What's in it |
| --- | --- |
| [`docs/architecture.md`](docs/architecture.md) | Layers, request flow, caching, project dependencies |
| [`docs/database.md`](docs/database.md) | Stored-procedure contract, response codes, locking strategy |
| [`docs/api.md`](docs/api.md) | Endpoint reference, envelope shape, error format |
| [`docs/security.md`](docs/security.md) | Auth flows, token rotation, 2FA, threat notes |
| [`docs/design-system.md`](docs/design-system.md) | Colour ramps, type scale, spacing, component classes |
| [`docs/contributing.md`](docs/contributing.md) | Branching, commits, PRs, issue workflow |

## Getting started

Prerequisites: **.NET 9 SDK**, **Node 20+**, and **SQL Server 2022** (Docker is fine).

```bash
# 1. Database — start SQL Server and apply schema + procs + seed
docker compose up -d mssql
./database/apply.sh                      # idempotent; safe to re-run

# 2. Backend
cd backend
dotnet restore
dotnet user-secrets set "ConnectionStrings:SaadsShop" "<your connection string>" \
  --project src/SaadsShop.Api
dotnet run --project src/SaadsShop.Api    # https://localhost:7178

# 3. Frontend
cd frontend
npm install
npm run dev                               # http://localhost:5173
```

Secrets (connection string, JWT signing key, Google OAuth client secret) are read from
user-secrets in development and environment variables in production. **Nothing secret is
committed** — see [`docs/security.md`](docs/security.md).

## Screens

**Storefront** — Home · Wedding-set listing · Product detail (clickable fabric swatches) ·
Build your set (live room preview) · Cart & checkout · Order placed

**Shop panel** — Overview (sales, chart, best sellers) · Orders · Order detail · Inventory ·
Product editor · Stitching queue · Customers · Settings · Staff login + 2FA

## Project status

Delivered in phases, one branch and one pull request each. See the
[issues](../../issues) and [pull requests](../../pulls) for current state.

## A note on photography

The design uses woven CSS colour patterns as fabric stand-ins — there are no product
photographs yet. Image upload is wired end to end so real photos can be dropped in without
touching the layout.
