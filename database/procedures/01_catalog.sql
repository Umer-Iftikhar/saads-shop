/*  Saad's Shop — catalogue procedures
    ========================================================================
    Contract (docs/database.md): payload result sets first, status row last:

        SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;

    Every procedure emits its payload sets in a FIXED order and a FIXED shape,
    empty ones included, even when it is failing. Dapper's QueryMultipleAsync
    reads sets positionally — a procedure that skips a set on the error path
    would make the reader throw while trying to read the status it needs to
    report the error, turning a clean 400 into a 500.
*/

SET NOCOUNT ON;
GO

/* ─────────────────────────────────────────────────────────────────────────
   Reference data
   ───────────────────────────────────────────────────────────────────────── */

CREATE OR ALTER PROCEDURE dbo.usp_Category_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        SELECT  CategoryId, Name, Slug, SortOrder
        FROM    dbo.Categories
        WHERE   IsActive = 1
        ORDER BY SortOrder, Name;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load categories. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Swatch_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        SELECT  SwatchId, Name, ColorValue, Weave, ImagePath, SortOrder
        FROM    dbo.Swatches
        WHERE   IsActive = 1
        ORDER BY SortOrder, Name;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load the cloth palette. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_BedSize_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        SELECT BedSizeCode, Name, PriceAdjustment, SortOrder
        FROM   dbo.BedSizes
        ORDER  BY SortOrder;
    END TRY
    BEGIN CATCH
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load bed sizes. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ─────────────────────────────────────────────────────────────────────────
   Product listing — storefront category pages and admin search

   Result sets: 1) page of products   2) total row count   3) status
   ───────────────────────────────────────────────────────────────────────── */

