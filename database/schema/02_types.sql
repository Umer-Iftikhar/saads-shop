/*  Saad's Shop — user-defined table types
    ------------------------------------------------------------------------
    Table-valued parameters let a whole cart cross into the database in ONE
    call, so the checkout transaction is opened once and held for the shortest
    possible time. The alternative — a proc call per line — would either hold a
    transaction open across round trips or give up atomicity entirely.

    Types cannot be altered in place; a change means dropping and recreating,
    which fails while a procedure references the type. Hence the drop-dependents
    dance below is deliberately NOT done: instead, apply.sh runs types before
    procedures, and a type change requires dropping the referencing procedures
    first. Keep these shapes stable.
*/

SET NOCOUNT ON;
GO

/*  One line of a cart at checkout.
    Note what is ABSENT: no price, no total. The client does not get a vote on
    money — the checkout procedure reads price from Products under lock.        */
IF TYPE_ID(N'dbo.OrderLineTableType') IS NULL
BEGIN
    CREATE TYPE dbo.OrderLineTableType AS TABLE (
        ProductId INT          NOT NULL,
        Quantity  INT          NOT NULL,
        SwatchId  INT          NULL,
        BedSize   NVARCHAR(16) NULL,
        INDEX IX_OrderLineTableType_ProductId (ProductId)
    );
    /*  No PRIMARY KEY: SwatchId and BedSize are legitimately nullable (an
        umbrella has neither) and SQL Server forbids nullable key columns.
        The same product may also appear twice in one cart in two different
        cloths, which is a real order, not a duplicate. usp_Order_Create
        therefore aggregates quantity per ProductId before checking stock —
        two lines of the same product must not each pass a stock test the
        pair would fail together.                                            */
END
GO

/*  Used by the product editor to replace a product's swatch set atomically.   */
IF TYPE_ID(N'dbo.IntListTableType') IS NULL
BEGIN
    CREATE TYPE dbo.IntListTableType AS TABLE (
        Value INT NOT NULL PRIMARY KEY
    );
END
GO
