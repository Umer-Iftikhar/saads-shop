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
| `ResponseCode` | `INT` | `0` on success; non-zero identifies the exact failure (table below) |
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

| Range | Class | Maps to HTTP |
| --- | --- | --- |
| `0` | Success | 200 / 201 |
| `1000–1999` | Input validation | 400 |
| `2000–2999` | Not found | 404 |
| `3000–3999` | Business rule / conflict | 409 |
| `4000–4999` | Authorisation | 403 |
| `9000–9999` | Unexpected server-side failure | 500 |

Assigned codes:

| Code | Constant | Meaning |
| --- | --- | --- |
| `0` | `Success` | Operation completed |
| `1001` | `RequiredFieldMissing` | A required parameter was null or blank |
| `1002` | `ValueOutOfRange` | Number outside its permitted range (price, qty, stock) |
| `1003` | `InvalidFormat` | Phone, email or enum value failed its format check |
| `1004` | `StringTooLong` | Text exceeded the column's limit |
| `1005` | `EmptyCart` | Checkout attempted with no lines |
| `2001` | `ProductNotFound` | |
| `2002` | `OrderNotFound` | |
| `2003` | `CustomerNotFound` | |
| `2004` | `SwatchNotFound` | |
| `2005` | `SettingsNotFound` | |
| `3001` | `InsufficientStock` | Requested quantity exceeds live stock |
| `3002` | `DuplicateName` | Product name already in use |
| `3003` | `InvalidStatusTransition` | e.g. Delivered → Measuring |
| `3004` | `OrderNotCancellable` | Order already delivered or cancelled |
| `3005` | `PaymentMethodDisabled` | Method turned off in settings |
| `3006` | `ProductInUse` | Delete blocked by existing order lines |
| `4001` | `SessionInvalid` | Refresh token missing, expired, revoked or replayed |
| `4002` | `TwoFactorCodeInvalid` | TOTP or recovery code rejected |
| `9001` | `UnexpectedError` | Caught in `CATCH`, logged, details not exposed |

The `4001` message is deliberately the same — "Please sign in again." — whether
the token was unknown, expired, revoked or replayed. The distinction is recorded
in the log and in the `ReuseDetected` flag, not handed to whoever presented it.

The C# mirror lives in `SaadsShop.Domain/ResponseCodes.cs` and the two are asserted equal
by an integration test, so they cannot drift.

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
    @ActorId   NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;          -- any error aborts the whole transaction

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        -- 1. validate every input before touching a table
        IF @ProductId IS NULL OR @ProductId <= 0
        BEGIN
            SELECT CAST(NULL AS INT) AS ProductId WHERE 1 = 0;   -- keep the shape
            SELECT 1001 AS ResponseCode, N'Product id is required.' AS ResponseMessage;
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
        SELECT 9001 AS ResponseCode, N'Something went wrong. Please try again.' AS ResponseMessage;
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
    SELECT 3001 AS ResponseCode, N'One of the items just went out of stock.' AS ResponseMessage;
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
exactly one succeeds and the rest get `3001`, with stock landing at 0 and never negative.

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
