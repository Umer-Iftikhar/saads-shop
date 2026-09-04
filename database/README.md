# Database

SQL Server 2022. The contract every procedure honours — response codes, result-set order,
locking strategy — is in [`../docs/database.md`](../docs/database.md). This file is just how
to run it.

## Apply

```bash
docker compose up -d mssql                       # from the repo root
export MSSQL_PASSWORD='Sh0p!Rawalpindi#2026'     # matches docker-compose.yml
./database/apply.sh --demo
```

`apply.sh` is idempotent — schema, types, indexes, procedures, then seed data, in that
order. Re-running it is the normal way to deploy a change, not a repair operation.

| Flag | Effect |
| --- | --- |
| `--demo` | Also seeds the eight orders, customers and floor jobs from the design, plus twelve weeks of history so the overview chart has a shape |
| `--database NAME` | Target a different database (used by the integration tests) |
| `--server`, `--user` | Override `MSSQL_SERVER` / `MSSQL_USER` |

Without `--demo` you get the shop's real starting state: ten products with their opening
stock, the six cloths, the four categories and one settings row. No fake orders.

## What is here

```
schema/       tables and constraints, TVP types, query-support indexes
procedures/   grouped by area — catalogue, orders, operations, shop, identity
seed/         01 reference (roles, categories, cloths, settings)
              02 catalogue (the ten products)
              03 demo      (optional history)
apply.sh
```

## Verifying a change

The integration test suite (`backend/tests/SaadsShop.IntegrationTests`) runs against a real
SQL Server via Testcontainers and exercises the procedures directly, including a
concurrent-checkout test. That is the real check. For a quick manual look:

```bash
docker exec -it saadsshop-mssql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_PASSWORD" -C -d SaadsShop -I
```

**Pass `-I`.** Writes to `Products` fail without it — see the `QUOTED_IDENTIFIER` note in
[`../docs/database.md`](../docs/database.md).

## What was verified when this landed

Run against SQL Server 2022 CU26, fresh database, zero warnings:

| Check | Result |
| --- | --- |
| Schema + 41 procedures apply clean, twice in a row | ✅ |
| Valid checkout prices from the table, free delivery over Rs 5,000 | ✅ |
| Order under Rs 5,000 charged Rs 300 delivery | ✅ |
| Malformed phone rejected (`400`) | ✅ |
| Disabled payment method rejected (`409`) | ✅ |
| Oversell rejected (`409`) | ✅ |
| Two cart lines of one product summed against stock, not checked separately | ✅ |
| **15 concurrent checkouts, stock 1 → exactly 1 success, 14 × `409`, stock 0, no deadlocks** | ✅ |
| Refresh rotation, then replay burns the whole family | ✅ |
| Recovery codes single-use (`401` on reuse) | ✅ |
| Last Owner cannot be demoted (`409`) | ✅ |
| Delivered order cannot be reopened (`409`) | ✅ |
| Disabling every payment method refused (`409`) | ✅ |
