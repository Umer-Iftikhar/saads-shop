# Database contract

SQL Server 2022. **Dapper calls stored procedures and nothing else** — there is no inline
SQL, no string-built query, and no ORM-generated statement anywhere in the C#. Every
procedure validates its own inputs and returns a status row, so the rules hold no matter
who is calling.

## The procedure contract

Every procedure returns **its payload result sets first, and a status result set last**:

```sql
SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
```

| Column | Type | Meaning |
| --- | --- | --- |
| `ResponseCode` | `INT` | An HTTP status code — `200` on success, otherwise the status the API returns verbatim (table below) |
| `ResponseMessage` | `NVARCHAR(400)` | Human-readable, safe to show a shop user. Never contains SQL, table names or internals. |

C# reads it with a single `QueryMultipleAsync`:

```csharp
await using var multi = await connection.QueryMultipleAsync(
    "usp_Product_GetById",
    new { ProductId = id },
    commandType: CommandType.StoredProcedure);

var product  = await multi.ReadSingleOrDefaultAsync<ProductDto>();
var swatches = (await multi.ReadAsync<SwatchDto>()).AsList();
var status   = await multi.ReadSingleAsync<ProcedureStatus>();   // always last
```

`ProcedureResult<T>` wraps `(T? Data, int ResponseCode, string ResponseMessage)`. A
repository never inspects `ResponseCode` to make a decision — it hands the whole result up
to the service, which maps codes to outcomes. That keeps the mapping in one place.

### Why the status row is last

A payload set can be empty or absent depending on the branch taken inside the procedure,
but the status row is unconditional. Reading it last means the caller always knows where
it is, and a procedure can add payload sets later without breaking the reader.

## Response codes

**A procedure's `ResponseCode` is an HTTP status code.** There is no translation
table between the database and the wire, and therefore no way for the two to
disagree — what a procedure decides is exactly what the caller sees.

| Code | Meaning | Raised when |
| --- | --- | --- |
| `200` | Success | The operation completed |
| `400` | Validation failed | Missing field, bad format, value out of range, empty cart |
| `401` | Unauthorised | Refresh token missing, expired, revoked or replayed; 2FA code rejected |
| `403` | Forbidden | Authenticated but not permitted |
| `404` | Not found | No such product, order, customer, cloth or settings row |
| `409` | Conflict | Insufficient stock, duplicate name, illegal status move, disabled payment method, product still referenced by orders |
| `429` | Too many requests | Rate limit (applied at the API, not in SQL) |
| `500` | Server error | Caught in the procedure's `CATCH`, logged there, details never exposed |

The C# mirror is `backend/src/SaadsShop.Api/Constants/ResponseCodes.cs`, and an
integration test asserts every code a procedure can return appears there.

### What the code does not tell you

Collapsing onto HTTP statuses means the code alone no longer says *which* rule
refused: an out-of-stock line and a duplicate product name are both `409`. That
detail lives in `ResponseMessage`, which is written to be shown to a shopkeeper
or a customer unchanged — "Compact Chhata just went out of stock." names the
offending line precisely enough for the cart to highlight it.

Two consequences worth knowing:

- **`401` is deliberately uniform.** A refresh token that is unknown, expired,
  revoked or replayed all return `401` with the same "Please sign in again."
  The distinction is recorded in the log and in the redemption's
  `ReuseDetected` flag, never handed back to whoever presented the token.
- **Client branching should key on status + endpoint, not on message text.**
  Messages are copy and will be reworded; a cart that greps for the word
  "stock" will break the first time someone edits the wording.

## Naming

| Kind | Pattern | Example |
| --- | --- | --- |
| Table | `PascalCase`, plural | `Products`, `OrderLines` |
| Procedure | `usp_{Entity}_{Action}` | `usp_Order_Create`, `usp_Product_UpdateStock` |
| Parameter | `@PascalCase` matching the DTO property | `@ProductId` |
| Index | `IX_{Table}_{Columns}` | `IX_Orders_Status_PlacedAt` |

Matching parameter names to DTO property names lets Dapper bind the anonymous object
directly, with no manual `DynamicParameters` and no chance of a silent mismatch.

## Standard procedure skeleton

