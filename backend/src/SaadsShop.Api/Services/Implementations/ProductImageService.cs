using Microsoft.Extensions.Options;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace SaadsShop.Api.Services.Implementations;

/// <summary>
/// Takes a photograph off a shopkeeper's phone and turns it into two files the
/// storefront can serve.
/// </summary>
/// <remarks>
/// Everything a caller sends here is untrusted, and an image decoder is a large
/// attack surface, so the checks run in order of cost: the cheap ones that need
/// no decoding first, and only what survives them is handed to ImageSharp.
///
///   1. Extension against a closed list.
///   2. The file's own first bytes — its signature — against the same list.
///   3. Size, already capped by the form limit, checked again here.
///   4. Dimensions, read from the header, before a single pixel is decoded.
///
/// Step 2 is the one that matters: an extension is a claim the uploader makes,
/// and Content-Type is a claim too. Only the bytes are evidence.
/// </remarks>
public sealed class ProductImageService(
    IOptions<ImageOptions> options,
    ILogger<ProductImageService> logger) : IProductImageService
{
    private readonly ImageOptions _options = options.Value;

    /// <summary>
    /// What the shop accepts, by extension and by the bytes a file of that kind
    /// actually starts with.
    /// </summary>
    /// <remarks>
    /// No library: a JPEG starts FF D8 FF, a PNG starts with the eight-byte PNG
    /// signature, and a WebP is a RIFF container with "WEBP" at offset 8. That
    /// is the whole of it, and three lines of comparison is a smaller thing to
    /// trust than a MIME-sniffing dependency.
    ///
    /// GIF and BMP are left out deliberately rather than forgotten: an animated
    /// GIF is not a product photograph, and a BMP is megabytes of nothing.
    /// </remarks>
    private static readonly (string Extension, byte[] Signature, int Offset)[] Accepted =
    [
        (".jpg",  [0xFF, 0xD8, 0xFF], 0),
        (".jpeg", [0xFF, 0xD8, 0xFF], 0),
        (".png",  [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0),
        (".webp", "WEBP"u8.ToArray(), 8),
    ];

    public static IReadOnlyList<string> AcceptedExtensions { get; } =
        [.. Accepted.Select(a => a.Extension).Distinct()];

    public async Task<OperationResult<StoredImage>> SaveAsync(
        Stream content, string fileName, long length, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        if (!Accepted.Any(a => a.Extension == extension))
        {
            return OperationResult<StoredImage>.Failure(
                ResponseCodes.ValidationFailed,
                $"That file type is not accepted. Please use {Readable(AcceptedExtensions)}.");
        }

        if (length <= 0)
            return Invalid("That file is empty.");

        if (length > _options.MaxUploadBytes)
        {
            return Invalid(
                $"That photo is {length / (1024 * 1024)} MB. Please use one under " +
                $"{_options.MaxUploadBytes / (1024 * 1024)} MB.");
        }

        //  Copy to memory once. The file is capped at a few MB, and the checks
        //  below plus the decode all need to read from the start — a request
        //  body stream cannot be rewound.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (buffer.Length > _options.MaxUploadBytes)
            return Invalid("That photo is larger than the shop accepts.");

        if (!SignatureMatches(buffer, extension))
        {
            //  Renaming a file does not change what it is. This is the check
            //  that stops a .exe arriving as a .jpg.
            logger.LogWarning("Rejected an upload named {FileName}: its bytes are not a {Extension}",
                fileName, extension);

            return Invalid("That file is not the kind of image it claims to be.");
        }

        buffer.Position = 0;

        try
        {
            //  Dimensions from the header, before decoding. A 40,000-square PNG
            //  is a few kilobytes on disk and gigabytes in memory.
            var info = Image.Identify(buffer);

            if (info is null)
                return Invalid("That image could not be read.");

            var megapixels = (long)info.Width * info.Height / 1_000_000d;

            if (megapixels > _options.MaxMegapixels)
            {
                logger.LogWarning("Rejected a {Width}x{Height} image ({Megapixels:F0}MP)",
                    info.Width, info.Height, megapixels);

                return Invalid($"That image is {info.Width}×{info.Height}, which is too large to process.");
            }

            buffer.Position = 0;
            return await WriteAsync(buffer, ct);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return Invalid("That image could not be read. It may be damaged.");
        }
    }

    private async Task<OperationResult<StoredImage>> WriteAsync(Stream buffer, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(buffer, ct);

        //  Everything the camera wrote goes: a phone photograph carries the GPS
        //  coordinates it was taken at, and the shop's own address is one thing,
        //  a shopkeeper's home is another.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile  = null;

        //  A name nobody can guess and nothing can collide with. Not the
        //  uploader's filename: that is their text, and it would arrive in a URL.
        var stem   = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(ResolvedRoot, DateTime.UtcNow.ToString("yyyy-MM"));
        Directory.CreateDirectory(folder);

        var fullName  = $"{stem}.webp";
        var thumbName = $"{stem}-thumb.webp";
        var encoder   = new WebpEncoder { Quality = _options.WebpQuality };

        ShrinkToFit(image, _options.MaxEdgePixels);
        await image.SaveAsync(Path.Combine(folder, fullName), encoder, ct);

        var width  = image.Width;
        var height = image.Height;

        ShrinkToFit(image, _options.ThumbnailEdgePixels);
        await image.SaveAsync(Path.Combine(folder, thumbName), encoder, ct);

        var month = DateTime.UtcNow.ToString("yyyy-MM");

        return OperationResult<StoredImage>.Success(new StoredImage
        {
            ImagePath     = $"{_options.RequestPath}/{month}/{fullName}",
            ThumbnailPath = $"{_options.RequestPath}/{month}/{thumbName}",
            Width         = width,
            Height        = height,
        }, "Photo saved.");
    }

    /// <summary>
    /// Resizes so the longest edge is at most <paramref name="edge"/>, and never
    /// the other way.
    /// </summary>
    /// <remarks>
    /// ResizeMode.Max scales *up* as well as down, so a 300px photograph asked
    /// to fit 1600 comes back a blurry 1600 — a bigger file that looks worse.
    /// Anything already inside the box is left exactly as it is.
    ///
    /// Max rather than Crop throughout: which part of the cloth is in frame is
    /// the shop's decision, not the server's.
    /// </remarks>
    private static void ShrinkToFit(Image image, int edge)
    {
        if (image.Width <= edge && image.Height <= edge) return;

        image.Mutate(c => c.Resize(new ResizeOptions
        {
            Size = new Size(edge, edge),
            Mode = ResizeMode.Max,
        }));
    }

    /// <summary>
    /// Removes the files behind a stored path.
    /// </summary>
    /// <remarks>
    /// Best effort on purpose. The product's record of its photo is the database
    /// row; a file left behind is wasted disk, while a failure here that stopped
    /// the row being cleared would leave the panel showing a photo the shop has
    /// asked to remove.
    /// </remarks>
    public void TryDelete(string? imagePath, string? thumbnailPath)
    {
        foreach (var path in new[] { imagePath, thumbnailPath })
        {
            var resolved = ResolveStoredPath(path);
            if (resolved is null) continue;

            try
            {
                if (File.Exists(resolved)) File.Delete(resolved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove {Path}; the row was cleared anyway", resolved);
            }
        }
    }

    /// <summary>The upload folder, as an absolute path.</summary>
    public string ResolvedRoot => Path.IsPathRooted(_options.RootPath)
        ? _options.RootPath
        : Path.Combine(AppContext.BaseDirectory, _options.RootPath);

    /// <summary>
    /// Turns a stored URL path back into a file path, refusing anything that
    /// does not sit inside the upload folder.
    /// </summary>
    /// <remarks>
    /// These values come from the database rather than from a request, so this
    /// is defence in depth — but "../../appsettings.json" is exactly the sort of
    /// thing a path built from data should not be able to express, and checking
    /// costs a comparison.
    /// </remarks>
    private string? ResolveStoredPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (!storedPath.StartsWith(_options.RequestPath, StringComparison.Ordinal)) return null;

        var relative = storedPath[_options.RequestPath.Length..].TrimStart('/');
        var root     = Path.GetFullPath(ResolvedRoot);
        var full     = Path.GetFullPath(Path.Combine(root, relative));

        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private static bool SignatureMatches(Stream stream, string extension)
    {
        foreach (var (candidate, signature, offset) in Accepted)
        {
            if (candidate != extension) continue;

            var header = new byte[offset + signature.Length];
            stream.Position = 0;

            if (stream.Read(header, 0, header.Length) < header.Length) return false;

            if (header.AsSpan(offset, signature.Length).SequenceEqual(signature)) return true;
        }

        return false;
    }

    private static OperationResult<StoredImage> Invalid(string message)
        => OperationResult<StoredImage>.Failure(ResponseCodes.ValidationFailed, message);

    /// <summary>"JPG, JPEG, PNG or WEBP" — for a message a shopkeeper reads.</summary>
    private static string Readable(IReadOnlyList<string> extensions)
    {
        var names = extensions.Select(e => e.TrimStart('.').ToUpperInvariant()).ToList();
        return names.Count <= 1
            ? names.FirstOrDefault() ?? string.Empty
            : string.Join(", ", names[..^1]) + " or " + names[^1];
    }
}
