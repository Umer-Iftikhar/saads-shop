namespace SaadsShop.Api.DTOs.Response;

public sealed class CategoryResponse
{
    public int    CategoryId { get; init; }
    public string Name       { get; init; } = string.Empty;
    public string Slug       { get; init; } = string.Empty;
}

public sealed class SwatchResponse
{
    public int     SwatchId   { get; init; }
    public string  Name       { get; init; } = string.Empty;
    public string  ColorValue { get; init; } = string.Empty;
    public string  Weave      { get; init; } = "Woven";
    public string? ImagePath  { get; init; }
}

public sealed class BedSizeResponse
{
    public string  BedSizeCode     { get; init; } = string.Empty;
    public string  Name            { get; init; } = string.Empty;
    public decimal PriceAdjustment { get; init; }
}

/// <summary>What a product card needs, and nothing more.</summary>
public sealed class ProductSummaryResponse
{
    public int     ProductId        { get; init; }
    public string  Name             { get; init; } = string.Empty;
    public string  Slug             { get; init; } = string.Empty;
    public string  CategoryName     { get; init; } = string.Empty;
    public string? Kicker           { get; init; }
    public string? Blurb            { get; init; }
    public decimal Price            { get; init; }
    public string? Pieces           { get; init; }

    /// <summary>
    /// Whether the shop can sell it today. The exact count is withheld from the
    /// storefront: how many are left is the shop's business, and it stops the
    /// catalogue being scraped for stock levels.
    /// </summary>
    public bool    InStock          { get; init; }

    public int?    DefaultSwatchId  { get; init; }
    public string? SwatchColorValue { get; init; }
    public string? SwatchWeave      { get; init; }
}

public sealed class ProductDetailResponse
{
    public int     ProductId       { get; init; }
    public string  Name            { get; init; } = string.Empty;
    public string  Slug            { get; init; } = string.Empty;
    public string  CategoryName    { get; init; } = string.Empty;
    public string  CategorySlug    { get; init; } = string.Empty;
    public string? Kicker          { get; init; }
    public string? Blurb           { get; init; }
    public string? LongDescription { get; init; }
    public decimal Price           { get; init; }
    public string? Pieces          { get; init; }
    public int     StitchingDays   { get; init; }
    public bool    InStock         { get; init; }
    public int?    DefaultSwatchId { get; init; }

    public IReadOnlyList<SwatchResponse>         Swatches { get; init; } = [];
    public IReadOnlyList<ProductSummaryResponse> Related  { get; init; } = [];
}

/// <summary>An admin product row — includes the stock count the storefront never sees.</summary>
public sealed class ProductAdminResponse
{
    public int     ProductId        { get; init; }
    public string  Name             { get; init; } = string.Empty;
    public string  Slug             { get; init; } = string.Empty;
    public int     CategoryId       { get; init; }
    public string  CategoryName     { get; init; } = string.Empty;
    public string? Kicker           { get; init; }
    public string? Blurb            { get; init; }
    public decimal Price            { get; init; }
    public string? Pieces           { get; init; }
    public int     StitchingDays    { get; init; }
    public int     Stock            { get; init; }
    public int     LowStockAt       { get; init; }
    public int     SoldCount        { get; init; }
    public bool    IsActive         { get; init; }
    public int?    DefaultSwatchId  { get; init; }
    public string? SwatchColorValue { get; init; }
    public string? SwatchWeave      { get; init; }
}

/// <summary>A page of rows plus what the client needs to render paging.</summary>
public sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Items      { get; init; } = [];
    public int              TotalCount { get; init; }
    public int              Page       { get; init; }
    public int              PageSize   { get; init; }
    public int              TotalPages { get; init; }
}

public sealed class ShopSettingsPublicResponse
{
    public string  ShopName              { get; init; } = string.Empty;
    public string  City                  { get; init; } = string.Empty;
    public string  AddressLine           { get; init; } = string.Empty;
    public string  WhatsAppNumber        { get; init; } = string.Empty;
    public string? BannerText            { get; init; }
    public string? OpeningHours          { get; init; }
    public decimal DeliveryCharge        { get; init; }
    public decimal FreeDeliveryThreshold { get; init; }

    /// <summary>Only the methods actually switched on, so the checkout cannot offer a dead one.</summary>
    public IReadOnlyList<string> PaymentMethods { get; init; } = [];
}

public sealed class ShopSettingsResponse
{
    public string    ShopName              { get; init; } = string.Empty;
    public string    City                  { get; init; } = string.Empty;
    public string    AddressLine           { get; init; } = string.Empty;
    public string    WhatsAppNumber        { get; init; } = string.Empty;
    public string?   BannerText            { get; init; }
    public string?   OpeningHours          { get; init; }
    public decimal   DeliveryCharge        { get; init; }
    public decimal   FreeDeliveryThreshold { get; init; }
    public bool      CashOnDeliveryEnabled { get; init; }
    public bool      WhatsAppOrdersEnabled { get; init; }
    public bool      ReserveInShopEnabled  { get; init; }
    public bool      CardPaymentEnabled    { get; init; }
    public DateTime? UpdatedAt             { get; init; }
    public string?   UpdatedBy             { get; init; }
}
