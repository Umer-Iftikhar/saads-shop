/*  Saad's Shop — query-support indexes
    ------------------------------------------------------------------------
    Key and uniqueness indexes live with their tables in 01_tables.sql. What
    follows exists purely to serve specific queries the app actually runs; each
    one names the screen it is for, so a future reader can tell whether it is
    still earning its keep.
*/

SET NOCOUNT ON;
GO

/*  Orders list, filtered by status and sorted newest first (shop panel · 08). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status_PlacedAt' AND object_id = OBJECT_ID(N'dbo.Orders'))
    CREATE INDEX IX_Orders_Status_PlacedAt ON dbo.Orders (Status, PlacedAt DESC)
        INCLUDE (Reference, CustomerId, PaymentMethod, Total);
GO

/*  Latest orders on the overview, and the 12-week sales chart (07).          */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_PlacedAt' AND object_id = OBJECT_ID(N'dbo.Orders'))
    CREATE INDEX IX_Orders_PlacedAt ON dbo.Orders (PlacedAt DESC)
        INCLUDE (Status, Total);
GO

/*  A customer's order history, and the Customers screen's per-customer
    aggregates (13).                                                          */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_CustomerId_PlacedAt' AND object_id = OBJECT_ID(N'dbo.Orders'))
    CREATE INDEX IX_Orders_CustomerId_PlacedAt ON dbo.Orders (CustomerId, PlacedAt DESC)
        INCLUDE (Total, Status);
GO

/*  Category listing pages (02) — active products of a category, cheapest
    query the storefront makes and the most frequent.                         */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_Category_Active' AND object_id = OBJECT_ID(N'dbo.Products'))
    CREATE INDEX IX_Products_Category_Active ON dbo.Products (CategoryId, IsActive)
        INCLUDE (Name, Price, Stock, Kicker, Blurb, Pieces, DefaultSwatchId);
GO

/*  Inventory screen's low-stock flag (10). Filtered so the index stays tiny —
    it only ever contains the handful of products actually running out.       */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_LowStock' AND object_id = OBJECT_ID(N'dbo.Products'))
    CREATE INDEX IX_Products_LowStock ON dbo.Products (Stock)
        INCLUDE (Name, CategoryId, LowStockAt)
        WHERE IsActive = 1;
GO

/*  Best sellers on the overview (07).                                        */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_SoldCount' AND object_id = OBJECT_ID(N'dbo.Products'))
    CREATE INDEX IX_Products_SoldCount ON dbo.Products (SoldCount DESC)
        INCLUDE (Name, Price, DefaultSwatchId)
        WHERE IsActive = 1;
GO

/*  Stitching queue board, grouped by stage and ordered by due date (12).     */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StitchingJobs_Stage_DueDate' AND object_id = OBJECT_ID(N'dbo.StitchingJobs'))
    CREATE INDEX IX_StitchingJobs_Stage_DueDate ON dbo.StitchingJobs (Stage, DueDate)
        INCLUDE (OrderId, Title, AssignedTo, SwatchId)
        WHERE Stage <> N'Done';
GO

/*  Refresh-token lookup on every /auth/refresh: hash → row, and the
    family-wide revoke that follows a detected reuse. Filtered to live tokens
    so the index does not carry years of spent ones.                          */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RefreshTokens_Live' AND object_id = OBJECT_ID(N'dbo.RefreshTokens'))
    CREATE INDEX IX_RefreshTokens_Live ON dbo.RefreshTokens (UserId, ExpiresAt)
        INCLUDE (FamilyId, UsedAt, RevokedAt)
        WHERE RevokedAt IS NULL AND UsedAt IS NULL;
GO

/*  Customer search by phone or name in the panel (13) — phone already has a
    unique index; this covers the name search.                                */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Name' AND object_id = OBJECT_ID(N'dbo.Customers'))
    CREATE INDEX IX_Customers_Name ON dbo.Customers (Name)
        INCLUDE (Phone, Area);
GO

/*  ------------------------------------------------------------------------
    Measured, not guessed
    ------------------------------------------------------------------------
    What follows was decided by loading 40,000 orders, 100,000 order lines,
    8,000 customers and 60,000 refresh tokens into a real SQL Server and
    reading the plans. The rejected candidates are recorded alongside the one
    that earned its place, because "why is there no index on that?" is a
    question this file should answer.
*/

/*  Orders list (08). The summary of what was ordered is built from
    ProductName, once per row on screen, and the OrderId index did not carry
    it — so each one was a lookup back into the table. Including it took the
    per-lookup cost from 5.1 logical reads to 3.2, and the whole list screen
    down by 23%.

    ProductName alone, deliberately: adding Quantity, LineTotal and ProductId
    as well measured no better (210,505 reads against 209,155) and every extra
    column is width to maintain on a table written to at every checkout.

    This index is created with the table in 01_tables.sql; the rebuild here is
    for databases created before the INCLUDE was added.                       */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderLines_OrderId' AND object_id = OBJECT_ID(N'dbo.OrderLines'))
   AND NOT EXISTS (SELECT 1
                   FROM   sys.index_columns ic
                   JOIN   sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   WHERE  i.name = N'IX_OrderLines_OrderId'
                     AND  i.object_id = OBJECT_ID(N'dbo.OrderLines')
                     AND  ic.is_included_column = 1
                     AND  COL_NAME(ic.object_id, ic.column_id) = N'ProductName')
BEGIN
    DROP INDEX IX_OrderLines_OrderId ON dbo.OrderLines;
    CREATE INDEX IX_OrderLines_OrderId ON dbo.OrderLines (OrderId) INCLUDE (ProductName);
END
GO

/*  ------------------------------------------------------------------------
    Considered and rejected
    ------------------------------------------------------------------------

    RefreshTokens (ExpiresAt) — for the nightly purge.
        Tempting: ExpiresAt leads no index, and IX_RefreshTokens_Live is
        filtered to live tokens, which is precisely the opposite of what the
        purge wants. Finding the spent tokens does drop from 1,490 logical
        reads to 143.
        But the purge does not read them, it deletes them — 89% of the table
        in one statement — and a fifth index to maintain took the purge itself
        from 497,310 reads to 605,797. A scan is the right plan for a delete
        that large, so there is no index here.

    The seven foreign keys with no index of their own
        (Customers.UserId, InventoryAdjustments.OrderId, Products.DefaultSwatchId,
         ProductSwatches.SwatchId, StitchingJobs.OrderLineId,
         StitchingJobs.SwatchId, UserRoles.RoleId).
        An unindexed foreign key is a common finding, and every one of these
        would be dead weight: no procedure filters on any of them. They are
        insert targets, or they are joined from the other side, where the
        parent's primary key already does the work. They would cost writes and
        earn nothing.

    Widening IX_Orders_PlacedAt with CustomerId and Reference.
        Helped the unfiltered list and made the text search measurably worse
        by flipping its plan. Left alone.

    Anything for the customer and order text search.
        The panel searches with a leading wildcard — "gulaab" has to match
        "Bridal gulaab set" — and no B-tree can seek that. Full-text indexing
        is the answer if it ever becomes a problem; at this shop's size it has
        not.
*/
