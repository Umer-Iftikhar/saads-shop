using SaadsShop.Api.DTOs.Internal;

namespace SaadsShop.Api.Services.Interfaces;

/// <summary>Storing a product photograph, and taking one away.</summary>
public interface IProductImageService
{
    /// <summary>
    /// Validates an upload and writes it as two WebP files — one for the product
    /// page, one for the card.
    /// </summary>
    /// <remarks>
    /// Failures come back as <see cref="ResponseCodes.ValidationFailed"/> with a
    /// message meant for the person who chose the file, because every one of
    /// them is something they can fix by choosing a different one.
    /// </remarks>
    Task<OperationResult<StoredImage>> SaveAsync(
        Stream content, string fileName, long length, CancellationToken ct = default);

    /// <summary>Removes the files behind a stored path. Best effort.</summary>
    void TryDelete(string? imagePath, string? thumbnailPath);

    /// <summary>The upload folder, as an absolute path.</summary>
    string ResolvedRoot { get; }
}

/// <summary>Where a saved photograph ended up, and how big it is.</summary>
public sealed class StoredImage
{
    public required string ImagePath     { get; init; }
    public required string ThumbnailPath { get; init; }
    public int             Width         { get; init; }
    public int             Height        { get; init; }
}
