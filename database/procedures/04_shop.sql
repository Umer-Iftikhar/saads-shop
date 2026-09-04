/*  Saad's Shop — settings and the overview dashboard
    ======================================================================== */

SET NOCOUNT ON;
GO

/* ═════════════════════════════════════════════════════════════════════════
   Settings

   Two readers, deliberately different. The storefront gets only what a
   shopper needs to see; the panel gets the whole row. Splitting them here
   rather than filtering in C# means a mistake in a controller cannot leak
   a column the public was never meant to have.
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Settings_GetPublic
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.ShopSettings WHERE ShopSettingsId = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'The shop is not set up yet.';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load the shop details. Please try again.';
    END CATCH

    SELECT  ShopName, City, AddressLine, WhatsAppNumber, BannerText, OpeningHours,
            DeliveryCharge, FreeDeliveryThreshold,
            CashOnDeliveryEnabled, WhatsAppOrdersEnabled, ReserveInShopEnabled, CardPaymentEnabled
    FROM    dbo.ShopSettings
    WHERE   ShopSettingsId = 1 AND @ResponseCode = 200;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Settings_Get
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.ShopSettings WHERE ShopSettingsId = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'The shop is not set up yet.';
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load settings. Please try again.';
    END CATCH

    SELECT  s.ShopSettingsId, s.ShopName, s.City, s.AddressLine, s.WhatsAppNumber, s.BannerText,
            s.OpeningHours, s.DeliveryCharge, s.FreeDeliveryThreshold,
            s.CashOnDeliveryEnabled, s.WhatsAppOrdersEnabled, s.ReserveInShopEnabled,
            s.CardPaymentEnabled, s.UpdatedAt, u.FullName AS UpdatedBy
    FROM    dbo.ShopSettings AS s
    LEFT JOIN dbo.Users AS u ON u.Id = s.UpdatedByUserId
    WHERE   s.ShopSettingsId = 1 AND @ResponseCode = 200;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Settings_Update
    @ShopName              NVARCHAR(128),
    @City                  NVARCHAR(64),
    @AddressLine           NVARCHAR(256),
    @WhatsAppNumber        NVARCHAR(20),
    @BannerText            NVARCHAR(400) = NULL,
    @OpeningHours          NVARCHAR(200) = NULL,
    @DeliveryCharge        DECIMAL(12,2),
    @FreeDeliveryThreshold DECIMAL(12,2),
    @CashOnDeliveryEnabled BIT,
    @WhatsAppOrdersEnabled BIT,
    @ReserveInShopEnabled  BIT,
    @CardPaymentEnabled    BIT,
    @ActorUserId           NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @NormalisedWhatsApp NVARCHAR(20);

    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@ShopName)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'The shop needs a name.';
        ELSE IF LEN(@ShopName) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'That shop name is too long.';
        ELSE IF NULLIF(LTRIM(RTRIM(@City)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'City is required.';
        ELSE IF NULLIF(LTRIM(RTRIM(@AddressLine)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Address is required.';
        ELSE IF @DeliveryCharge IS NULL OR @DeliveryCharge < 0 OR @DeliveryCharge > 100000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Delivery charge must be between Rs 0 and Rs 100,000.';
        ELSE IF @FreeDeliveryThreshold IS NULL OR @FreeDeliveryThreshold < 0 OR @FreeDeliveryThreshold > 10000000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Free-delivery threshold is out of range.';
        /*  Turning off every way to pay would leave a shop that cannot take
            an order — a mistake worth refusing rather than saving.           */
        ELSE IF @CashOnDeliveryEnabled = 0 AND @WhatsAppOrdersEnabled = 0
                AND @ReserveInShopEnabled = 0 AND @CardPaymentEnabled = 0
            SELECT @ResponseCode = 409, @ResponseMessage = N'Leave at least one way for customers to pay.';

        IF @ResponseCode = 200
        BEGIN
            SET @NormalisedWhatsApp = REPLACE(REPLACE(REPLACE(REPLACE(
                    ISNULL(@WhatsAppNumber, N''), N' ', N''), N'-', N''), N'(', N''), N')', N'');
            IF LEFT(@NormalisedWhatsApp, 3) = N'+92' SET @NormalisedWhatsApp = N'0' + SUBSTRING(@NormalisedWhatsApp, 4, 20);

            IF @NormalisedWhatsApp NOT LIKE N'03[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
                SELECT @ResponseCode = 400,
                       @ResponseMessage = N'The WhatsApp number should look like 03xx xxx xxxx.';
        END

        IF @ResponseCode = 200
        BEGIN
            /*  MERGE on the singleton: creates row 1 on a fresh database,
                updates it thereafter. The CK_ShopSettings_Singleton check
                keeps a second row from ever appearing.                       */
            MERGE dbo.ShopSettings AS target
            USING (SELECT 1 AS ShopSettingsId) AS source
               ON target.ShopSettingsId = source.ShopSettingsId
            WHEN MATCHED THEN UPDATE SET
                    ShopName = LTRIM(RTRIM(@ShopName)), City = LTRIM(RTRIM(@City)),
                    AddressLine = LTRIM(RTRIM(@AddressLine)), WhatsAppNumber = @NormalisedWhatsApp,
                    BannerText = @BannerText, OpeningHours = @OpeningHours,
                    DeliveryCharge = @DeliveryCharge, FreeDeliveryThreshold = @FreeDeliveryThreshold,
                    CashOnDeliveryEnabled = @CashOnDeliveryEnabled,
                    WhatsAppOrdersEnabled = @WhatsAppOrdersEnabled,
                    ReserveInShopEnabled = @ReserveInShopEnabled,
                    CardPaymentEnabled = @CardPaymentEnabled,
                    UpdatedAt = SYSUTCDATETIME(), UpdatedByUserId = @ActorUserId
            WHEN NOT MATCHED THEN INSERT
                    (ShopSettingsId, ShopName, City, AddressLine, WhatsAppNumber, BannerText, OpeningHours,
                     DeliveryCharge, FreeDeliveryThreshold, CashOnDeliveryEnabled, WhatsAppOrdersEnabled,
                     ReserveInShopEnabled, CardPaymentEnabled, UpdatedAt, UpdatedByUserId)
                 VALUES
                    (1, LTRIM(RTRIM(@ShopName)), LTRIM(RTRIM(@City)), LTRIM(RTRIM(@AddressLine)),
                     @NormalisedWhatsApp, @BannerText, @OpeningHours, @DeliveryCharge, @FreeDeliveryThreshold,
                     @CashOnDeliveryEnabled, @WhatsAppOrdersEnabled, @ReserveInShopEnabled,
                     @CardPaymentEnabled, SYSUTCDATETIME(), @ActorUserId);

            SET @ResponseMessage = N'Settings saved.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save settings. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Overview dashboard (shop panel · 07)

   Result sets: 1) stat tiles  2) 12-week chart  3) best sellers
                4) latest orders  5) status
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Dashboard_Get
    @Today DATE = NULL          -- injectable so tests are not time-dependent
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    SET @Today = ISNULL(@Today, CAST(SYSUTCDATETIME() AS DATE));

    DECLARE @SalesToday      DECIMAL(12,2) = 0,
            @SalesLastWeek   DECIMAL(12,2) = 0,
            @OrdersOpen      INT = 0,
            @AwaitingMeasure INT = 0,
            @OnFloor         INT = 0,
            @DueTomorrow     INT = 0,
            @MonthToDate     DECIMAL(12,2) = 0;

    BEGIN TRY
        /*  Cancelled orders are excluded from every money figure. A tile that
            counts revenue the shop refunded is worse than no tile.           */
        SELECT @SalesToday = ISNULL(SUM(Total), 0)
        FROM   dbo.Orders
        WHERE  Status <> N'Cancelled'
          AND  PlacedAt >= @Today AND PlacedAt < DATEADD(DAY, 1, @Today);

        /*  Same weekday last week — 'Thursday vs last Thursday' is how a shop
            with a weekly rhythm actually reads its numbers.                  */
        SELECT @SalesLastWeek = ISNULL(SUM(Total), 0)
        FROM   dbo.Orders
        WHERE  Status <> N'Cancelled'
          AND  PlacedAt >= DATEADD(DAY, -7, @Today)
          AND  PlacedAt <  DATEADD(DAY, -6, @Today);

        SELECT @OrdersOpen = COUNT(*) FROM dbo.Orders
        WHERE  Status IN (N'Placed', N'Measuring', N'Stitching', N'Ready');

        SELECT @AwaitingMeasure = COUNT(*) FROM dbo.Orders WHERE Status IN (N'Placed', N'Measuring');

        SELECT @OnFloor = COUNT(*) FROM dbo.StitchingJobs WHERE Stage <> N'Done';

        SELECT @DueTomorrow = COUNT(*) FROM dbo.StitchingJobs
        WHERE  Stage <> N'Done' AND DueDate = DATEADD(DAY, 1, @Today);

        SELECT @MonthToDate = ISNULL(SUM(Total), 0)
        FROM   dbo.Orders
        WHERE  Status <> N'Cancelled'
          AND  PlacedAt >= DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1)
          AND  PlacedAt <  DATEADD(DAY, 1, @Today);
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load the dashboard. Please try again.';
    END CATCH

    /*  1 — stat tiles                                                       */
    SELECT  @SalesToday      AS SalesToday,
            @SalesLastWeek   AS SalesSameDayLastWeek,
            @OrdersOpen      AS OrdersOpen,
            @AwaitingMeasure AS OrdersAwaitingMeasurements,
            @OnFloor         AS JobsOnFloor,
            @DueTomorrow     AS JobsDueTomorrow,
            @MonthToDate     AS MonthToDateSales;

    /*  2 — twelve weeks of sales. The numbers series generates the weeks so
        a week with no orders still appears as a zero bar; joining straight
        to Orders would silently drop it and bend the chart.                  */
    ;WITH weeks AS (
        SELECT TOP (12)
               DATEADD(WEEK, -(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1),
                       DATEADD(DAY, -(DATEPART(WEEKDAY, @Today) - 1), @Today)) AS WeekStart
        FROM   sys.all_objects
    )
    SELECT  CAST(w.WeekStart AS DATE) AS WeekStart,
            ISNULL(SUM(o.Total), 0)   AS Sales,
            COUNT(o.OrderId)          AS OrderCount
    FROM    weeks AS w
    LEFT JOIN dbo.Orders AS o
           ON o.PlacedAt >= w.WeekStart
          AND o.PlacedAt <  DATEADD(WEEK, 1, w.WeekStart)
          AND o.Status <> N'Cancelled'
    GROUP BY w.WeekStart
    ORDER BY w.WeekStart;

    /*  3 — best sellers                                                     */
    SELECT TOP (4)
            p.ProductId, p.Name, p.SoldCount, p.Price,
            CAST(p.SoldCount * p.Price AS DECIMAL(14,2)) AS Revenue,
            p.DefaultSwatchId, s.ColorValue AS SwatchColorValue, s.Weave AS SwatchWeave
    FROM    dbo.Products AS p
    LEFT JOIN dbo.Swatches AS s ON s.SwatchId = p.DefaultSwatchId
    WHERE   p.IsActive = 1
    ORDER BY p.SoldCount DESC, p.ProductId;

    /*  4 — latest orders                                                    */
    SELECT TOP (5)
            o.OrderId, o.Reference, c.Name AS CustomerName, o.Total, o.PaymentMethod,
            o.Status, o.PlacedAt,
            STUFF((SELECT N', ' + ol.ProductName FROM dbo.OrderLines AS ol
                   WHERE ol.OrderId = o.OrderId ORDER BY ol.OrderLineId
                   FOR XML PATH(N''), TYPE).value(N'.', N'NVARCHAR(400)'), 1, 2, N'') AS ItemSummary
    FROM    dbo.Orders AS o
    JOIN    dbo.Customers AS c ON c.CustomerId = o.CustomerId
    ORDER BY o.PlacedAt DESC, o.OrderId DESC;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO
