/*  Saad's Shop — demo orders, customers and floor jobs
    ------------------------------------------------------------------------
    OPTIONAL. apply.sh runs this only with --demo. It exists so the shop panel
    has something to show on a fresh database: the overview chart, the orders
    list and the stitching board are all empty and fairly meaningless without
    history behind them.

    These are the eight orders from the design, dated relative to today so the
    dashboard's "today", "same day last week" and 12-week chart stay sensible
    however long after writing this the database is created.

    Note: these orders do NOT decrement stock. The stock numbers seeded in
    02_catalog.sql are already the shop's current counts — the design's
    figures are what is on the shelf now, after these sales. Replaying the
    decrements would double-count them.
*/

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    PRINT 'Orders already present — skipping demo data.';
    RETURN;
END
GO

DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);

/* ── Customers ───────────────────────────────────────────────────────── */
DECLARE @Customers TABLE (Name NVARCHAR(128), Phone NVARCHAR(20), Area NVARCHAR(96), Address NVARCHAR(400));
INSERT INTO @Customers VALUES
(N'Hina Aslam',      N'03012345678', N'Satellite Town', N'Satellite Town, Block C'),
(N'Bilal Tariq',     N'03328874410', N'Chaklala',       N'Chaklala Scheme 3'),
(N'Ayesha Nadeem',   N'03451129087', N'Raja Bazaar',    N'Raja Bazaar, near Moti'),
(N'Kashif Mehmood',  N'03004452211', N'Peshawar Road',  N'Peshawar Road'),
(N'Sana Rauf',       N'03116603390', N'Adiala Road',    N'Adiala Road'),
(N'Usman Shah',      N'03219091200', N'Westridge',      N'Westridge 1'),
(N'Fatima Zubair',   N'03334478891', N'Bahria Town',    N'Bahria Town Phase 4'),
(N'Rehan Sadiq',     N'03082216754', N'Dhoke Kala Khan',N'Dhoke Kala Khan');

INSERT INTO dbo.Customers (Name, Phone, Area, Address)
SELECT c.Name, c.Phone, c.Area, c.Address
FROM   @Customers AS c
WHERE  NOT EXISTS (SELECT 1 FROM dbo.Customers AS x WHERE x.Phone = c.Phone);

/* ── Orders ──────────────────────────────────────────────────────────── */
DECLARE @Seed TABLE (
    Seq INT, Phone NVARCHAR(20), DaysAgo INT, PaymentMethod NVARCHAR(24),
    Status NVARCHAR(24), ProductName NVARCHAR(128), Quantity INT, SwatchName NVARCHAR(48)
);

INSERT INTO @Seed VALUES
(1, N'03012345678',  0, N'CashOnDelivery', N'Stitching', N'Chandni Room Package',   1, N'Sage'),
(2, N'03328874410',  0, N'WhatsApp',       N'Measuring', N'Gulaab Bridal Set',      1, N'Plum'),
(2, N'03328874410',  0, N'WhatsApp',       N'Measuring', N'Velvet Drape Pair',      1, N'Sage'),
(3, N'03451129087',  1, N'ReserveInShop',  N'Ready',     N'Jahez Six-Piece Bundle', 1, N'Terracotta'),
(4, N'03004452211',  2, N'CashOnDelivery', N'Delivered', N'Mehr Bridal Set',        1, N'Gold'),
(5, N'03116603390',  3, N'CashOnDelivery', N'Delivered', N'Sooti Double Bedsheet',  2, N'Cream'),
(6, N'03219091200',  4, N'WhatsApp',       N'Delivered', N'Barsaat Long Umbrella',  3, N'Sage'),
(7, N'03334478891',  5, N'CashOnDelivery', N'Cancelled', N'Chandni Room Package',   1, N'Sage'),
(8, N'03082216754',  6, N'CashOnDelivery', N'Ready',     N'Jaali Sheer Panel',      4, N'Cream');

/*  Spread a further twelve weeks of delivered orders behind those, so the
    overview's sales chart has a shape instead of one spike. Quantities rise
    towards the present — shaadi season running hot, as the design says.    */
INSERT INTO @Seed
SELECT  100 + n.Week, N'03012345678', n.Week * 7 + 3, N'CashOnDelivery', N'Delivered',
        N'Sooti Double Bedsheet', 1 + (12 - n.Week) / 3, N'Cream'
FROM   (SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Week
        FROM sys.all_objects) AS n;

DECLARE @Seq INT, @Phone NVARCHAR(20), @DaysAgo INT, @Pay NVARCHAR(24), @Status NVARCHAR(24);
DECLARE @OrderId INT, @CustomerId INT, @Reference NVARCHAR(16), @Subtotal DECIMAL(12,2),
        @Delivery DECIMAL(12,2), @PlacedAt DATETIME2(3);

DECLARE seq_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT Seq, Phone, DaysAgo, PaymentMethod, Status FROM @Seed ORDER BY Seq;

