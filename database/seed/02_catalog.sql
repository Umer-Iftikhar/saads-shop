/*  Saad's Shop — the catalogue from the design
    ------------------------------------------------------------------------
    Ten products, with the prices, piece counts, stock levels and cloth
    choices exactly as the prototype had them. This is real starting stock
    for the shop, not throwaway test data — the numbers were chosen to make
    the Inventory screen show a believable spread including two low lines
    (Jaali Sheer Panel at 4, Compact Chhata at 2).

    Idempotent: matched on product name, inserted only when missing.
*/

SET NOCOUNT ON;
GO

DECLARE @Products TABLE (
    Name NVARCHAR(128), CategorySlug NVARCHAR(64), Kicker NVARCHAR(48),
    Price DECIMAL(12,2), Pieces NVARCHAR(48), Stock INT, SoldCount INT,
    SwatchName NVARCHAR(48), Blurb NVARCHAR(280), LongDescription NVARCHAR(2000)
);

INSERT INTO @Products VALUES
(N'Gulaab Bridal Set', N'wedding-sets', N'Bridal bedding', 18500, N'12 pieces', 14, 38, N'Plum',
 N'Sheet, two covers, four cushions and a runner.',
 N'A twelve-piece bridal bedding set: double sheet, two pillow covers, four cushion covers, a bed runner and matched laces. Stitched in the shop.'),

(N'Chandni Room Package', N'wedding-sets', N'Full room', 34000, N'Bedding + parde', 6, 21, N'Sage',
 N'Bedding with the curtains cut to your windows.',
 N'The full room: bridal bedding plus two curtain panels stitched to your window measurements, in the same cloth family.'),

(N'Mehr Bridal Set', N'wedding-sets', N'Bridal bedding', 22000, N'14 pieces', 9, 17, N'Gold',
 N'Heavier cloth, gold thread work on the covers.',
 N'Fourteen pieces in a heavier weave with gold thread work along the covers and cushions.'),

(N'Jahez Six-Piece Bundle', N'wedding-sets', N'Jahez bundle', 9800, N'6 pieces', 22, 44, N'Terracotta',
 N'Two sheets, two covers, two cushions.',
 N'A starter jahez bundle: two double sheets, two pillow covers and two cushion covers.'),

(N'Sooti Double Bedsheet', N'bed-sheets', N'Bistar', 3200, N'3 pieces', 48, 96, N'Cream',
 N'Plain cotton, washes soft.',
 N'Plain cotton double sheet with two pillow covers.'),

(N'Block Print Sheet', N'bed-sheets', N'Bistar', 4100, N'3 pieces', 31, 52, N'Clay',
 N'Hand block print, king or double.',
 N'Hand block printed cotton with two matching covers.'),

(N'Velvet Drape Pair', N'curtains', N'Parde', 6900, N'Pair', 12, 29, N'Plum',
 N'Heavy velvet, blocks the afternoon sun.',
 N'A pair of heavy velvet drapes with a stitched header, hemmed to your drop.'),

(N'Jaali Sheer Panel', N'curtains', N'Parde', 2400, N'Single panel', 4, 33, N'Cream',
 N'Light net, layers under drapes.',
 N'A light jaali net panel that layers under heavier drapes.'),

(N'Barsaat Long Umbrella', N'umbrellas', N'Chhata', 1450, N'One', 26, 61, N'Sage',
 N'Wooden handle, full span.',
 N'Full-span monsoon umbrella with a wooden handle.'),

(N'Compact Chhata', N'umbrellas', N'Chhata', 950, N'One', 2, 40, N'Gold',
 N'Folds into a bag.',
 N'Folds down small enough for a handbag; two-fold frame, wind vented.');

INSERT INTO dbo.Products
    (Name, Slug, CategoryId, Kicker, Blurb, LongDescription, Price, Pieces,
     StitchingDays, Stock, LowStockAt, DefaultSwatchId, SoldCount, IsActive)
SELECT  p.Name,
        LOWER(REPLACE(REPLACE(p.Name, N'''', N''), N' ', N'-')),
        c.CategoryId,
        p.Kicker, p.Blurb, p.LongDescription, p.Price, p.Pieces,
        /*  Umbrellas are stock items, not stitched to order.               */
        CASE WHEN p.CategorySlug = N'umbrellas' THEN 0 ELSE 3 END,
        p.Stock, 6, s.SwatchId, p.SoldCount, 1
FROM    @Products AS p
JOIN    dbo.Categories AS c ON c.Slug = p.CategorySlug
JOIN    dbo.Swatches  AS s ON s.Name = p.SwatchName
WHERE   NOT EXISTS (SELECT 1 FROM dbo.Products AS x WHERE x.Name = p.Name);
GO

/*  Every product is offered in the full palette — the shop stocks the same
    six cloths and cuts whatever the customer picks. The product's own
    default swatch sorts first so the swatch strip opens on it.             */
INSERT INTO dbo.ProductSwatches (ProductId, SwatchId, SortOrder)
SELECT  p.ProductId, s.SwatchId,
        CASE WHEN s.SwatchId = p.DefaultSwatchId THEN 0 ELSE s.SortOrder END
FROM    dbo.Products AS p
CROSS JOIN dbo.Swatches AS s
WHERE   s.IsActive = 1
  AND   NOT EXISTS (SELECT 1 FROM dbo.ProductSwatches AS ps
                    WHERE ps.ProductId = p.ProductId AND ps.SwatchId = s.SwatchId);
GO

/*  Opening-stock audit rows, so InventoryAdjustments explains where the
    starting numbers came from rather than stock appearing from nowhere.    */
INSERT INTO dbo.InventoryAdjustments (ProductId, Delta, ResultingStock, Reason)
SELECT  p.ProductId, p.Stock, p.Stock, N'Opening stock'
FROM    dbo.Products AS p
WHERE   p.Stock > 0
  AND   NOT EXISTS (SELECT 1 FROM dbo.InventoryAdjustments AS a
                    WHERE a.ProductId = p.ProductId AND a.Reason = N'Opening stock');
GO
