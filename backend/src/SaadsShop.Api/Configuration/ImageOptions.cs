namespace SaadsShop.Api.Configuration;

/// <summary>
/// Where product photographs are kept, and what will be accepted as one.
/// </summary>
/// <remarks>
/// Photos live on the API server's disk and are served back as static files.
/// For one shop on one machine that is the right answer: no second service to
/// run, no bill, and a backup of the folder is a backup of the photos.
///
/// It has one operational requirement, and it is not optional: <see cref="RootPath"/>
/// must be a mounted volume. A container's own filesystem is discarded on
/// redeploy, and with it every photograph the shop has taken. See the deployment
/// note in docs/architecture.md.
/// </remarks>
public sealed class ImageOptions
{
    public const string SectionName = "Images";

    /// <summary>Where the files are written. Relative paths resolve from the content root.</summary>
    public string RootPath { get; init; } = "uploads";

    /// <summary>The URL prefix those files are served under.</summary>
    public string RequestPath { get; init; } = "/media";

    /// <summary>
    /// The largest upload accepted, in bytes. A phone photo is 3–8 MB; this is
    /// generous enough for one and small enough that a hostile caller cannot
    /// tie up the server with it.
    /// </summary>
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>The longest edge of the stored photograph.</summary>
    public int MaxEdgePixels { get; init; } = 1600;

    /// <summary>The longest edge of the card thumbnail.</summary>
    public int ThumbnailEdgePixels { get; init; } = 400;

    /// <summary>
    /// The largest image, in megapixels, that will be decoded at all.
    /// </summary>
    /// <remarks>
    /// A decompression bomb is a small file that declares enormous dimensions —
    /// a 40,000 × 40,000 PNG compresses to a few kilobytes and expands to about
    /// 6 GB in memory. <see cref="MaxUploadBytes"/> does not catch it, because
    /// the file really is small. The dimensions are read from the header before
    /// any pixels are decoded, and anything past this is refused.
    /// </remarks>
    public int MaxMegapixels { get; init; } = 50;

    /// <summary>Quality for the stored WebP, 1–100.</summary>
    public int WebpQuality { get; init; } = 82;
}
