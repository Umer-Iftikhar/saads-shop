/*  Saad's Shop — inventory, the stitching floor, and customers
    ======================================================================== */

SET NOCOUNT ON;
GO

/* ═════════════════════════════════════════════════════════════════════════
   Inventory (shop panel · 10)
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Inventory_GetList
    @Search       NVARCHAR(128) = NULL,
    @LowStockOnly BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @ProductCount INT = 0, @LowCount INT = 0;

    CREATE TABLE #Inv (
        ProductId INT, Name NVARCHAR(128), CategoryName NVARCHAR(64), Price DECIMAL(12,2),
        Stock INT, LowStockAt INT, StockLabel NVARCHAR(24), DefaultSwatchId INT,
        SwatchColorValue NVARCHAR(64), SwatchWeave NVARCHAR(16), IsActive BIT,
        SortIndex INT IDENTITY(1,1)
    );

    BEGIN TRY
        IF LEN(ISNULL(@Search, N'')) > 128
            SELECT @ResponseCode = 1004, @ResponseMessage = N'Search text is too long.';

        IF @ResponseCode = 0
        BEGIN
            DECLARE @Pattern NVARCHAR(140) = NULL;
            IF NULLIF(LTRIM(RTRIM(@Search)), N'') IS NOT NULL
                SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(@Search)), N'\', N'\\'), N'%', N'\%'), N'_', N'\_') + N'%';

            INSERT INTO #Inv (ProductId, Name, CategoryName, Price, Stock, LowStockAt, StockLabel,
                              DefaultSwatchId, SwatchColorValue, SwatchWeave, IsActive)
            SELECT  p.ProductId, p.Name, c.Name, p.Price, p.Stock, p.LowStockAt,
                    /*  The label the panel shows, decided here so the storefront,
                        the panel and any future report agree on what 'low' means. */
                    CASE WHEN p.Stock <= 0             THEN N'Out of stock'
                         WHEN p.Stock <  p.LowStockAt  THEN N'Low — reorder'
                         WHEN p.Stock <  p.LowStockAt * 3 THEN N'Fine'
                         ELSE N'Plenty' END,
                    p.DefaultSwatchId, s.ColorValue, s.Weave, p.IsActive
            FROM    dbo.Products AS p
            JOIN    dbo.Categories AS c ON c.CategoryId = p.CategoryId
            LEFT JOIN dbo.Swatches AS s ON s.SwatchId = p.DefaultSwatchId
            WHERE   p.IsActive = 1
              AND   (@Pattern IS NULL OR p.Name LIKE @Pattern ESCAPE N'\')
              AND   (@LowStockOnly = 0 OR p.Stock < p.LowStockAt)
            ORDER BY CASE WHEN p.Stock < p.LowStockAt THEN 0 ELSE 1 END, p.Name;

            SELECT @ProductCount = COUNT(*), @LowCount = SUM(CASE WHEN Stock < LowStockAt THEN 1 ELSE 0 END)
            FROM   dbo.Products WHERE IsActive = 1;
        END
    END TRY
    BEGIN CATCH
        DELETE FROM #Inv;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not load inventory. Please try again.';
    END CATCH

    SELECT ProductId, Name, CategoryName, Price, Stock, LowStockAt, StockLabel,
           DefaultSwatchId, SwatchColorValue, SwatchWeave, IsActive
    FROM   #Inv ORDER BY SortIndex;

    SELECT ISNULL(@ProductCount, 0) AS ProductCount, ISNULL(@LowCount, 0) AS LowStockCount;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Signed stock movement. Locks the row so two people adding stock at the
    same counter cannot both read 4 and both write 9.                        */
CREATE OR ALTER PROCEDURE dbo.usp_Product_AdjustStock
    @ProductId   INT,
    @Delta       INT,
    @Reason      NVARCHAR(200),
    @ActorUserId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Stock INT = NULL, @NewStock INT = NULL;

    BEGIN TRY
        IF @ProductId IS NULL OR @ProductId <= 0
            SELECT @ResponseCode = 1001, @ResponseMessage = N'Product id is required.';
        ELSE IF @Delta IS NULL OR @Delta = 0
            SELECT @ResponseCode = 1002, @ResponseMessage = N'Enter how many pieces to add or remove.';
        ELSE IF ABS(@Delta) > 100000
            SELECT @ResponseCode = 1002, @ResponseMessage = N'That adjustment is too large.';
        ELSE IF NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
            SELECT @ResponseCode = 1001, @ResponseMessage = N'Give a reason for the adjustment.';
        ELSE IF LEN(@Reason) > 200
            SELECT @ResponseCode = 1004, @ResponseMessage = N'That reason is too long.';

        IF @ResponseCode = 0
        BEGIN
            BEGIN TRANSACTION;

                SELECT @Stock = Stock FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE ProductId = @ProductId;

                IF @Stock IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 2001, @ResponseMessage = N'That product no longer exists.';
                END
                ELSE IF @Stock + @Delta < 0
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 1002,
                           @ResponseMessage = N'There are only ' + CAST(@Stock AS NVARCHAR(12)) + N' in stock.';
                END
                ELSE
                BEGIN
                    SET @NewStock = @Stock + @Delta;

                    UPDATE dbo.Products SET Stock = @NewStock, UpdatedAt = SYSUTCDATETIME()
                    WHERE  ProductId = @ProductId;

                    INSERT INTO dbo.InventoryAdjustments (ProductId, Delta, ResultingStock, Reason, ActorUserId)
                    VALUES (@ProductId, @Delta, @NewStock, LTRIM(RTRIM(@Reason)), @ActorUserId);

                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'Stock updated.';
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @NewStock = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not update stock. Please try again.';
    END CATCH

    SELECT @ProductId AS ProductId, @NewStock AS Stock;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Stitching queue (shop panel · 12)
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_StitchingQueue_Get
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        /*  One flat set; the panel groups it into the four board columns.
            Ordering by due date puts the work that is late at the top of
            each column, which is what the floor actually needs to see.      */
        SELECT  j.StitchingJobId, j.OrderId, o.Reference, j.Title, j.Stage,
                j.AssignedTo, j.DueDate, j.SwatchId,
                s.ColorValue AS SwatchColorValue, s.Weave AS SwatchWeave,
                CASE WHEN j.DueDate IS NOT NULL AND j.DueDate < CAST(SYSUTCDATETIME() AS DATE)
                     THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsOverdue
        FROM    dbo.StitchingJobs AS j
        JOIN    dbo.Orders AS o ON o.OrderId = j.OrderId
        LEFT JOIN dbo.Swatches AS s ON s.SwatchId = j.SwatchId
        WHERE   j.Stage <> N'Done'
        ORDER BY CASE j.Stage WHEN N'Measuring' THEN 1 WHEN N'Cutting' THEN 2
                              WHEN N'Stitching' THEN 3 ELSE 4 END,
                 CASE WHEN j.DueDate IS NULL THEN 1 ELSE 0 END, j.DueDate, j.StitchingJobId;

        SELECT  Stage, COUNT(*) AS JobCount
        FROM    dbo.StitchingJobs WHERE Stage <> N'Done' GROUP BY Stage;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not load the stitching queue. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_StitchingJob_Create
    @OrderId     INT,
    @Title       NVARCHAR(160),
    @AssignedTo  NVARCHAR(128) = NULL,
    @SwatchId    INT           = NULL,
    @DueDate     DATE          = NULL,
    @OrderLineId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @JobId INT = NULL;

    BEGIN TRY
        IF @OrderId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @OrderId)
            SELECT @ResponseCode = 2002, @ResponseMessage = N'That order no longer exists.';
        ELSE IF NULLIF(LTRIM(RTRIM(@Title)), N'') IS NULL
            SELECT @ResponseCode = 1001, @ResponseMessage = N'Give the job a description.';
        ELSE IF LEN(@Title) > 160
            SELECT @ResponseCode = 1004, @ResponseMessage = N'That description is too long.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = @OrderId AND Status IN (N'Delivered', N'Cancelled'))
            SELECT @ResponseCode = 3003, @ResponseMessage = N'That order is closed.';
        ELSE IF @DueDate IS NOT NULL AND @DueDate < DATEADD(YEAR, -1, CAST(SYSUTCDATETIME() AS DATE))
            SELECT @ResponseCode = 1002, @ResponseMessage = N'That due date is not sensible.';
        ELSE IF @OrderLineId IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.OrderLines WHERE OrderLineId = @OrderLineId AND OrderId = @OrderId)
            SELECT @ResponseCode = 1003, @ResponseMessage = N'That item is not on this order.';

        IF @ResponseCode = 0
        BEGIN
            INSERT INTO dbo.StitchingJobs (OrderId, OrderLineId, Title, Stage, AssignedTo, SwatchId, DueDate)
            VALUES (@OrderId, @OrderLineId, LTRIM(RTRIM(@Title)), N'Measuring', @AssignedTo, @SwatchId, @DueDate);

            SET @JobId = CAST(SCOPE_IDENTITY() AS INT);
            SET @ResponseMessage = N'Job added to the floor.';
        END
    END TRY
    BEGIN CATCH
        SET @JobId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not add that job. Please try again.';
    END CATCH

    SELECT @JobId AS StitchingJobId;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_StitchingJob_Update
    @StitchingJobId INT,
    @Stage          NVARCHAR(24)  = NULL,
    @AssignedTo     NVARCHAR(128) = NULL,
    @DueDate        DATE          = NULL,
    @ClearDueDate   BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @CurrentStage NVARCHAR(24) = NULL;

    BEGIN TRY
        IF @StitchingJobId IS NULL OR @StitchingJobId <= 0
            SELECT @ResponseCode = 1001, @ResponseMessage = N'Job id is required.';
        ELSE IF @Stage IS NOT NULL AND @Stage NOT IN (N'Measuring', N'Cutting', N'Stitching', N'Ready', N'Done')
            SELECT @ResponseCode = 1003, @ResponseMessage = N'That is not a stage on the floor.';
        ELSE IF @Stage IS NULL AND @AssignedTo IS NULL AND @DueDate IS NULL AND @ClearDueDate = 0
            SELECT @ResponseCode = 1001, @ResponseMessage = N'Nothing to change.';

        IF @ResponseCode = 0
        BEGIN
            BEGIN TRANSACTION;

                SELECT @CurrentStage = Stage FROM dbo.StitchingJobs WITH (UPDLOCK, HOLDLOCK)
                WHERE  StitchingJobId = @StitchingJobId;

                IF @CurrentStage IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT @ResponseCode = 2002, @ResponseMessage = N'That job no longer exists.';
                END
                ELSE
                BEGIN
                    UPDATE dbo.StitchingJobs
                    SET    Stage       = ISNULL(@Stage, Stage),
                           AssignedTo  = ISNULL(@AssignedTo, AssignedTo),
                           DueDate     = CASE WHEN @ClearDueDate = 1 THEN NULL
                                              ELSE ISNULL(@DueDate, DueDate) END,
                           CompletedAt = CASE WHEN @Stage = N'Done' THEN SYSUTCDATETIME() ELSE CompletedAt END,
                           UpdatedAt   = SYSUTCDATETIME()
                    WHERE  StitchingJobId = @StitchingJobId;

                    COMMIT TRANSACTION;
                    SET @ResponseMessage = N'Job updated.';
                END
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not update the job. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ═════════════════════════════════════════════════════════════════════════
   Customers (shop panel · 13)
   ═════════════════════════════════════════════════════════════════════════ */