OPEN seq_cursor;
FETCH NEXT FROM seq_cursor INTO @Seq, @Phone, @DaysAgo, @Pay, @Status;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @CustomerId = CustomerId FROM dbo.Customers WHERE Phone = @Phone;

    SET @PlacedAt = DATEADD(HOUR, 11 + (@Seq % 8), CAST(DATEADD(DAY, -@DaysAgo, @Today) AS DATETIME2(3)));
    SET @Reference = N'SS-' + CAST(NEXT VALUE FOR dbo.OrderReferenceSequence AS NVARCHAR(10));

    SELECT @Subtotal = SUM(p.Price * s.Quantity)
    FROM   @Seed AS s JOIN dbo.Products AS p ON p.Name = s.ProductName
    WHERE  s.Seq = @Seq;

    SELECT @Delivery = CASE WHEN @Subtotal >= st.FreeDeliveryThreshold THEN 0 ELSE st.DeliveryCharge END
    FROM   dbo.ShopSettings AS st WHERE st.ShopSettingsId = 1;

    INSERT INTO dbo.Orders (Reference, CustomerId, Status, PaymentMethod, Subtotal,
                            DeliveryCharge, Total, DeliveryAddress, PlacedAt, UpdatedAt)
    SELECT @Reference, @CustomerId, @Status, @Pay, @Subtotal, @Delivery,
           @Subtotal + @Delivery, c.Address, @PlacedAt, @PlacedAt
    FROM   dbo.Customers AS c WHERE c.CustomerId = @CustomerId;

    SET @OrderId = CAST(SCOPE_IDENTITY() AS INT);

    INSERT INTO dbo.OrderLines (OrderId, ProductId, ProductName, SwatchId, SwatchName,
                                BedSize, UnitPrice, Quantity, LineTotal)
    SELECT @OrderId, p.ProductId, p.Name, sw.SwatchId, sw.Name,
           CASE WHEN c.Slug IN (N'wedding-sets', N'bed-sheets') THEN N'Double' ELSE NULL END,
           p.Price, s.Quantity, p.Price * s.Quantity
    FROM   @Seed AS s
    JOIN   dbo.Products AS p ON p.Name = s.ProductName
    JOIN   dbo.Categories AS c ON c.CategoryId = p.CategoryId
    LEFT JOIN dbo.Swatches AS sw ON sw.Name = s.SwatchName
    WHERE  s.Seq = @Seq;

    INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, Note, ChangedAt)
    VALUES (@OrderId, NULL, N'Placed', N'Order placed from the website', @PlacedAt);

    IF @Status <> N'Placed'
        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, Note, ChangedAt)
        VALUES (@OrderId, N'Placed', @Status, N'Seeded demo history', DATEADD(HOUR, 6, @PlacedAt));

    FETCH NEXT FROM seq_cursor INTO @Seq, @Phone, @DaysAgo, @Pay, @Status;
END

CLOSE seq_cursor;
DEALLOCATE seq_cursor;
GO

/* ── The stitching floor ─────────────────────────────────────────────── */
DECLARE @Today2 DATE = CAST(SYSUTCDATETIME() AS DATE);

INSERT INTO dbo.StitchingJobs (OrderId, Title, Stage, AssignedTo, SwatchId, DueDate)
SELECT  o.OrderId, j.Title, j.Stage, j.AssignedTo, sw.SwatchId, DATEADD(DAY, j.DueInDays, @Today2)
FROM   (VALUES
            (N'Gulaab Bridal Set',        N'Measuring', N'Nasir',      2, N'Plum'),
            (N'Velvet drapes, 3 windows', N'Measuring', N'Nasir',      3, N'Sage'),
            (N'Chandni Room Package',     N'Cutting',   N'Rafiq',      3, N'Sage'),
            (N'Mehr Bridal Set',          N'Stitching', N'Shakeel',    1, N'Gold'),
            (N'Jahez bundle ×2',          N'Stitching', N'Rafiq',      4, N'Terracotta'),
            (N'Jahez Six-Piece Bundle',   N'Ready',     N'Front desk', 0, N'Terracotta'),
            (N'Jaali Sheer Panel ×4',     N'Ready',     N'Front desk', 0, N'Cream')
        ) AS j (Title, Stage, AssignedTo, DueInDays, SwatchName)
CROSS APPLY (SELECT TOP (1) OrderId FROM dbo.Orders
             WHERE Status NOT IN (N'Delivered', N'Cancelled') ORDER BY OrderId) AS o
LEFT JOIN dbo.Swatches AS sw ON sw.Name = j.SwatchName
WHERE  NOT EXISTS (SELECT 1 FROM dbo.StitchingJobs AS x WHERE x.Title = j.Title);
GO

/* ── Measurements on the order that is being stitched ────────────────── */
INSERT INTO dbo.OrderMeasurements (OrderId, BedWidthIn, BedLengthIn, WindowDropIn, WindowCount, TakenBy, Notes)
SELECT TOP (1) OrderId, 60, 78, 84, 2, N'Nasir', N'Bed 78×60in · windows 84in drop'
FROM   dbo.Orders WHERE Status = N'Stitching'
  AND  NOT EXISTS (SELECT 1 FROM dbo.OrderMeasurements);
GO

PRINT 'Demo data seeded.';
GO