CREATE OR ALTER PROCEDURE dbo.usp_Product_GetList
    @CategorySlug   NVARCHAR(64)  = NULL,
    @Search         NVARCHAR(128) = NULL,
    @IncludeInactive BIT          = 0,
    @SortBy         NVARCHAR(24)  = N'Featured',
    @Page           INT           = 1,
    @PageSize       INT           = 24
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @Total INT = 0;

    CREATE TABLE #Page (
        ProductId INT, Name NVARCHAR(128), Slug NVARCHAR(160), CategoryId INT,
        CategoryName NVARCHAR(64), Kicker NVARCHAR(48), Blurb NVARCHAR(280),
        Price DECIMAL(12,2), Pieces NVARCHAR(48), Stock INT, StitchingDays INT,
        DefaultSwatchId INT, SwatchName NVARCHAR(48), SwatchColorValue NVARCHAR(64),
        SwatchWeave NVARCHAR(16), IsActive BIT, SortIndex INT IDENTITY(1,1)
    );

    BEGIN TRY
        /* --- validate ------------------------------------------------- */
        IF @Page IS NULL OR @Page < 1
            SELECT @ResponseCode = 400, @ResponseMessage = N'Page must be 1 or more.';
        ELSE IF @PageSize IS NULL OR @PageSize < 1 OR @PageSize > 100
            SELECT @ResponseCode = 400, @ResponseMessage = N'Page size must be between 1 and 100.';
        ELSE IF LEN(ISNULL(@Search, N'')) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'Search text is too long.';
        /*  @SortBy is compared against a closed list rather than being
            concatenated into the query. It never reaches the SQL text, so a
            hostile value is simply rejected — there is nothing to inject into. */
        ELSE IF @SortBy NOT IN (N'Featured', N'PriceAsc', N'PriceDesc', N'Newest', N'Name')
            SELECT @ResponseCode = 400, @ResponseMessage = N'Unknown sort order.';
        ELSE IF @CategorySlug IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = @CategorySlug AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That category does not exist.';

        IF @ResponseCode = 200
        BEGIN
            DECLARE @SearchPattern NVARCHAR(140) = NULL;
            IF NULLIF(LTRIM(RTRIM(@Search)), N'') IS NOT NULL
                /*  ESCAPE guards the LIKE wildcards: a customer searching for
                    '100%' or 'a_b' means those characters literally.          */
                SET @SearchPattern = N'%' + REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(@Search)), N'\', N'\\'), N'%', N'\%'), N'_', N'\_') + N'%';

            ;WITH filtered AS (
                SELECT  p.ProductId, p.Name, p.Slug, p.CategoryId, c.Name AS CategoryName,
                        p.Kicker, p.Blurb, p.Price, p.Pieces, p.Stock, p.StitchingDays,
                        p.DefaultSwatchId, s.Name AS SwatchName, s.ColorValue AS SwatchColorValue,
                        s.Weave AS SwatchWeave, p.IsActive, p.SoldCount, p.CreatedAt
                FROM    dbo.Products AS p
                JOIN    dbo.Categories AS c ON c.CategoryId = p.CategoryId
                LEFT JOIN dbo.Swatches AS s ON s.SwatchId = p.DefaultSwatchId
                WHERE   (@IncludeInactive = 1 OR p.IsActive = 1)
                  AND   (@CategorySlug IS NULL OR c.Slug = @CategorySlug)
                  AND   (@SearchPattern IS NULL
                         OR p.Name  LIKE @SearchPattern ESCAPE N'\'
                         OR p.Blurb LIKE @SearchPattern ESCAPE N'\')
            )
            INSERT INTO #Page (ProductId, Name, Slug, CategoryId, CategoryName, Kicker, Blurb,
                               Price, Pieces, Stock, StitchingDays, DefaultSwatchId,
                               SwatchName, SwatchColorValue, SwatchWeave, IsActive)
            SELECT  ProductId, Name, Slug, CategoryId, CategoryName, Kicker, Blurb,
                    Price, Pieces, Stock, StitchingDays, DefaultSwatchId,
                    SwatchName, SwatchColorValue, SwatchWeave, IsActive
            FROM    filtered
            ORDER BY
                CASE WHEN @SortBy = N'PriceAsc'  THEN Price END ASC,
                CASE WHEN @SortBy = N'PriceDesc' THEN Price END DESC,
                CASE WHEN @SortBy = N'Newest'    THEN CreatedAt END DESC,
                CASE WHEN @SortBy = N'Name'      THEN Name END ASC,
                CASE WHEN @SortBy = N'Featured'  THEN SoldCount END DESC,
                ProductId                      -- stable tie-break: no page ever repeats a row
            OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT @Total = COUNT(*)
            FROM   dbo.Products AS p
            JOIN   dbo.Categories AS c ON c.CategoryId = p.CategoryId
            WHERE  (@IncludeInactive = 1 OR p.IsActive = 1)
              AND  (@CategorySlug IS NULL OR c.Slug = @CategorySlug)
              AND  (@SearchPattern IS NULL
                    OR p.Name  LIKE @SearchPattern ESCAPE N'\'
                    OR p.Blurb LIKE @SearchPattern ESCAPE N'\');
        END
    END TRY
    BEGIN CATCH
        DELETE FROM #Page;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load products. Please try again.';
    END CATCH

    /* --- single exit: fixed shape, always ------------------------------ */
    SELECT  ProductId, Name, Slug, CategoryId, CategoryName, Kicker, Blurb, Price,
            Pieces, Stock, StitchingDays, DefaultSwatchId, SwatchName,
            SwatchColorValue, SwatchWeave, IsActive
    FROM    #Page
    ORDER BY SortIndex;

    SELECT @Total AS TotalCount, @Page AS Page, @PageSize AS PageSize;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ─────────────────────────────────────────────────────────────────────────
   Single product

   Result sets: 1) product  2) its swatches  3) related products  4) status
   ───────────────────────────────────────────────────────────────────────── */

