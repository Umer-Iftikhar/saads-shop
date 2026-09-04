namespace SaadsShop.Api.Models;

/*  POCO models. No data annotations, no EF attributes, no framework types —
    Dapper materialises these from stored-procedure result sets by name, and
    nothing else in the system needs them to carry metadata.

    Property names match the columns the procedures SELECT, which is what lets
    Dapper map with no configuration at all.                                 */

public class Category
{
    public int    CategoryId { get; set; }
    public string Name       { get; set; } = string.Empty;
    public string Slug       { get; set; } = string.Empty;
    public int    SortOrder  { get; set; }
}

/// <summary>One cloth in the shop's palette.</summary>
public class Swatch
{
    public int     SwatchId   { get; set; }
    public string  Name       { get; set; } = string.Empty;

    /// <summary>
    /// A CSS colour as authored in the design — hex, or oklch(...) for Gold and
    /// Plum. Stored verbatim: converting the OKLCH values to hex would shift
    /// them, and browsers read oklch() natively.
    /// </summary>
    public string  ColorValue { get; set; } = string.Empty;

    public string  Weave      { get; set; } = "Woven";

    /// <summary>Set when a real photograph replaces the drawn gradient.</summary>
    public string? ImagePath  { get; set; }

    public int     SortOrder  { get; set; }
}

public class BedSize
{
    public string  BedSizeCode     { get; set; } = string.Empty;
    public string  Name            { get; set; } = string.Empty;

    /// <summary>Added to the bedding price. Single is negative — less cloth.</summary>
    public decimal PriceAdjustment { get; set; }

    public int     SortOrder       { get; set; }
}

public class Product
{
    public int      ProductId       { get; set; }
    public string   Name            { get; set; } = string.Empty;
    public string   Slug            { get; set; } = string.Empty;
    public int      CategoryId      { get; set; }
    public string?  CategoryName    { get; set; }
    public string?  CategorySlug    { get; set; }

    /// <summary>The small uppercase label on the card — "Bridal bedding", "Parde".</summary>
    public string?  Kicker          { get; set; }

    /// <summary>One line for the card.</summary>
    public string?  Blurb           { get; set; }

    /// <summary>The fuller description on the product page.</summary>
    public string?  LongDescription { get; set; }

    public decimal  Price           { get; set; }

    /// <summary>Free text as the shop says it: "12 pieces", "Pair", "One".</summary>
    public string?  Pieces          { get; set; }

    public int      StitchingDays   { get; set; }
    public int      Stock           { get; set; }
    public int      LowStockAt      { get; set; }
    public int?     DefaultSwatchId { get; set; }
    public int      SoldCount       { get; set; }
    public bool     IsActive        { get; set; }

    // Flattened from the default swatch so a listing needs only one result set.
    public string?  SwatchName       { get; set; }
    public string?  SwatchColorValue { get; set; }
    public string?  SwatchWeave      { get; set; }
}

/// <summary>A row of the Inventory screen.</summary>
public class InventoryItem
{
    public int     ProductId        { get; set; }
    public string  Name             { get; set; } = string.Empty;
    public string  CategoryName     { get; set; } = string.Empty;
    public decimal Price            { get; set; }
    public int     Stock            { get; set; }
    public int     LowStockAt       { get; set; }

    /// <summary>
    /// Decided by the database, not recomputed here, so the storefront, the
    /// panel and any future report agree on what "low" means.
    /// </summary>
    public string  StockLabel       { get; set; } = string.Empty;

    public int?    DefaultSwatchId  { get; set; }
    public string? SwatchColorValue { get; set; }
    public string? SwatchWeave      { get; set; }
    public bool    IsActive         { get; set; }
}