```sql
CREATE OR ALTER PROCEDURE usp_Product_UpdateStock
    @ProductId INT,
    @Delta     INT,
    @ActorId   NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;          -- any error aborts the whole transaction

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        -- 1. validate every input before touching a table
        IF @ProductId IS NULL OR @ProductId <= 0
        BEGIN
            SELECT CAST(NULL AS INT) AS ProductId WHERE 1 = 0;   -- keep the shape
            SELECT 400 AS ResponseCode, N'Product id is required.' AS ResponseMessage;
            RETURN;
        END
        ...
        BEGIN TRANSACTION;
        ...
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        -- log internally, expose nothing
        INSERT INTO ErrorLog (ProcedureName, ErrorMessage, ErrorLine, OccurredAt)
        VALUES (ERROR_PROCEDURE(), ERROR_MESSAGE(), ERROR_LINE(), SYSUTCDATETIME());
        SELECT 500 AS ResponseCode, N'Something went wrong. Please try again.' AS ResponseMessage;
    END CATCH
END
```

Note `SET XACT_ABORT ON` — without it a non-fatal error inside a transaction can leave the
transaction open and the connection poisoned for the next caller from the pool.

## Checkout: locking and race conditions

Two customers buying the last Jaali Sheer Panel is the collision that actually matters.
`usp_Order_Create` handles it like this:

```sql
BEGIN TRANSACTION;

-- Lock the exact product rows, ascending by id so concurrent checkouts
-- always acquire in the same order and cannot deadlock each other.
SELECT p.ProductId, p.Stock, p.Price, p.IsActive
INTO   #locked
FROM   Products AS p WITH (UPDLOCK, HOLDLOCK)
JOIN   @Lines AS l ON l.ProductId = p.ProductId
ORDER  BY p.ProductId;

-- Re-read stock under the lock; the client's numbers are never trusted.
IF EXISTS (SELECT 1 FROM #locked k JOIN @Lines l ON l.ProductId = k.ProductId
           WHERE k.Stock < l.Quantity)
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 409 AS ResponseCode, N'One of the items just went out of stock.' AS ResponseMessage;
    RETURN;
END

-- Price comes from the table, never from the request.
...
UPDATE Products SET Stock = Stock - l.Quantity ... ;
INSERT INTO Orders ... ; INSERT INTO OrderLines ... ;

COMMIT TRANSACTION;
```

Three things this buys us:

- **`UPDLOCK`** takes an update lock immediately rather than a shared lock that must be
  upgraded — which is the classic conversion-deadlock under load.
- **`HOLDLOCK`** keeps it until commit, so the stock a decision was made on cannot change
  underneath the decision.
- **Ascending `ProductId`** gives a global lock order. Deadlocks need a cycle; a consistent
  order makes a cycle impossible.

Prices and totals are recomputed from `Products` inside the transaction. A tampered client
that posts `price: 1` gets charged the real price — the request's money fields are ignored
entirely.

An integration test fires N concurrent checkouts at a product with stock 1 and asserts
exactly one succeeds and the rest get `409`, with stock landing at 0 and never negative.

## Layout

```
database/
├── schema/
│   ├── 01_tables.sql     tables, constraints, key indexes, the reference sequence
│   ├── 02_types.sql      table-valued parameter types
│   └── 03_indexes.sql    query-support indexes, each naming the screen it serves
├── procedures/
│   ├── 01_catalog.sql    categories, swatches, products, the product editor
│   ├── 02_orders.sql     checkout, set-builder quote, order lookup and status
│   ├── 03_operations.sql inventory, the stitching floor, customers
│   ├── 04_shop.sql       settings (public + panel) and the overview dashboard
│   └── 05_identity.sql   users, roles, external logins, refresh tokens, 2FA codes
├── seed/                 reference data + the catalogue from the design
└── apply.sh              applies everything in order; idempotent
```

Procedures are grouped by area rather than split one-per-file: they share
validation idioms and are read together, and five files stay navigable where
forty would not. Every one is `CREATE OR ALTER`, so re-applying is safe.

Every file is `CREATE OR ALTER` or guarded by an existence check, so `apply.sh` can be run
against a fresh or an existing database with the same result.

## `QUOTED_IDENTIFIER` must be ON for any write

`Products` carries **filtered indexes** (`IX_Products_LowStock`, `IX_Products_SoldCount`,
each `WHERE IsActive = 1`). SQL Server refuses to modify a table with a filtered index from
a session where `QUOTED_IDENTIFIER` is OFF:

```
Msg 1934: UPDATE failed because the following SET options have incorrect
settings: 'QUOTED_IDENTIFIER'.
```

This is not a problem for the application — `Microsoft.Data.SqlClient` sets it ON for every
connection, and a stored procedure runs with the setting captured when it was created, not
the caller's. It bites **ad-hoc scripting**: `sqlcmd -Q "UPDATE Products ..."` fails unless
you pass `-I`. `apply.sh` passes `-I` for exactly this reason.

The failure is easy to miss because `sqlcmd` returns the error on stderr and carries on, so
a maintenance script can appear to succeed while having changed nothing. If a manual fix-up
seems not to have applied, check this first.
