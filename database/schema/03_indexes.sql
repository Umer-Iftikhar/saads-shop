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
