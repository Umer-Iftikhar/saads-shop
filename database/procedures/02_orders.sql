/*  Saad's Shop — order procedures
    ========================================================================
    usp_Order_Create is the one procedure in this database where correctness
    under concurrency actually matters. Everything else can be retried; an
    oversold wedding set means telephoning a bride.
*/

SET NOCOUNT ON;
GO

/* ═════════════════════════════════════════════════════════════════════════
   CHECKOUT

   Result sets: 1) the created order  2) its priced lines  3) status

   The money in the request is ignored. Prices, the delivery charge and the
   total are all recomputed here, inside the transaction, from Products and
   ShopSettings. A client that posts { price: 1 } is charged the real price.
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Order_Create
    @CustomerName    NVARCHAR(128),
    @Phone           NVARCHAR(20),
    @DeliveryAddress NVARCHAR(400),
    @Area            NVARCHAR(96)  = NULL,
    @PaymentMethod   NVARCHAR(24),
    @Notes           NVARCHAR(1000) = NULL,
    @Lines           dbo.OrderLineTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    /*  Without XACT_ABORT a non-fatal error inside the transaction can leave
        it open, and the connection goes back to the pool poisoned.           */
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @OrderId INT = NULL, @Reference NVARCHAR(16) = NULL;
    DECLARE @CustomerId INT, @NormalisedPhone NVARCHAR(20);
    DECLARE @Subtotal DECIMAL(12,2) = 0, @DeliveryCharge DECIMAL(12,2) = 0, @Total DECIMAL(12,2) = 0;
    DECLARE @ShortName NVARCHAR(128) = NULL;

    /*  Two lines of the same product in different cloths are a real order,
        not a duplicate — but stock must be checked against the SUM. Held
        outside the transaction because it needs no locks.                    */
    DECLARE @Wanted TABLE (ProductId INT PRIMARY KEY, TotalQty INT NOT NULL);

    BEGIN TRY
        /* ── validate ─────────────────────────────────────────────────── */
        IF NULLIF(LTRIM(RTRIM(@CustomerName)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Please tell us your name.';
        ELSE IF LEN(@CustomerName) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'That name is too long.';
        ELSE IF NULLIF(LTRIM(RTRIM(@DeliveryAddress)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Please give an address in Rawalpindi.';
        ELSE IF LEN(@DeliveryAddress) > 400
            SELECT @ResponseCode = 400, @ResponseMessage = N'That address is too long.';
        ELSE IF NOT EXISTS (SELECT 1 FROM @Lines)
            SELECT @ResponseCode = 400, @ResponseMessage = N'Your cart is empty.';
        ELSE IF EXISTS (SELECT 1 FROM @Lines WHERE Quantity IS NULL OR Quantity <= 0 OR Quantity > 999)
            SELECT @ResponseCode = 400, @ResponseMessage = N'Each item needs a quantity between 1 and 999.';

        /*  Phone: strip spaces and dashes, accept +92 or 0 prefix, store the
            local 03xxxxxxxxx form so one customer is one row.                */
        IF @ResponseCode = 200
        BEGIN
            SET @NormalisedPhone = REPLACE(REPLACE(REPLACE(REPLACE(
                    ISNULL(@Phone, N''), N' ', N''), N'-', N''), N'(', N''), N')', N'');

            IF LEFT(@NormalisedPhone, 3) = N'+92' SET @NormalisedPhone = N'0' + SUBSTRING(@NormalisedPhone, 4, 20);
            ELSE IF LEFT(@NormalisedPhone, 2) = N'92' AND LEN(@NormalisedPhone) = 12
                SET @NormalisedPhone = N'0' + SUBSTRING(@NormalisedPhone, 3, 20);

            IF @NormalisedPhone NOT LIKE N'03[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
                SELECT @ResponseCode = 400,
                       @ResponseMessage = N'That phone number does not look right. Use the form 03xx xxx xxxx.';
        END

        /*  A payment method the shop has switched off must not be accepted
            just because a stale page still offered it.                       */
        IF @ResponseCode = 200
        BEGIN
            IF @PaymentMethod NOT IN (N'CashOnDelivery', N'WhatsApp', N'ReserveInShop', N'Card')
                SELECT @ResponseCode = 400, @ResponseMessage = N'Choose how you would like to pay.';
            ELSE IF NOT EXISTS (
                SELECT 1 FROM dbo.ShopSettings
                WHERE ShopSettingsId = 1
                  AND ((@PaymentMethod = N'CashOnDelivery' AND CashOnDeliveryEnabled = 1)
                    OR (@PaymentMethod = N'WhatsApp'       AND WhatsAppOrdersEnabled = 1)
                    OR (@PaymentMethod = N'ReserveInShop'  AND ReserveInShopEnabled  = 1)
                    OR (@PaymentMethod = N'Card'           AND CardPaymentEnabled    = 1)))
                SELECT @ResponseCode = 409, @ResponseMessage = N'That payment method is not available right now.';
        END

        IF @ResponseCode = 200
        BEGIN
            INSERT INTO @Wanted (ProductId, TotalQty)
            SELECT ProductId, SUM(Quantity) FROM @Lines GROUP BY ProductId;

            IF EXISTS (SELECT 1 FROM @Wanted AS w
                       WHERE NOT EXISTS (SELECT 1 FROM dbo.Products AS p
                                         WHERE p.ProductId = w.ProductId AND p.IsActive = 1))
                SELECT @ResponseCode = 404, @ResponseMessage = N'One of the items is no longer in the shop.';
            ELSE IF EXISTS (SELECT 1 FROM @Lines AS l
                            WHERE l.BedSize IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM dbo.BedSizes b WHERE b.BedSizeCode = l.BedSize))
                SELECT @ResponseCode = 400, @ResponseMessage = N'One of the items has an unknown bed size.';
            ELSE IF EXISTS (SELECT 1 FROM @Lines AS l
                            WHERE l.SwatchId IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM dbo.Swatches s WHERE s.SwatchId = l.SwatchId AND s.IsActive = 1))
                SELECT @ResponseCode = 404, @ResponseMessage = N'One of the chosen cloths is no longer available.';
        END

        /* ── the transaction ──────────────────────────────────────────── */
        IF @ResponseCode = 200
        BEGIN
            BEGIN TRANSACTION;

                /*  Lock the product rows we are about to sell.

                    UPDLOCK  — take the update lock now rather than a shared
                               lock that must later be upgraded; the upgrade is
                               the classic conversion deadlock under load.
                    HOLDLOCK — hold it to COMMIT, so the stock this decision is
                               made on cannot move underneath the decision.
                    ORDER BY — every concurrent checkout acquires rows in the
                               same ascending order. A deadlock needs a cycle;
                               a global lock order makes a cycle impossible.   */
                SELECT  p.ProductId, p.Name, p.Price, p.Stock
                INTO    #Locked
                FROM    dbo.Products AS p WITH (UPDLOCK, HOLDLOCK)
                JOIN    @Wanted AS w ON w.ProductId = p.ProductId
                ORDER BY p.ProductId;

                /*  Re-read under the lock. Whatever the browser believed about
                    stock five minutes ago is irrelevant.                      */
                SELECT TOP (1) @ShortName = k.Name
                FROM   #Locked AS k
                JOIN   @Wanted AS w ON w.ProductId = k.ProductId
                WHERE  k.Stock < w.TotalQty
                ORDER BY k.ProductId;

                IF @ShortName IS NOT NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 409,
                           @ResponseMessage = @ShortName + N' just went out of stock. Please adjust your cart.';
                END
                ELSE
                BEGIN
                    /*  Customer: one row per phone number.                    */
                    SELECT @CustomerId = CustomerId FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK)
                    WHERE  Phone = @NormalisedPhone;

                    IF @CustomerId IS NULL
                    BEGIN
                        INSERT INTO dbo.Customers (Name, Phone, Area, Address)
                        VALUES (LTRIM(RTRIM(@CustomerName)), @NormalisedPhone, @Area, @DeliveryAddress);
                        SET @CustomerId = CAST(SCOPE_IDENTITY() AS INT);
                    END
                    ELSE
                        UPDATE dbo.Customers
                        SET    Name = LTRIM(RTRIM(@CustomerName)),
                               Area = ISNULL(@Area, Area),
                               Address = @DeliveryAddress,
                               UpdatedAt = SYSUTCDATETIME()
                        WHERE  CustomerId = @CustomerId;

                    /*  Price every line from the LOCKED table, plus the bed-size
                        adjustment. Never from the request.                     */
                    SELECT  l.ProductId,
                            k.Name AS ProductName,
                            l.SwatchId,
                            sw.Name AS SwatchName,
                            l.BedSize,
                            CAST(k.Price + ISNULL(b.PriceAdjustment, 0) AS DECIMAL(12,2)) AS UnitPrice,
                            l.Quantity,
                            CAST((k.Price + ISNULL(b.PriceAdjustment, 0)) * l.Quantity AS DECIMAL(12,2)) AS LineTotal
                    INTO    #Priced
                    FROM    @Lines AS l
                    JOIN    #Locked AS k ON k.ProductId = l.ProductId
                    LEFT JOIN dbo.BedSizes AS b ON b.BedSizeCode = l.BedSize
                    LEFT JOIN dbo.Swatches AS sw ON sw.SwatchId = l.SwatchId;

                    /*  A bed-size discount must never drive a line negative.  */
                    IF EXISTS (SELECT 1 FROM #Priced WHERE UnitPrice < 0)
                    BEGIN
                        ROLLBACK TRANSACTION;
                        SELECT @ResponseCode = 400,
                               @ResponseMessage = N'That size is not available for one of the items.';
                    END
                    ELSE
                    BEGIN
                        SELECT @Subtotal = ISNULL(SUM(LineTotal), 0) FROM #Priced;

                        SELECT @DeliveryCharge =
                                 CASE WHEN @Subtotal >= s.FreeDeliveryThreshold THEN 0 ELSE s.DeliveryCharge END
                        FROM   dbo.ShopSettings AS s WHERE s.ShopSettingsId = 1;

                        SET @DeliveryCharge = ISNULL(@DeliveryCharge, 0);
                        SET @Total = @Subtotal + @DeliveryCharge;

                        SET @Reference = N'SS-' + CAST(NEXT VALUE FOR dbo.OrderReferenceSequence AS NVARCHAR(10));

                        INSERT INTO dbo.Orders (Reference, CustomerId, Status, PaymentMethod,
                                                Subtotal, DeliveryCharge, Total, DeliveryAddress, Notes)
                        VALUES (@Reference, @CustomerId, N'Placed', @PaymentMethod,
                                @Subtotal, @DeliveryCharge, @Total, @DeliveryAddress, @Notes);

                        SET @OrderId = CAST(SCOPE_IDENTITY() AS INT);

                        INSERT INTO dbo.OrderLines (OrderId, ProductId, ProductName, SwatchId, SwatchName,
                                                    BedSize, UnitPrice, Quantity, LineTotal)
                        SELECT @OrderId, ProductId, ProductName, SwatchId, SwatchName,
                               BedSize, UnitPrice, Quantity, LineTotal
                        FROM   #Priced;

                        /*  Decrement stock. The CK_Products_Stock >= 0 constraint
                            is the belt to this braces: if the lock logic above
                            were ever wrong, the write fails rather than
                            silently overselling.                              */
                        UPDATE  p
                        SET     p.Stock     = p.Stock - w.TotalQty,
                                p.SoldCount = p.SoldCount + w.TotalQty,
                                p.UpdatedAt = SYSUTCDATETIME()
                        FROM    dbo.Products AS p
                        JOIN    @Wanted AS w ON w.ProductId = p.ProductId;

                        INSERT INTO dbo.InventoryAdjustments (ProductId, Delta, ResultingStock, Reason, OrderId)
                        SELECT  w.ProductId, -w.TotalQty, p.Stock, N'Sold on ' + @Reference, @OrderId
                        FROM    @Wanted AS w
                        JOIN    dbo.Products AS p ON p.ProductId = w.ProductId;

                        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, Note)
                        VALUES (@OrderId, NULL, N'Placed', N'Order placed from the website');

                        COMMIT TRANSACTION;

                        SET @ResponseMessage = N'Shukriya! Your order is with the shop.';
                    END
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @OrderId = NULL; SET @Reference = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'We could not place the order. Please try again.';
    END CATCH

    /* ── single exit, fixed shape ─────────────────────────────────────── */
    SELECT  o.OrderId, o.Reference, o.Status, o.PaymentMethod, o.Subtotal,
            o.DeliveryCharge, o.Total, o.DeliveryAddress, o.Notes, o.PlacedAt,
            c.Name AS CustomerName, c.Phone, c.Area
    FROM    dbo.Orders AS o
    JOIN    dbo.Customers AS c ON c.CustomerId = o.CustomerId
    WHERE   o.OrderId = @OrderId;

    SELECT  OrderLineId, ProductId, ProductName, SwatchId, SwatchName,
            BedSize, UnitPrice, Quantity, LineTotal
    FROM    dbo.OrderLines
    WHERE   OrderId = @OrderId
    ORDER BY OrderLineId;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Set-builder quote — prices a bistar + parde + cushion combination.
   Read-only: it reserves nothing and locks nothing.
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_SetBuilder_Quote
    @SheetProductId   INT,
    @CurtainProductId INT,
    @CushionProductId INT,
    @BedSize          NVARCHAR(16)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Adjust DECIMAL(12,2) = 0, @Total DECIMAL(12,2) = 0;

    CREATE TABLE #Quote (
        Slot NVARCHAR(16), ProductId INT, ProductName NVARCHAR(128),
        UnitPrice DECIMAL(12,2), InStock BIT, SortIndex INT
    );

    BEGIN TRY
        IF @SheetProductId IS NULL OR @CurtainProductId IS NULL OR @CushionProductId IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Pick a cloth for each part of the set.';
        ELSE IF NULLIF(LTRIM(RTRIM(@BedSize)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Pick a bed size.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.BedSizes WHERE BedSizeCode = @BedSize)
            SELECT @ResponseCode = 400, @ResponseMessage = N'That bed size is not one we stitch.';
        ELSE IF (SELECT COUNT(*) FROM dbo.Products
                 WHERE ProductId IN (@SheetProductId, @CurtainProductId, @CushionProductId) AND IsActive = 1)
                < (SELECT COUNT(DISTINCT v) FROM (VALUES (@SheetProductId), (@CurtainProductId), (@CushionProductId)) AS x(v))
            SELECT @ResponseCode = 404, @ResponseMessage = N'One of the items is no longer in the shop.';

        IF @ResponseCode = 200
        BEGIN
            SELECT @Adjust = PriceAdjustment FROM dbo.BedSizes WHERE BedSizeCode = @BedSize;

            /*  Only the bedding is cut to the bed — curtains and cushions do
                not change price with the bed size.                            */
            INSERT INTO #Quote (Slot, ProductId, ProductName, UnitPrice, InStock, SortIndex)
            SELECT N'Bistar', p.ProductId, p.Name,
                   CASE WHEN p.Price + @Adjust < 0 THEN 0 ELSE p.Price + @Adjust END,
                   CASE WHEN p.Stock > 0 THEN 1 ELSE 0 END, 1
            FROM   dbo.Products AS p WHERE p.ProductId = @SheetProductId
            UNION ALL
            SELECT N'Parde', p.ProductId, p.Name, p.Price,
                   CASE WHEN p.Stock > 0 THEN 1 ELSE 0 END, 2
            FROM   dbo.Products AS p WHERE p.ProductId = @CurtainProductId
            UNION ALL
            SELECT N'Cushions', p.ProductId, p.Name, p.Price,
                   CASE WHEN p.Stock > 0 THEN 1 ELSE 0 END, 3
            FROM   dbo.Products AS p WHERE p.ProductId = @CushionProductId;

            SELECT @Total = ISNULL(SUM(UnitPrice), 0) FROM #Quote;
        END
    END TRY
    BEGIN CATCH
        DELETE FROM #Quote; SET @Total = 0;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not price that set. Please try again.';
    END CATCH

    SELECT Slot, ProductId, ProductName, UnitPrice, InStock FROM #Quote ORDER BY SortIndex;
    SELECT @Total AS Total, @BedSize AS BedSize;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Customer-facing order lookup — reference plus the phone it was placed with.

   The phone acts as the shared secret. A reference alone is guessable
   (SS-2419 sits next to SS-2418), so it is never sufficient on its own.
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Order_GetByReference
    @Reference NVARCHAR(16),
    @Phone     NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @OrderId INT = NULL, @NormalisedPhone NVARCHAR(20);

    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@Reference)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'An order number is required.';
        ELSE IF NULLIF(LTRIM(RTRIM(@Phone)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'The phone number on the order is required.';

        IF @ResponseCode = 200
        BEGIN
            SET @NormalisedPhone = REPLACE(REPLACE(REPLACE(REPLACE(@Phone, N' ', N''), N'-', N''), N'(', N''), N')', N'');
            IF LEFT(@NormalisedPhone, 3) = N'+92' SET @NormalisedPhone = N'0' + SUBSTRING(@NormalisedPhone, 4, 20);

            SELECT @OrderId = o.OrderId
            FROM   dbo.Orders AS o
            JOIN   dbo.Customers AS c ON c.CustomerId = o.CustomerId
            WHERE  o.Reference = @Reference AND c.Phone = @NormalisedPhone;

            /*  Deliberately the same message whether the reference is unknown
                or the phone does not match — otherwise this endpoint becomes
                an oracle for enumerating which order numbers exist.           */
            IF @OrderId IS NULL
                SELECT @ResponseCode = 404, @ResponseMessage = N'We could not find that order.';
        END
    END TRY
    BEGIN CATCH
        SET @OrderId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not look up that order. Please try again.';
    END CATCH

    SELECT  o.OrderId, o.Reference, o.Status, o.PaymentMethod, o.Subtotal, o.DeliveryCharge,
            o.Total, o.DeliveryAddress, o.PlacedAt, c.Name AS CustomerName
    FROM    dbo.Orders AS o
    JOIN    dbo.Customers AS c ON c.CustomerId = o.CustomerId
    WHERE   o.OrderId = @OrderId;

    SELECT  OrderLineId, ProductId, ProductName, SwatchName, BedSize, UnitPrice, Quantity, LineTotal
    FROM    dbo.OrderLines WHERE OrderId = @OrderId ORDER BY OrderLineId;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Shop panel — orders list, order detail, status moves, measurements
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Order_GetList
    @Status   NVARCHAR(24)  = NULL,
    @Search   NVARCHAR(128) = NULL,
    @FromDate DATE          = NULL,
    @ToDate   DATE          = NULL,
    @Page     INT           = 1,
    @PageSize INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Total INT = 0, @NeedsAttention INT = 0;

    CREATE TABLE #Orders (
        OrderId INT, Reference NVARCHAR(16), PlacedAt DATETIME2(3), CustomerName NVARCHAR(128),
        Phone NVARCHAR(20), ItemSummary NVARCHAR(400), LineCount INT, Total DECIMAL(12,2),
        PaymentMethod NVARCHAR(24), Status NVARCHAR(24), SortIndex INT IDENTITY(1,1)
    );

    BEGIN TRY
        IF @Page IS NULL OR @Page < 1
            SELECT @ResponseCode = 400, @ResponseMessage = N'Page must be 1 or more.';
        ELSE IF @PageSize IS NULL OR @PageSize < 1 OR @PageSize > 200
            SELECT @ResponseCode = 400, @ResponseMessage = N'Page size must be between 1 and 200.';
        ELSE IF @Status IS NOT NULL
                AND @Status NOT IN (N'Placed', N'Measuring', N'Stitching', N'Ready', N'Delivered', N'Cancelled')
            SELECT @ResponseCode = 400, @ResponseMessage = N'Unknown order status.';
        ELSE IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate > @ToDate
            SELECT @ResponseCode = 400, @ResponseMessage = N'The start date must be before the end date.';

        IF @ResponseCode = 200
        BEGIN
            DECLARE @Pattern NVARCHAR(140) = NULL;
            IF NULLIF(LTRIM(RTRIM(@Search)), N'') IS NOT NULL
                SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(@Search)), N'\', N'\\'), N'%', N'\%'), N'_', N'\_') + N'%';

            INSERT INTO #Orders (OrderId, Reference, PlacedAt, CustomerName, Phone,
                                 ItemSummary, LineCount, Total, PaymentMethod, Status)
            SELECT  o.OrderId, o.Reference, o.PlacedAt, c.Name, c.Phone,
                    STUFF((SELECT N', ' + ol.ProductName
                           FROM dbo.OrderLines AS ol WHERE ol.OrderId = o.OrderId
                           ORDER BY ol.OrderLineId
                           FOR XML PATH(N''), TYPE).value(N'.', N'NVARCHAR(400)'), 1, 2, N''),
                    (SELECT COUNT(*) FROM dbo.OrderLines AS ol WHERE ol.OrderId = o.OrderId),
                    o.Total, o.PaymentMethod, o.Status
            FROM    dbo.Orders AS o
            JOIN    dbo.Customers AS c ON c.CustomerId = o.CustomerId
            WHERE   (@Status   IS NULL OR o.Status = @Status)
              AND   (@FromDate IS NULL OR o.PlacedAt >= @FromDate)
              AND   (@ToDate   IS NULL OR o.PlacedAt <  DATEADD(DAY, 1, @ToDate))
              AND   (@Pattern  IS NULL OR o.Reference LIKE @Pattern ESCAPE N'\'
                                       OR c.Name      LIKE @Pattern ESCAPE N'\'
                                       OR c.Phone     LIKE @Pattern ESCAPE N'\')
            ORDER BY o.PlacedAt DESC, o.OrderId DESC
            OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT @Total = COUNT(*)
            FROM   dbo.Orders AS o JOIN dbo.Customers AS c ON c.CustomerId = o.CustomerId
            WHERE  (@Status   IS NULL OR o.Status = @Status)
              AND  (@FromDate IS NULL OR o.PlacedAt >= @FromDate)
              AND  (@ToDate   IS NULL OR o.PlacedAt <  DATEADD(DAY, 1, @ToDate))
              AND  (@Pattern  IS NULL OR o.Reference LIKE @Pattern ESCAPE N'\'
                                      OR c.Name      LIKE @Pattern ESCAPE N'\'
                                      OR c.Phone     LIKE @Pattern ESCAPE N'\');

            /*  'N orders · M need attention' on the Orders header.           */
            SELECT @NeedsAttention = COUNT(*) FROM dbo.Orders WHERE Status IN (N'Placed', N'Measuring');
        END
    END TRY
    BEGIN CATCH
        DELETE FROM #Orders;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load orders. Please try again.';
    END CATCH

    SELECT OrderId, Reference, PlacedAt, CustomerName, Phone, ItemSummary,
           LineCount, Total, PaymentMethod, Status
    FROM   #Orders ORDER BY SortIndex;

    SELECT @Total AS TotalCount, @NeedsAttention AS NeedsAttentionCount, @Page AS Page, @PageSize AS PageSize;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Order_GetById
    @OrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @FoundId INT = NULL;

    BEGIN TRY
        IF @OrderId IS NULL OR @OrderId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Order id is required.';
        ELSE
        BEGIN
            SELECT @FoundId = OrderId FROM dbo.Orders WHERE OrderId = @OrderId;
            IF @FoundId IS NULL
                SELECT @ResponseCode = 404, @ResponseMessage = N'That order no longer exists.';
        END
    END TRY
    BEGIN CATCH
        SET @FoundId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load that order. Please try again.';
    END CATCH

    /*  1 — the order   2 — lines   3 — measurements   4 — history   5 — status */
    SELECT  o.OrderId, o.Reference, o.Status, o.PaymentMethod, o.Subtotal, o.DeliveryCharge,
            o.Total, o.DeliveryAddress, o.Notes, o.PlacedAt, o.UpdatedAt,
            c.CustomerId, c.Name AS CustomerName, c.Phone, c.Area
    FROM    dbo.Orders AS o
    JOIN    dbo.Customers AS c ON c.CustomerId = o.CustomerId
    WHERE   o.OrderId = @FoundId;

    SELECT  ol.OrderLineId, ol.ProductId, ol.ProductName, ol.SwatchId, ol.SwatchName,
            ol.BedSize, ol.UnitPrice, ol.Quantity, ol.LineTotal,
            s.ColorValue AS SwatchColorValue, s.Weave AS SwatchWeave
    FROM    dbo.OrderLines AS ol
    LEFT JOIN dbo.Swatches AS s ON s.SwatchId = ol.SwatchId
    WHERE   ol.OrderId = @FoundId
    ORDER BY ol.OrderLineId;

    SELECT  OrderMeasurementId, BedWidthIn, BedLengthIn, WindowDropIn, WindowCount,
            Notes, TakenBy, TakenAt
    FROM    dbo.OrderMeasurements WHERE OrderId = @FoundId ORDER BY TakenAt DESC;

    SELECT  h.FromStatus, h.ToStatus, h.Note, h.ChangedAt, u.FullName AS ChangedBy
    FROM    dbo.OrderStatusHistory AS h
    LEFT JOIN dbo.Users AS u ON u.Id = h.ChangedByUserId
    WHERE   h.OrderId = @FoundId
    ORDER BY h.ChangedAt DESC;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Status moves are a state machine, not a free-text field. An order cannot
    go back to Measuring once Delivered, and Cancelled is terminal. Cancelling
    also returns the stock — otherwise a cancelled wedding set stays
    invisible in inventory forever.                                          */
CREATE OR ALTER PROCEDURE dbo.usp_Order_UpdateStatus
    @OrderId     INT,
    @NewStatus   NVARCHAR(24),
    @Note        NVARCHAR(400) = NULL,
    @ActorUserId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Current NVARCHAR(24) = NULL, @Reference NVARCHAR(16) = NULL;

    BEGIN TRY
        IF @OrderId IS NULL OR @OrderId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Order id is required.';
        ELSE IF @NewStatus NOT IN (N'Placed', N'Measuring', N'Stitching', N'Ready', N'Delivered', N'Cancelled')
            SELECT @ResponseCode = 400, @ResponseMessage = N'That is not a status we use.';

        IF @ResponseCode = 200
        BEGIN
            BEGIN TRANSACTION;

                SELECT @Current = Status, @Reference = Reference
                FROM   dbo.Orders WITH (UPDLOCK, HOLDLOCK)
                WHERE  OrderId = @OrderId;

                IF @Current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 404, @ResponseMessage = N'That order no longer exists.';
                END
                ELSE IF @Current = @NewStatus
                BEGIN
                    /*  Idempotent: setting a status it already has is a
                        no-op, not a failure. Double-clicks happen.           */
                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'The order is already in that state.';
                END
                ELSE IF @Current IN (N'Delivered', N'Cancelled')
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 409,
                           @ResponseMessage = N'A ' + LOWER(@Current) + N' order cannot be moved again.';
                END
                ELSE
                BEGIN
                    UPDATE dbo.Orders
                    SET    Status = @NewStatus, UpdatedAt = SYSUTCDATETIME()
                    WHERE  OrderId = @OrderId;

                    INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedByUserId, Note)
                    VALUES (@OrderId, @Current, @NewStatus, @ActorUserId, @Note);

                    IF @NewStatus = N'Cancelled'
                    BEGIN
                        UPDATE  p
                        SET     p.Stock     = p.Stock + agg.Qty,
                                p.SoldCount = CASE WHEN p.SoldCount >= agg.Qty THEN p.SoldCount - agg.Qty ELSE 0 END,
                                p.UpdatedAt = SYSUTCDATETIME()
                        FROM    dbo.Products AS p
                        JOIN    (SELECT ProductId, SUM(Quantity) AS Qty
                                 FROM dbo.OrderLines WHERE OrderId = @OrderId GROUP BY ProductId) AS agg
                              ON agg.ProductId = p.ProductId;

                        INSERT INTO dbo.InventoryAdjustments (ProductId, Delta, ResultingStock, Reason, OrderId, ActorUserId)
                        SELECT  agg.ProductId, agg.Qty, p.Stock,
                                N'Returned to stock — ' + @Reference + N' cancelled', @OrderId, @ActorUserId
                        FROM    (SELECT ProductId, SUM(Quantity) AS Qty
                                 FROM dbo.OrderLines WHERE OrderId = @OrderId GROUP BY ProductId) AS agg
                        JOIN    dbo.Products AS p ON p.ProductId = agg.ProductId;

                        /*  Nothing on the floor should still be stitching a
                            cancelled order.                                  */
                        UPDATE dbo.StitchingJobs
                        SET    Stage = N'Done', CompletedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
                        WHERE  OrderId = @OrderId AND Stage <> N'Done';
                    END

                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'Order moved to ' + @NewStatus + N'.';
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not update the order. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Order_SaveMeasurements
    @OrderId      INT,
    @BedWidthIn   DECIMAL(6,2)  = NULL,
    @BedLengthIn  DECIMAL(6,2)  = NULL,
    @WindowDropIn DECIMAL(6,2)  = NULL,
    @WindowCount  INT           = NULL,
    @Notes        NVARCHAR(1000) = NULL,
    @TakenBy      NVARCHAR(128) = NULL,
    @ActorUserId  NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Current NVARCHAR(24);

    BEGIN TRY
        IF @OrderId IS NULL OR @OrderId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Order id is required.';
        ELSE IF @BedWidthIn IS NULL AND @BedLengthIn IS NULL AND @WindowDropIn IS NULL
                AND NULLIF(LTRIM(RTRIM(@Notes)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Record at least one measurement or a note.';
        ELSE IF (@BedWidthIn   IS NOT NULL AND (@BedWidthIn   <= 0 OR @BedWidthIn   > 200))
             OR (@BedLengthIn  IS NOT NULL AND (@BedLengthIn  <= 0 OR @BedLengthIn  > 200))
             OR (@WindowDropIn IS NOT NULL AND (@WindowDropIn <= 0 OR @WindowDropIn > 300))
            SELECT @ResponseCode = 400, @ResponseMessage = N'One of those measurements is out of range.';
        ELSE IF @WindowCount IS NOT NULL AND (@WindowCount < 0 OR @WindowCount > 100)
            SELECT @ResponseCode = 400, @ResponseMessage = N'Window count is out of range.';

        IF @ResponseCode = 200
        BEGIN
            BEGIN TRANSACTION;

                SELECT @Current = Status FROM dbo.Orders WITH (UPDLOCK, HOLDLOCK) WHERE OrderId = @OrderId;

                IF @Current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 404, @ResponseMessage = N'That order no longer exists.';
                END
                ELSE IF @Current IN (N'Delivered', N'Cancelled')
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 409, @ResponseMessage = N'That order is closed.';
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.OrderMeasurements (OrderId, BedWidthIn, BedLengthIn, WindowDropIn,
                                                       WindowCount, Notes, TakenBy)
                    VALUES (@OrderId, @BedWidthIn, @BedLengthIn, @WindowDropIn, @WindowCount, @Notes, @TakenBy);

                    /*  Taking measurements is what moves a fresh order along;
                        the shopkeeper should not have to do it twice.        */
                    IF @Current = N'Placed'
                    BEGIN
                        UPDATE dbo.Orders SET Status = N'Measuring', UpdatedAt = SYSUTCDATETIME()
                        WHERE OrderId = @OrderId;

                        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedByUserId, Note)
                        VALUES (@OrderId, @Current, N'Measuring', @ActorUserId, N'Measurements recorded');
                    END

                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'Measurements saved.';
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save the measurements. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO
