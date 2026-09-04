/*  Saad's Shop — reference data
    ------------------------------------------------------------------------
    Required for the application to function at all. Idempotent: every insert
    is guarded, so re-applying never duplicates or overwrites live edits.
*/

SET NOCOUNT ON;
GO

/* ── Roles ──────────────────────────────────────────────────────────────
   Two, and only two. Staff run the shop day to day; Owner additionally
   controls settings, staff accounts and product deletion.                 */
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NormalizedName = N'OWNER')
    INSERT INTO dbo.Roles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (N'11111111-1111-1111-1111-111111111111', N'Owner', N'OWNER', NEWID());

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NormalizedName = N'STAFF')
    INSERT INTO dbo.Roles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (N'22222222-2222-2222-2222-222222222222', N'Staff', N'STAFF', NEWID());
GO

/* ── Categories ─────────────────────────────────────────────────────────
   Wedding sets lead — that was the explicit ask, and the storefront hero
   depends on the ordering here.                                           */
MERGE dbo.Categories AS t
USING (VALUES
    (N'Wedding sets', N'wedding-sets', 1),
    (N'Bed sheets',   N'bed-sheets',   2),
    (N'Curtains',     N'curtains',     3),
    (N'Cushions',     N'cushions',     4),
    (N'Umbrellas',    N'umbrellas',    5)
) AS s (Name, Slug, SortOrder)
   ON t.Slug = s.Slug
WHEN NOT MATCHED THEN
    INSERT (Name, Slug, SortOrder, IsActive) VALUES (s.Name, s.Slug, s.SortOrder, 1);
GO

/* ── The shop's cloth palette ───────────────────────────────────────────
   Six colours, exactly as authored in the design. Gold and Plum are OKLCH
   in the original and stay that way — converting them to hex would shift
   them, and every modern browser reads oklch() natively.                  */
MERGE dbo.Swatches AS t
USING (VALUES
    (N'Terracotta', N'#c67139',                 N'Woven', 1),
    (N'Sage',       N'#7a8a5e',                 N'Woven', 2),
    (N'Cream',      N'#ebddc5',                 N'Woven', 3),
    (N'Clay',       N'#8c491a',                 N'Woven', 4),
    (N'Gold',       N'oklch(0.78 0.11 85)',     N'Woven', 5),
    (N'Plum',       N'oklch(0.46 0.09 350)',    N'Woven', 6)
) AS s (Name, ColorValue, Weave, SortOrder)
   ON t.Name = s.Name
WHEN NOT MATCHED THEN
    INSERT (Name, ColorValue, Weave, SortOrder, IsActive)
    VALUES (s.Name, s.ColorValue, s.Weave, s.SortOrder, 1);
GO

/* ── Bed sizes ──────────────────────────────────────────────────────────
   The adjustments come straight from the set builder: Single is Rs 1,200
   less cloth than Double, King is Rs 2,600 more.                          */
MERGE dbo.BedSizes AS t
USING (VALUES
    (N'Single', N'Single', CAST(-1200 AS DECIMAL(12,2)), 1),
    (N'Double', N'Double', CAST(     0 AS DECIMAL(12,2)), 2),
    (N'King',   N'King',   CAST(  2600 AS DECIMAL(12,2)), 3)
) AS s (BedSizeCode, Name, PriceAdjustment, SortOrder)
   ON t.BedSizeCode = s.BedSizeCode
WHEN NOT MATCHED THEN
    INSERT (BedSizeCode, Name, PriceAdjustment, SortOrder)
    VALUES (s.BedSizeCode, s.Name, s.PriceAdjustment, s.SortOrder);
GO

/* ── Shop settings — the single row ─────────────────────────────────────
   Card payment is off: the shop does not take cards, and the Settings
   screen says so honestly rather than pretending.                         */
IF NOT EXISTS (SELECT 1 FROM dbo.ShopSettings WHERE ShopSettingsId = 1)
    INSERT INTO dbo.ShopSettings
        (ShopSettingsId, ShopName, City, AddressLine, WhatsAppNumber, BannerText, OpeningHours,
         DeliveryCharge, FreeDeliveryThreshold,
         CashOnDeliveryEnabled, WhatsAppOrdersEnabled, ReserveInShopEnabled, CardPaymentEnabled)
    VALUES
        (1, N'Saad''s Shop', N'Rawalpindi', N'Shop 14, Moti Bazaar, Raja Bazaar', N'03000000000',
         N'Free delivery inside Rawalpindi on orders over Rs 5,000 · Shaadi orders stitched in 3 days',
         N'Mon–Sat · 10am – 9pm · Friday break 1pm – 2:30pm',
         300, 5000, 1, 1, 1, 0);
GO
