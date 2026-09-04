/*  Saad's Shop — tables
    ------------------------------------------------------------------------
    Re-runnable: every object is guarded, so this applies cleanly to a fresh
    database or an existing one.

    Money is DECIMAL(12,2) everywhere. Never FLOAT — Rs 18,500.55 has no exact
    binary representation and a shop's totals must not drift.

    Times are DATETIME2(3) in UTC via SYSUTCDATETIME(). The shop reads times in
    PKT; the conversion is the client's job, not the database's.
*/

SET NOCOUNT ON;
GO

/* ═══════════════════════════════════════════════════════════════════════════
   Identity — ASP.NET Core Identity shapes, reached through Dapper + procs
   rather than EF Core. Ids are NVARCHAR(128) to match Identity's default.
   ═══════════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Roles (
        Id               NVARCHAR(128)  NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
        Name             NVARCHAR(64)   NOT NULL,
        NormalizedName   NVARCHAR(64)   NOT NULL,
        ConcurrencyStamp NVARCHAR(64)   NULL
    );
    CREATE UNIQUE INDEX UX_Roles_NormalizedName ON dbo.Roles (NormalizedName);
END
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        Id                   NVARCHAR(128)  NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        UserName             NVARCHAR(256)  NOT NULL,
        NormalizedUserName   NVARCHAR(256)  NOT NULL,
        Email                NVARCHAR(256)  NOT NULL,
        NormalizedEmail      NVARCHAR(256)  NOT NULL,
        EmailConfirmed       BIT            NOT NULL CONSTRAINT DF_Users_EmailConfirmed DEFAULT (0),
        PasswordHash         NVARCHAR(MAX)  NULL,
        SecurityStamp        NVARCHAR(128)  NULL,
        ConcurrencyStamp     NVARCHAR(64)   NULL,
        PhoneNumber          NVARCHAR(32)   NULL,
        FullName             NVARCHAR(128)  NOT NULL,
        TwoFactorEnabled     BIT            NOT NULL CONSTRAINT DF_Users_TwoFactorEnabled DEFAULT (0),
        LockoutEnd           DATETIMEOFFSET NULL,
        LockoutEnabled       BIT            NOT NULL CONSTRAINT DF_Users_LockoutEnabled DEFAULT (1),
        AccessFailedCount    INT            NOT NULL CONSTRAINT DF_Users_AccessFailedCount DEFAULT (0),
        IsActive             BIT            NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        CreatedAt            DATETIME2(3)   NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt            DATETIME2(3)   NULL,
        CONSTRAINT CK_Users_Email CHECK (Email LIKE N'%_@_%._%')
    );
    CREATE UNIQUE INDEX UX_Users_NormalizedUserName ON dbo.Users (NormalizedUserName);
    CREATE UNIQUE INDEX UX_Users_NormalizedEmail    ON dbo.Users (NormalizedEmail);
END
GO

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRoles (
        UserId NVARCHAR(128) NOT NULL,
        RoleId NVARCHAR(128) NOT NULL,
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id) ON DELETE CASCADE
    );
END
GO

/*  External logins (Google). ProviderKey is the provider's subject id.        */
IF OBJECT_ID(N'dbo.UserLogins', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserLogins (
        LoginProvider       NVARCHAR(128) NOT NULL,
        ProviderKey         NVARCHAR(256) NOT NULL,
        ProviderDisplayName NVARCHAR(128) NULL,
        UserId              NVARCHAR(128) NOT NULL,
        LinkedAt            DATETIME2(3)  NOT NULL CONSTRAINT DF_UserLogins_LinkedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_UserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_UserLogins_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_UserLogins_UserId ON dbo.UserLogins (UserId);
END
GO

/*  Identity token store — holds the protected TOTP secret, among others.
    Value is encrypted by the application's data-protection stack before it
    reaches this table; the database never sees a usable TOTP seed.           */
IF OBJECT_ID(N'dbo.UserTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserTokens (
        UserId        NVARCHAR(128) NOT NULL,
        LoginProvider NVARCHAR(128) NOT NULL,
        Name          NVARCHAR(128) NOT NULL,
        Value         NVARCHAR(MAX) NULL,
        CONSTRAINT PK_UserTokens PRIMARY KEY (UserId, LoginProvider, Name),
        CONSTRAINT FK_UserTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
END
GO

/*  Refresh tokens — rotated on every use, stored only as a SHA-256 hash.
    FamilyId chains a lineage: redeeming an already-used token means the
    lineage leaked, and the whole family is revoked at once.                  */
IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens (
        RefreshTokenId    BIGINT         IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
        UserId            NVARCHAR(128)  NOT NULL,
        TokenHash         BINARY(32)     NOT NULL,
        FamilyId          UNIQUEIDENTIFIER NOT NULL,
        CreatedAt         DATETIME2(3)   NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAt         DATETIME2(3)   NOT NULL,
        UsedAt            DATETIME2(3)   NULL,
        RevokedAt         DATETIME2(3)   NULL,
        RevokedReason     NVARCHAR(128)  NULL,
        CreatedByIp       NVARCHAR(64)   NULL,
        ReplacedByTokenId BIGINT         NULL,
        CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_RefreshTokens_TokenHash ON dbo.RefreshTokens (TokenHash);
    CREATE INDEX IX_RefreshTokens_UserId   ON dbo.RefreshTokens (UserId);
    CREATE INDEX IX_RefreshTokens_FamilyId ON dbo.RefreshTokens (FamilyId);
END
GO

/*  Single-use 2FA recovery codes, stored hashed.                             */
IF OBJECT_ID(N'dbo.RecoveryCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecoveryCodes (
        RecoveryCodeId BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecoveryCodes PRIMARY KEY,
        UserId         NVARCHAR(128) NOT NULL,
        CodeHash       BINARY(32)    NOT NULL,
        CreatedAt      DATETIME2(3)  NOT NULL CONSTRAINT DF_RecoveryCodes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UsedAt         DATETIME2(3)  NULL,
        CONSTRAINT FK_RecoveryCodes_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_RecoveryCodes_UserId ON dbo.RecoveryCodes (UserId);
END
GO

/* ═══════════════════════════════════════════════════════════════════════════
   Catalogue
   ═══════════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categories (
        CategoryId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
        Name       NVARCHAR(64)  NOT NULL,
        Slug       NVARCHAR(64)  NOT NULL,
        SortOrder  INT           NOT NULL CONSTRAINT DF_Categories_SortOrder DEFAULT (0),
        IsActive   BIT           NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_Categories_Slug ON dbo.Categories (Slug);
END
GO

/*  The shop's cloth palette. Fabric is drawn in CSS from (ColorValue, Weave)
    until real photographs arrive — see docs/design-system.md.                */
IF OBJECT_ID(N'dbo.Swatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Swatches (
        SwatchId   INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Swatches PRIMARY KEY,
        Name       NVARCHAR(48) NOT NULL,
        ColorValue NVARCHAR(64) NOT NULL,          -- hex or oklch(), as authored in the design
        Weave      NVARCHAR(16) NOT NULL CONSTRAINT DF_Swatches_Weave DEFAULT (N'Woven'),
        ImagePath  NVARCHAR(512) NULL,             -- set when a real photo replaces the gradient
        SortOrder  INT          NOT NULL CONSTRAINT DF_Swatches_SortOrder DEFAULT (0),
        IsActive   BIT          NOT NULL CONSTRAINT DF_Swatches_IsActive DEFAULT (1),
        CONSTRAINT CK_Swatches_Weave CHECK (Weave IN (N'Woven', N'Striped', N'Floral'))
    );
    CREATE UNIQUE INDEX UX_Swatches_Name ON dbo.Swatches (Name);
END
GO

IF OBJECT_ID(N'dbo.BedSizes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BedSizes (
        BedSizeCode     NVARCHAR(16)   NOT NULL CONSTRAINT PK_BedSizes PRIMARY KEY,
        Name            NVARCHAR(32)   NOT NULL,
        PriceAdjustment DECIMAL(12,2)  NOT NULL CONSTRAINT DF_BedSizes_PriceAdjustment DEFAULT (0),
        SortOrder       INT            NOT NULL CONSTRAINT DF_BedSizes_SortOrder DEFAULT (0)
    );
END
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products (
        ProductId       INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        Name            NVARCHAR(128)  NOT NULL,
        Slug            NVARCHAR(160)  NOT NULL,
        CategoryId      INT            NOT NULL,
        Kicker          NVARCHAR(48)   NULL,        -- 'Bridal bedding', 'Parde', 'Chhata'
        Blurb           NVARCHAR(280)  NULL,        -- one line on the card
        LongDescription NVARCHAR(2000) NULL,        -- the product page
        Price           DECIMAL(12,2)  NOT NULL,
        Pieces          NVARCHAR(48)   NULL,        -- '12 pieces', 'Pair', 'One'
        StitchingDays   INT            NOT NULL CONSTRAINT DF_Products_StitchingDays DEFAULT (3),
        Stock           INT            NOT NULL CONSTRAINT DF_Products_Stock DEFAULT (0),
        LowStockAt      INT            NOT NULL CONSTRAINT DF_Products_LowStockAt DEFAULT (6),
        DefaultSwatchId INT            NULL,
        SoldCount       INT            NOT NULL CONSTRAINT DF_Products_SoldCount DEFAULT (0),
        IsActive        BIT            NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT (1),
        CreatedAt       DATETIME2(3)   NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt       DATETIME2(3)   NULL,
        RowVersion      ROWVERSION     NOT NULL,
        CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories (CategoryId),
        CONSTRAINT FK_Products_Swatches   FOREIGN KEY (DefaultSwatchId) REFERENCES dbo.Swatches (SwatchId),
        -- Stock can never go below zero even if a procedure is bypassed entirely.
        CONSTRAINT CK_Products_Stock         CHECK (Stock >= 0),
        CONSTRAINT CK_Products_Price         CHECK (Price >= 0 AND Price <= 10000000),
        CONSTRAINT CK_Products_StitchingDays CHECK (StitchingDays BETWEEN 0 AND 90)
    );
    CREATE UNIQUE INDEX UX_Products_Slug ON dbo.Products (Slug);
    CREATE UNIQUE INDEX UX_Products_Name ON dbo.Products (Name);
END
GO

IF OBJECT_ID(N'dbo.ProductSwatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductSwatches (
        ProductId INT NOT NULL,
        SwatchId  INT NOT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_ProductSwatches_SortOrder DEFAULT (0),
        CONSTRAINT PK_ProductSwatches PRIMARY KEY (ProductId, SwatchId),
        CONSTRAINT FK_ProductSwatches_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId) ON DELETE CASCADE,
        CONSTRAINT FK_ProductSwatches_Swatches FOREIGN KEY (SwatchId)  REFERENCES dbo.Swatches (SwatchId)
    );
END
GO

/* ═══════════════════════════════════════════════════════════════════════════
   Customers and orders
   ═══════════════════════════════════════════════════════════════════════════ */

/*  A customer is identified by phone — that is how this shop actually works.
    UserId stays NULL until customer accounts exist; the column is here so
    adding them later does not rewrite the orders table.                      */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers (
        CustomerId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
        Name       NVARCHAR(128) NOT NULL,
        Phone      NVARCHAR(20)  NOT NULL,          -- normalised to 03xxxxxxxxx
        Area       NVARCHAR(96)  NULL,
        Address    NVARCHAR(400) NULL,
        UserId     NVARCHAR(128) NULL,              -- future customer accounts
        CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt  DATETIME2(3)  NULL,
        CONSTRAINT FK_Customers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT CK_Customers_Phone CHECK (Phone LIKE N'03[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
    );
    CREATE UNIQUE INDEX UX_Customers_Phone ON dbo.Customers (Phone);
END
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (
        OrderId         INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
        Reference       NVARCHAR(16)   NOT NULL,     -- SS-2419
        CustomerId      INT            NOT NULL,
        Status          NVARCHAR(24)   NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (N'Placed'),
        PaymentMethod   NVARCHAR(24)   NOT NULL,
        Subtotal        DECIMAL(12,2)  NOT NULL,
        DeliveryCharge  DECIMAL(12,2)  NOT NULL CONSTRAINT DF_Orders_DeliveryCharge DEFAULT (0),
        Total           DECIMAL(12,2)  NOT NULL,
        DeliveryAddress NVARCHAR(400)  NOT NULL,
        Notes           NVARCHAR(1000) NULL,
        PlacedAt        DATETIME2(3)   NOT NULL CONSTRAINT DF_Orders_PlacedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt       DATETIME2(3)   NULL,
        CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId),
        CONSTRAINT CK_Orders_Status CHECK (Status IN (N'Placed', N'Measuring', N'Stitching', N'Ready', N'Delivered', N'Cancelled')),
        CONSTRAINT CK_Orders_PaymentMethod CHECK (PaymentMethod IN (N'CashOnDelivery', N'WhatsApp', N'ReserveInShop', N'Card')),
        CONSTRAINT CK_Orders_Money CHECK (Subtotal >= 0 AND DeliveryCharge >= 0 AND Total >= 0)
    );
    CREATE UNIQUE INDEX UX_Orders_Reference ON dbo.Orders (Reference);
END
GO

/*  Product name, swatch name and unit price are SNAPSHOTTED onto the line.
    An order is a historical record: renaming a product or changing its price
    next season must not silently rewrite what a customer was charged.        */
IF OBJECT_ID(N'dbo.OrderLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrderLines (
        OrderLineId INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLines PRIMARY KEY,
        OrderId     INT            NOT NULL,
        ProductId   INT            NOT NULL,
        ProductName NVARCHAR(128)  NOT NULL,
        SwatchId    INT            NULL,
        SwatchName  NVARCHAR(48)   NULL,
        BedSize     NVARCHAR(16)   NULL,
        UnitPrice   DECIMAL(12,2)  NOT NULL,
        Quantity    INT            NOT NULL,
        LineTotal   DECIMAL(12,2)  NOT NULL,
        CONSTRAINT FK_OrderLines_Orders   FOREIGN KEY (OrderId)   REFERENCES dbo.Orders (OrderId) ON DELETE CASCADE,
        CONSTRAINT FK_OrderLines_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
        CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity > 0 AND Quantity <= 999),
        CONSTRAINT CK_OrderLines_Money    CHECK (UnitPrice >= 0 AND LineTotal >= 0)
    );
    CREATE INDEX IX_OrderLines_OrderId   ON dbo.OrderLines (OrderId);
    CREATE INDEX IX_OrderLines_ProductId ON dbo.OrderLines (ProductId);
END
GO

IF OBJECT_ID(N'dbo.OrderStatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrderStatusHistory (
        OrderStatusHistoryId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderStatusHistory PRIMARY KEY,
        OrderId              INT           NOT NULL,
        FromStatus           NVARCHAR(24)  NULL,
        ToStatus             NVARCHAR(24)  NOT NULL,
        ChangedByUserId      NVARCHAR(128) NULL,
        Note                 NVARCHAR(400) NULL,
        ChangedAt            DATETIME2(3)  NOT NULL CONSTRAINT DF_OrderStatusHistory_ChangedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_OrderStatusHistory_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId) ON DELETE CASCADE
    );
    CREATE INDEX IX_OrderStatusHistory_OrderId ON dbo.OrderStatusHistory (OrderId);
END
GO

/*  'Measurements: bed 78x60in, windows 84in drop, taken by Nasir on 4 Sep'    */
IF OBJECT_ID(N'dbo.OrderMeasurements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrderMeasurements (
        OrderMeasurementId INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderMeasurements PRIMARY KEY,
        OrderId            INT            NOT NULL,
        BedWidthIn         DECIMAL(6,2)   NULL,
        BedLengthIn        DECIMAL(6,2)   NULL,
        WindowDropIn       DECIMAL(6,2)   NULL,
        WindowCount        INT            NULL,
        Notes              NVARCHAR(1000) NULL,
        TakenBy            NVARCHAR(128)  NULL,
        TakenAt            DATETIME2(3)   NOT NULL CONSTRAINT DF_OrderMeasurements_TakenAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_OrderMeasurements_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId) ON DELETE CASCADE,
        CONSTRAINT CK_OrderMeasurements_Dimensions CHECK (
            (BedWidthIn   IS NULL OR BedWidthIn   BETWEEN 1 AND 200) AND
            (BedLengthIn  IS NULL OR BedLengthIn  BETWEEN 1 AND 200) AND
            (WindowDropIn IS NULL OR WindowDropIn BETWEEN 1 AND 300) AND
            (WindowCount  IS NULL OR WindowCount  BETWEEN 0 AND 100)
        )
    );
    CREATE INDEX IX_OrderMeasurements_OrderId ON dbo.OrderMeasurements (OrderId);
END
GO

/* ═══════════════════════════════════════════════════════════════════════════
   The stitching floor
   ═══════════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.StitchingJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StitchingJobs (
        StitchingJobId INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_StitchingJobs PRIMARY KEY,
        OrderId        INT           NOT NULL,
        OrderLineId    INT           NULL,
        Title          NVARCHAR(160) NOT NULL,      -- 'Velvet drapes, 3 windows'
        Stage          NVARCHAR(24)  NOT NULL CONSTRAINT DF_StitchingJobs_Stage DEFAULT (N'Measuring'),
        AssignedTo     NVARCHAR(128) NULL,          -- Nasir, Rafiq, Shakeel, Front desk
        SwatchId       INT           NULL,
        DueDate        DATE          NULL,
        CreatedAt      DATETIME2(3)  NOT NULL CONSTRAINT DF_StitchingJobs_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      DATETIME2(3)  NULL,
        CompletedAt    DATETIME2(3)  NULL,
        CONSTRAINT FK_StitchingJobs_Orders     FOREIGN KEY (OrderId)     REFERENCES dbo.Orders (OrderId) ON DELETE CASCADE,
        CONSTRAINT FK_StitchingJobs_OrderLines FOREIGN KEY (OrderLineId) REFERENCES dbo.OrderLines (OrderLineId),
        CONSTRAINT FK_StitchingJobs_Swatches   FOREIGN KEY (SwatchId)    REFERENCES dbo.Swatches (SwatchId),
        CONSTRAINT CK_StitchingJobs_Stage CHECK (Stage IN (N'Measuring', N'Cutting', N'Stitching', N'Ready', N'Done'))
    );
    CREATE INDEX IX_StitchingJobs_Stage   ON dbo.StitchingJobs (Stage) INCLUDE (DueDate);
    CREATE INDEX IX_StitchingJobs_OrderId ON dbo.StitchingJobs (OrderId);
END
GO

/* ═══════════════════════════════════════════════════════════════════════════
   Inventory audit — every stock movement is attributable
   ═══════════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.InventoryAdjustments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryAdjustments (
        InventoryAdjustmentId BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryAdjustments PRIMARY KEY,
        ProductId             INT           NOT NULL,
        Delta                 INT           NOT NULL,
        ResultingStock        INT           NOT NULL,
        Reason                NVARCHAR(200) NOT NULL,
        OrderId               INT           NULL,     -- set when the movement was a sale
        ActorUserId           NVARCHAR(128) NULL,     -- NULL for system/checkout movements
        CreatedAt             DATETIME2(3)  NOT NULL CONSTRAINT DF_InventoryAdjustments_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_InventoryAdjustments_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
        CONSTRAINT FK_InventoryAdjustments_Orders   FOREIGN KEY (OrderId)   REFERENCES dbo.Orders (OrderId),
        CONSTRAINT CK_InventoryAdjustments_Delta CHECK (Delta <> 0)
    );
    CREATE INDEX IX_InventoryAdjustments_ProductId ON dbo.InventoryAdjustments (ProductId, CreatedAt DESC);
END
GO

/* ═══════════════════════════════════════════════════════════════════════════
   Shop settings — exactly one row, pinned by a CHECK constraint
   ═══════════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.ShopSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShopSettings (
        ShopSettingsId         TINYINT        NOT NULL CONSTRAINT PK_ShopSettings PRIMARY KEY,
        ShopName               NVARCHAR(128)  NOT NULL,
        City                   NVARCHAR(64)   NOT NULL,
        AddressLine            NVARCHAR(256)  NOT NULL,
        WhatsAppNumber         NVARCHAR(20)   NOT NULL,
        BannerText             NVARCHAR(400)  NULL,
        OpeningHours           NVARCHAR(200)  NULL,
        DeliveryCharge         DECIMAL(12,2)  NOT NULL CONSTRAINT DF_ShopSettings_DeliveryCharge DEFAULT (300),
        FreeDeliveryThreshold  DECIMAL(12,2)  NOT NULL CONSTRAINT DF_ShopSettings_FreeDeliveryThreshold DEFAULT (5000),
        CashOnDeliveryEnabled  BIT            NOT NULL CONSTRAINT DF_ShopSettings_Cod DEFAULT (1),
        WhatsAppOrdersEnabled  BIT            NOT NULL CONSTRAINT DF_ShopSettings_Wa  DEFAULT (1),
        ReserveInShopEnabled   BIT            NOT NULL CONSTRAINT DF_ShopSettings_Res DEFAULT (1),
        CardPaymentEnabled     BIT            NOT NULL CONSTRAINT DF_ShopSettings_Card DEFAULT (0),
        UpdatedAt              DATETIME2(3)   NULL,
        UpdatedByUserId        NVARCHAR(128)  NULL,
        -- One row, forever. Id must be 1.
        CONSTRAINT CK_ShopSettings_Singleton CHECK (ShopSettingsId = 1),
        CONSTRAINT CK_ShopSettings_Money CHECK (DeliveryCharge >= 0 AND FreeDeliveryThreshold >= 0)
    );
END
GO

/*  Sequence behind the SS-#### order reference. Starting at 2419 continues
    the numbering the shop is already using in the design.                    */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'OrderReferenceSequence')
    CREATE SEQUENCE dbo.OrderReferenceSequence AS INT START WITH 2419 INCREMENT BY 1;
GO

/*  Internal error log. Procedures write here in CATCH; the caller is told
    only 'something went wrong' so no internals leak to a browser.            */
IF OBJECT_ID(N'dbo.ErrorLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ErrorLog (
        ErrorLogId    BIGINT         IDENTITY(1,1) NOT NULL CONSTRAINT PK_ErrorLog PRIMARY KEY,
        ProcedureName NVARCHAR(256)  NULL,
        ErrorNumber   INT            NULL,
        ErrorMessage  NVARCHAR(MAX)  NULL,
        ErrorLine     INT            NULL,
        ErrorSeverity INT            NULL,
        OccurredAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_ErrorLog_OccurredAt DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_ErrorLog_OccurredAt ON dbo.ErrorLog (OccurredAt DESC);
END
GO