CREATE OR ALTER PROCEDURE dbo.usp_Product_GetById
    @ProductId       INT           = NULL,
    @Slug            NVARCHAR(160) = NULL,
    @IncludeInactive BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @FoundId INT = NULL, @CategoryId INT = NULL;

    BEGIN TRY
        IF @ProductId IS NULL AND NULLIF(LTRIM(RTRIM(@Slug)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'A product id or slug is required.';
        ELSE IF @ProductId IS NOT NULL AND @ProductId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product id must be a positive number.';

        IF @ResponseCode = 200
        BEGIN
            SELECT @FoundId = p.ProductId, @CategoryId = p.CategoryId
            FROM   dbo.Products AS p
            WHERE  (@ProductId IS NOT NULL AND p.ProductId = @ProductId
                    OR @ProductId IS NULL AND p.Slug = @Slug)
              AND  (@IncludeInactive = 1 OR p.IsActive = 1);

            IF @FoundId IS NULL
                SELECT @ResponseCode = 404, @ResponseMessage = N'That product is no longer in the shop.';
        END
    END TRY
    BEGIN CATCH
        SET @FoundId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not load that product. Please try again.';
    END CATCH

    /*  1 — the product (empty when not found)                              */
    SELECT  p.ProductId, p.Name, p.Slug, p.CategoryId, c.Name AS CategoryName, c.Slug AS CategorySlug,
            p.Kicker, p.Blurb, p.LongDescription, p.Price, p.Pieces, p.StitchingDays,
            p.Stock, p.LowStockAt, p.DefaultSwatchId, p.SoldCount, p.IsActive
    FROM    dbo.Products AS p
    JOIN    dbo.Categories AS c ON c.CategoryId = p.CategoryId
    WHERE   p.ProductId = @FoundId;

    /*  2 — the cloths this product can be made in                          */
    SELECT  s.SwatchId, s.Name, s.ColorValue, s.Weave, s.ImagePath, ps.SortOrder
    FROM    dbo.ProductSwatches AS ps
    JOIN    dbo.Swatches AS s ON s.SwatchId = ps.SwatchId
    WHERE   ps.ProductId = @FoundId AND s.IsActive = 1
    ORDER BY ps.SortOrder, s.Name;

    /*  3 — three more from the same category, for 'you may also like'      */
    SELECT TOP (3)
            p.ProductId, p.Name, p.Slug, p.Price, p.Kicker, p.Pieces,
            p.DefaultSwatchId, s.ColorValue AS SwatchColorValue, s.Weave AS SwatchWeave
    FROM    dbo.Products AS p
    LEFT JOIN dbo.Swatches AS s ON s.SwatchId = p.DefaultSwatchId
    WHERE   p.CategoryId = @CategoryId AND p.ProductId <> @FoundId AND p.IsActive = 1
    ORDER BY p.SoldCount DESC, p.ProductId;

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/* ─────────────────────────────────────────────────────────────────────────
   Product editor — create / update / soft-delete
   ───────────────────────────────────────────────────────────────────────── */

CREATE OR ALTER PROCEDURE dbo.usp_Product_Create
    @Name            NVARCHAR(128),
    @CategoryId      INT,
    @Price           DECIMAL(12,2),
    @Kicker          NVARCHAR(48)   = NULL,
    @Blurb           NVARCHAR(280)  = NULL,
    @LongDescription NVARCHAR(2000) = NULL,
    @Pieces          NVARCHAR(48)   = NULL,
    @StitchingDays   INT            = 3,
    @Stock           INT            = 0,
    @LowStockAt      INT            = 6,
    @DefaultSwatchId INT            = NULL,
    @SwatchIds       dbo.IntListTableType READONLY,
    @ActorUserId     NVARCHAR(128)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';
    DECLARE @NewId INT = NULL, @Slug NVARCHAR(160);

    BEGIN TRY
        /*  Validation is repeated here even though the API validated already.
            The API is one caller; this is the last line that cannot be
            bypassed by a script, a migration or a person with sqlcmd.        */
        IF NULLIF(LTRIM(RTRIM(@Name)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product name is required.';
        ELSE IF LEN(@Name) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product name must be 128 characters or fewer.';
        ELSE IF @CategoryId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE CategoryId = @CategoryId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'Pick a valid category.';
        ELSE IF @Price IS NULL OR @Price < 0 OR @Price > 10000000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Price must be between Rs 0 and Rs 10,000,000.';
        ELSE IF @Stock IS NULL OR @Stock < 0 OR @Stock > 100000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Stock must be between 0 and 100,000.';
        ELSE IF @StitchingDays IS NULL OR @StitchingDays < 0 OR @StitchingDays > 90
            SELECT @ResponseCode = 400, @ResponseMessage = N'Stitching days must be between 0 and 90.';
        ELSE IF @LowStockAt IS NULL OR @LowStockAt < 0 OR @LowStockAt > 100000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Low-stock threshold is out of range.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.Products WHERE Name = @Name)
            SELECT @ResponseCode = 409, @ResponseMessage = N'A product with that name already exists.';
        ELSE IF @DefaultSwatchId IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.Swatches WHERE SwatchId = @DefaultSwatchId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That cloth is not in the shop palette.';
        ELSE IF EXISTS (SELECT 1 FROM @SwatchIds AS s
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.Swatches AS x WHERE x.SwatchId = s.Value AND x.IsActive = 1))
            SELECT @ResponseCode = 404, @ResponseMessage = N'One of the chosen cloths is not in the shop palette.';

        IF @ResponseCode = 200
        BEGIN
            /*  Slug: lower-case, spaces to dashes, punctuation dropped.      */
            SET @Slug = LOWER(LTRIM(RTRIM(@Name)));
            SET @Slug = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@Slug,
                            N'''', N''), N'&', N'and'), N'.', N''), N',', N''), N' ', N'-');
            SET @Slug = LEFT(@Slug, 160);

            IF EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = @Slug)
                SET @Slug = LEFT(@Slug, 150) + N'-' + CAST(ABS(CHECKSUM(NEWID())) % 9999 AS NVARCHAR(8));

            BEGIN TRANSACTION;

                INSERT INTO dbo.Products (Name, Slug, CategoryId, Kicker, Blurb, LongDescription,
                                          Price, Pieces, StitchingDays, Stock, LowStockAt, DefaultSwatchId)
                VALUES (LTRIM(RTRIM(@Name)), @Slug, @CategoryId, @Kicker, @Blurb, @LongDescription,
                        @Price, @Pieces, @StitchingDays, @Stock, @LowStockAt, @DefaultSwatchId);

                SET @NewId = CAST(SCOPE_IDENTITY() AS INT);

                INSERT INTO dbo.ProductSwatches (ProductId, SwatchId, SortOrder)
                SELECT @NewId, s.Value, ROW_NUMBER() OVER (ORDER BY s.Value)
                FROM   @SwatchIds AS s;

                IF @Stock <> 0
                    INSERT INTO dbo.InventoryAdjustments (ProductId, Delta, ResultingStock, Reason, ActorUserId)
                    VALUES (@NewId, @Stock, @Stock, N'Opening stock', @ActorUserId);

            COMMIT TRANSACTION;

            SET @ResponseMessage = N'Product saved.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @NewId = NULL;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save the product. Please try again.';
    END CATCH

    SELECT @NewId AS ProductId, @Slug AS Slug;
    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Product_Update
    @ProductId       INT,
    @Name            NVARCHAR(128),
    @CategoryId      INT,
    @Price           DECIMAL(12,2),
    @Kicker          NVARCHAR(48)   = NULL,
    @Blurb           NVARCHAR(280)  = NULL,
    @LongDescription NVARCHAR(2000) = NULL,
    @Pieces          NVARCHAR(48)   = NULL,
    @StitchingDays   INT            = 3,
    @LowStockAt      INT            = 6,
    @DefaultSwatchId INT            = NULL,
    @IsActive        BIT            = 1,
    @SwatchIds       dbo.IntListTableType READONLY,
    @ActorUserId     NVARCHAR(128)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF @ProductId IS NULL OR @ProductId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product id is required.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That product no longer exists.';
        ELSE IF NULLIF(LTRIM(RTRIM(@Name)), N'') IS NULL
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product name is required.';
        ELSE IF LEN(@Name) > 128
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product name must be 128 characters or fewer.';
        ELSE IF @CategoryId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE CategoryId = @CategoryId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'Pick a valid category.';
        ELSE IF @Price IS NULL OR @Price < 0 OR @Price > 10000000
            SELECT @ResponseCode = 400, @ResponseMessage = N'Price must be between Rs 0 and Rs 10,000,000.';
        ELSE IF @StitchingDays IS NULL OR @StitchingDays < 0 OR @StitchingDays > 90
            SELECT @ResponseCode = 400, @ResponseMessage = N'Stitching days must be between 0 and 90.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.Products WHERE Name = @Name AND ProductId <> @ProductId)
            SELECT @ResponseCode = 409, @ResponseMessage = N'Another product already uses that name.';
        ELSE IF @DefaultSwatchId IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.Swatches WHERE SwatchId = @DefaultSwatchId AND IsActive = 1)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That cloth is not in the shop palette.';
        ELSE IF EXISTS (SELECT 1 FROM @SwatchIds AS s
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.Swatches AS x WHERE x.SwatchId = s.Value AND x.IsActive = 1))
            SELECT @ResponseCode = 404, @ResponseMessage = N'One of the chosen cloths is not in the shop palette.';

        IF @ResponseCode = 200
        BEGIN
            BEGIN TRANSACTION;

                UPDATE dbo.Products
                SET    Name = LTRIM(RTRIM(@Name)), CategoryId = @CategoryId, Kicker = @Kicker,
                       Blurb = @Blurb, LongDescription = @LongDescription, Price = @Price,
                       Pieces = @Pieces, StitchingDays = @StitchingDays, LowStockAt = @LowStockAt,
                       DefaultSwatchId = @DefaultSwatchId, IsActive = @IsActive,
                       UpdatedAt = SYSUTCDATETIME()
                WHERE  ProductId = @ProductId;

                /*  Replace the swatch set wholesale — the editor sends the
                    complete list it wants, not a diff.                       */
                DELETE FROM dbo.ProductSwatches WHERE ProductId = @ProductId;

                INSERT INTO dbo.ProductSwatches (ProductId, SwatchId, SortOrder)
                SELECT @ProductId, s.Value, ROW_NUMBER() OVER (ORDER BY s.Value)
                FROM   @SwatchIds AS s;

            COMMIT TRANSACTION;

            SET @ResponseMessage = N'Product saved.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not save the product. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO

/*  Soft delete. A product referenced by an order line is never removed —
    deleting it would tear a hole in the shop's own sales history.           */
CREATE OR ALTER PROCEDURE dbo.usp_Product_Delete
    @ProductId   INT,
    @ActorUserId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ResponseCode INT = 200, @ResponseMessage NVARCHAR(400) = N'OK';

    BEGIN TRY
        IF @ProductId IS NULL OR @ProductId <= 0
            SELECT @ResponseCode = 400, @ResponseMessage = N'Product id is required.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
            SELECT @ResponseCode = 404, @ResponseMessage = N'That product no longer exists.';
        ELSE IF EXISTS (SELECT 1 FROM dbo.OrderLines WHERE ProductId = @ProductId)
        BEGIN
            /*  Not an error the shopkeeper can fix by trying again — tell them
                what actually happens instead, and do it.                     */
            UPDATE dbo.Products SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE ProductId = @ProductId;
            SELECT @ResponseCode = 200,
                   @ResponseMessage = N'This product is on past orders, so it was hidden from the shop rather than deleted.';
        END
        ELSE
        BEGIN
            UPDATE dbo.Products SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE ProductId = @ProductId;
            SET @ResponseMessage = N'Product removed from the shop.';
        END
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.ErrorLog (ProcedureName, ErrorNumber, ErrorMessage, ErrorLine, ErrorSeverity)
        VALUES (ERROR_PROCEDURE(), ERROR_NUMBER(), ERROR_MESSAGE(), ERROR_LINE(), ERROR_SEVERITY());
        SELECT @ResponseCode = 500, @ResponseMessage = N'Could not remove the product. Please try again.';
    END CATCH

    SELECT @ResponseCode AS ResponseCode, @ResponseMessage AS ResponseMessage;
END
GO
