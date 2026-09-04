using System.ComponentModel.DataAnnotations;

namespace SaadsShop.Api.DTOs.Request;

/// <summary>Storefront and admin product listing.</summary>
public sealed class ProductListQuery
{
    [StringLength(64)]
    public string? Category { get; init; }

    [StringLength(128, ErrorMessage = "Search text must be 128 characters or fewer.")]
    public string? Search { get; init; }

    /// <summary>
    /// Compared against a closed list rather than interpolated into SQL. The
    /// stored procedure rejects anything unknown as well.
    /// </summary>
    [RegularExpression("^(Featured|PriceAsc|PriceDesc|Newest|Name)$", ErrorMessage = "Unknown sort order.")]
    public string SortBy { get; init; } = "Featured";

    [Range(1, 10_000, ErrorMessage = "Page must be 1 or more.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    public int PageSize { get; init; } = 24;
}

/// <summary>Create/update payload from the product editor.</summary>
public sealed class ProductEditorRequest
{
    [Required(ErrorMessage = "Product name is required.")]
    [StringLength(128, MinimumLength = 2, ErrorMessage = "Product name must be between 2 and 128 characters.")]
    public string Name { get; init; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Pick a category.")]
    public int CategoryId { get; init; }

    [Range(0, 10_000_000, ErrorMessage = "Price must be between Rs 0 and Rs 10,000,000.")]
    public decimal Price { get; init; }

    [StringLength(48)]   public string? Kicker          { get; init; }
    [StringLength(280)]  public string? Blurb           { get; init; }
    [StringLength(2000)] public string? LongDescription { get; init; }
    [StringLength(48)]   public string? Pieces          { get; init; }

    [Range(0, 90, ErrorMessage = "Stitching days must be between 0 and 90.")]
    public int StitchingDays { get; init; } = 3;

    [Range(0, 100_000, ErrorMessage = "Stock must be between 0 and 100,000.")]
    public int Stock { get; init; }

    [Range(0, 100_000, ErrorMessage = "The low-stock threshold must be between 0 and 100,000.")]
    public int LowStockAt { get; init; } = 6;

    public int? DefaultSwatchId { get; init; }

    /// <summary>
    /// The complete set of cloths for this product — the editor sends what it
    /// wants, not a diff, and the procedure replaces the set wholesale.
    /// </summary>
    [MaxLength(50, ErrorMessage = "A product can carry at most 50 cloths.")]
    public IReadOnlyList<int> SwatchIds { get; init; } = [];

    public bool IsActive { get; init; } = true;
}