CREATE OR ALTER PROCEDURE dbo.usp_Customer_GetList
    @Search   NVARCHAR(128) = NULL,
    @Page     INT           = 1,
    @PageSize INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 0, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Total INT = 0;

    CREATE TABLE #Cust (
        CustomerId INT, Name NVARCHAR(128), Phone NVARCHAR(20), Area NVARCHAR(96),
        OrderCount INT, TotalSpent DECIMAL(12,2), LastOrderAt DATETIME2(3),
        SortIndex INT IDENTITY(1,1)
    );

    BEGIN TRY
        IF @Page IS NULL OR @Page < 1
            SELECT @ResponseCode = 1002, @ResponseMessage = N'Page must be 1 or more.';
        ELSE IF @PageSize IS NULL OR @PageSize < 1 OR @PageSize > 200
            SELECT @ResponseCode = 1002, @ResponseMessage = N'Page size must be between 1 and 200.';

        IF @ResponseCode = 0
        BEGIN
            DECLARE @Pattern NVARCHAR(140) = NULL;
            IF NULLIF(LTRIM(RTRIM(@Search)), N'') IS NOT NULL
                SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(@Search)), N'\', N'\\'), N'%', N'\%'), N'_', N'\_') + N'%';

            /*  Cancelled orders are excluded from lifetime spend — counting
                money the shop never took would flatter every total.          */
            INSERT INTO #Cust (CustomerId, Name, Phone, Area, OrderCount, TotalSpent, LastOrderAt)
            SELECT  c.CustomerId, c.Name, c.Phone, c.Area,
                    COUNT(o.OrderId),
                    ISNULL(SUM(CASE WHEN o.Status <> N'Cancelled' THEN o.Total ELSE 0 END), 0),
                    MAX(o.PlacedAt)
            FROM    dbo.Customers AS c
            LEFT JOIN dbo.Orders AS o ON o.CustomerId = c.CustomerId
            WHERE   (@Pattern IS NULL OR c.Name  LIKE @Pattern ESCAPE N'\'
                                      OR c.Phone LIKE @Pattern ESCAPE N'\'
                                      OR c.Area  LIKE @Pattern ESCAPE N'\')
            GROUP BY c.CustomerId, c.Name, c.Phone, c.Area
            ORDER BY MAX(o.PlacedAt) DESC, c.CustomerId
            OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT @Total = COUNT(*)
            FROM   dbo.Customers AS c
            WHERE  (@Pattern IS NULL OR c.Name  LIKE @Pattern ESCAPE N'\'
                                     OR c.Phone LIKE @Pattern ESCAPE N'\'
                                     OR c.Area  LIKE @Pattern ESCAPE N'\');
        END
    END TRY
    BEGIN CATCH
        DELETE FROM #Cust;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 9001, @ResponseMessage = N'Could not load customers. Please try again.';
    END CATCH

    SELECT CustomerId, Name, Phone, Area, OrderCount, TotalSpent, LastOrderAt
    FROM   #Cust ORDER BY SortIndex;

    SELECT @Total AS TotalCount, @Page AS Page, @PageSize AS PageSize;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO
