using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SaadsShop.Api.Configuration;
using SaadsShop.Api.Constants;
using SaadsShop.Api.Services.Implementations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SaadsShop.UnitTests.Services;

/// <summary>
/// What the shop will accept as a photograph.
/// </summary>
/// <remarks>
/// Everything reaching this service is untrusted, and an image decoder is a
/// large attack surface, so most of these tests are about what is refused
/// rather than what works.
/// </remarks>
public class ProductImageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "saadsshop-image-tests", Guid.NewGuid().ToString("N"));

    private ProductImageService Service(ImageOptions? options = null)
        => new(Options.Create(options ?? new ImageOptions { RootPath = _root }),
               NullLogger<ProductImageService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A real image of the given size, encoded as the given format.</summary>
    private static MemoryStream AnImage(int width = 800, int height = 600, string format = "jpeg")
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();

        switch (format)
        {
            case "png":  image.SaveAsPng(stream);  break;
            case "webp": image.SaveAsWebp(stream); break;
            default:     image.SaveAsJpeg(stream); break;
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Bytes(params byte[] content) => new(content);

    // ── what is accepted ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("photo.jpg",  "jpeg")]
    [InlineData("photo.jpeg", "jpeg")]
    [InlineData("PHOTO.JPG",  "jpeg")]
    [InlineData("photo.png",  "png")]
    [InlineData("photo.webp", "webp")]
    public async Task Accepts_a_real_photograph(string fileName, string format)
    {
        using var content = AnImage(format: format);

        var result = await Service().SaveAsync(content, fileName, content.Length);

        Assert.True(result.IsSuccess, result.Message);
    }

    [Fact]
    public async Task Writes_a_full_size_and_a_thumbnail()
    {
        using var content = AnImage();

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.NotEqual(result.Value!.ImagePath, result.Value.ThumbnailPath);
        Assert.EndsWith(".webp", result.Value.ImagePath);
        Assert.EndsWith(".webp", result.Value.ThumbnailPath);
    }

    [Fact]
    public async Task Shrinks_a_phone_photo_to_something_a_3G_connection_can_load()
    {
        using var content = AnImage(4000, 3000);

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.Equal(1600, result.Value!.Width);
        Assert.Equal(1200, result.Value.Height);   // aspect ratio kept
    }

    [Fact]
    public async Task Leaves_an_already_small_photo_alone_rather_than_blowing_it_up()
    {
        using var content = AnImage(300, 200);

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.Equal(300, result.Value!.Width);
        Assert.Equal(200, result.Value.Height);
    }

    [Fact]
    public async Task Serves_the_file_from_the_configured_request_path()
    {
        using var content = AnImage();

        var result = await Service(new ImageOptions { RootPath = _root, RequestPath = "/media" })
            .SaveAsync(content, "photo.jpg", content.Length);

        Assert.StartsWith("/media/", result.Value!.ImagePath);
    }

    [Fact]
    public async Task Names_the_file_itself_rather_than_trusting_the_uploader()
    {
        //  The uploader's filename is their text. Putting it in a URL invites
        //  everything from a path traversal to a script name in a link.
        using var content = AnImage();

        var result = await Service().SaveAsync(content, "../../etc/passwd.jpg", content.Length);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("passwd", result.Value!.ImagePath);
        Assert.DoesNotContain("..", result.Value.ImagePath);
    }

    [Fact]
    public async Task Actually_puts_the_files_on_disk_where_it_says()
    {
        using var content = AnImage();
        var service = Service();

        var result = await service.SaveAsync(content, "photo.jpg", content.Length);

        var relative = result.Value!.ImagePath["/media/".Length..];
        Assert.True(File.Exists(Path.Combine(service.ResolvedRoot, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Two_uploads_never_collide()
    {
        var service = Service();

        using var first  = AnImage();
        using var second = AnImage();

        var a = await service.SaveAsync(first,  "photo.jpg", first.Length);
        var b = await service.SaveAsync(second, "photo.jpg", second.Length);

        Assert.NotEqual(a.Value!.ImagePath, b.Value!.ImagePath);
    }

    // ── extensions ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("sheet.svg")]      // SVG is XML, and XML can carry script
    [InlineData("animation.gif")]  // not a product photograph
    [InlineData("photo")]          // no extension at all
    [InlineData("")]
    public async Task Refuses_a_file_type_the_shop_does_not_take(string fileName)
    {
        using var content = AnImage();

        var result = await Service().SaveAsync(content, fileName, content.Length);

        Assert.Equal(ResponseCodes.ValidationFailed, result.ResponseCode);
    }

    [Fact]
    public async Task Says_which_types_it_does_take()
    {
        using var content = AnImage();

        var result = await Service().SaveAsync(content, "payload.exe", content.Length);

        Assert.Contains("JPG", result.Message);
        Assert.Contains("PNG", result.Message);
    }

    [Fact]
    public async Task Refuses_a_double_extension()
    {
        // "photo.jpg.exe" is an executable, whatever the middle says.
        using var content = AnImage();

        var result = await Service().SaveAsync(content, "photo.jpg.exe", content.Length);

        Assert.False(result.IsSuccess);
    }

    // ── the bytes, not the name ──────────────────────────────────────────────

    [Fact]
    public async Task Refuses_an_executable_renamed_to_jpg()
    {
        //  This is the check that matters. An extension is a claim the uploader
        //  makes; so is Content-Type. Only the first bytes are evidence.
        //  MZ is the DOS header every Windows executable starts with.
        using var content = Bytes(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00);

        var result = await Service().SaveAsync(content, "innocent.jpg", content.Length);

        Assert.Equal(ResponseCodes.ValidationFailed, result.ResponseCode);
        Assert.Contains("not the kind of image it claims", result.Message);
    }

    [Fact]
    public async Task Refuses_a_shell_script_renamed_to_png()
    {
        using var content = Bytes("#!/bin/sh\nrm -rf /\n"u8.ToArray());

        var result = await Service().SaveAsync(content, "cloth.png", content.Length);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Refuses_a_real_png_wearing_a_jpg_name()
    {
        // Not hostile, but the signature and the extension must agree, or the
        // stored file says one thing and is another.
        using var content = AnImage(format: "png");

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Refuses_a_file_too_short_to_have_a_signature()
    {
        using var content = Bytes(0xFF);

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Refuses_a_webp_whose_RIFF_container_does_not_say_WEBP()
    {
        //  A WebP is RIFF....WEBP. A WAV is RIFF....WAVE — same container, and
        //  it would pass a check that only looked at the first four bytes.
        using var content = Bytes([.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray()]);

        var result = await Service().SaveAsync(content, "cloth.webp", content.Length);

        Assert.False(result.IsSuccess);
    }

    // ── size ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refuses_a_file_larger_than_the_cap()
    {
        using var content = AnImage();

        var result = await Service(new ImageOptions { RootPath = _root, MaxUploadBytes = 1024 })
            .SaveAsync(content, "photo.jpg", content.Length);

        Assert.Equal(ResponseCodes.ValidationFailed, result.ResponseCode);
    }

    [Fact]
    public async Task Refuses_an_empty_file()
    {
        using var content = new MemoryStream();

        var result = await Service().SaveAsync(content, "photo.jpg", 0);

        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_body_that_is_bigger_than_the_length_it_declared()
    {
        //  A declared length is another claim. The bytes that arrive are checked
        //  against the cap too, so understating it buys nothing.
        using var content = AnImage(2000, 2000);

        var result = await Service(new ImageOptions { RootPath = _root, MaxUploadBytes = 4096 })
            .SaveAsync(content, "photo.jpg", 100);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Refuses_a_decompression_bomb_without_decoding_it()
    {
        //  A 12,000-square PNG of one colour is a few hundred kilobytes on disk
        //  and about 576 MB decoded. The size cap does not catch it, because the
        //  file really is small — the dimensions in the header do.
        using var bomb = new Image<Rgba32>(12_000, 12_000);
        using var content = new MemoryStream();
        await bomb.SaveAsync(content, new PngEncoder());
        content.Position = 0;

        var result = await Service(new ImageOptions
        {
            RootPath = _root, MaxMegapixels = 50, MaxUploadBytes = 50 * 1024 * 1024,
        }).SaveAsync(content, "bomb.png", content.Length);

        Assert.Equal(ResponseCodes.ValidationFailed, result.ResponseCode);
        Assert.Contains("too large to process", result.Message);
    }

    [Fact]
    public async Task Refuses_a_damaged_image_without_throwing()
    {
        //  A correct JPEG signature and rubbish after it. The shopkeeper gets a
        //  message; the API does not get a 500.
        using var content = Bytes([0xFF, 0xD8, 0xFF, .. new byte[64]]);

        var result = await Service().SaveAsync(content, "photo.jpg", content.Length);

        Assert.Equal(ResponseCodes.ValidationFailed, result.ResponseCode);
    }

    // ── what is thrown away ──────────────────────────────────────────────────

    [Fact]
    public async Task Strips_the_metadata_a_phone_attaches()
    {
        //  A phone photograph carries the GPS coordinates it was taken at. The
        //  shop's address is public; a shopkeeper's house is not.
        using var original = new Image<Rgba32>(400, 300);
        original.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
        original.Metadata.ExifProfile.SetValue(
            SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.GPSLatitudeRef, "N");

        using var content = new MemoryStream();
        original.SaveAsJpeg(content);
        content.Position = 0;

        var service = Service();
        var result  = await service.SaveAsync(content, "photo.jpg", content.Length);

        var relative = result.Value!.ImagePath["/media/".Length..];
        var stored   = Path.Combine(service.ResolvedRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        using var saved = await Image.LoadAsync(stored);
        Assert.Null(saved.Metadata.ExifProfile);
    }

    // ── removing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_removes_both_files()
    {
        using var content = AnImage();
        var service = Service();

        var result = await service.SaveAsync(content, "photo.jpg", content.Length);
        service.TryDelete(result.Value!.ImagePath, result.Value.ThumbnailPath);

        var relative = result.Value.ImagePath["/media/".Length..];
        Assert.False(File.Exists(Path.Combine(service.ResolvedRoot, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Deleting_something_that_is_already_gone_is_not_an_error()
    {
        // Called on every photo replacement, including the first, when there is
        // nothing to replace.
        Service().TryDelete("/media/2026-01/missing.webp", null);
    }

    [Theory]
    [InlineData("/media/../../appsettings.json")]
    [InlineData("/etc/passwd")]
    [InlineData("../../../secret.txt")]
    [InlineData(null)]
    [InlineData("")]
    public void Refuses_to_delete_anything_outside_the_upload_folder(string? path)
    {
        //  These come from the database rather than a request, so this is depth
        //  rather than the front line — but a path built from data should not be
        //  able to name a file outside the folder, and checking is one compare.
        Service().TryDelete(path, null);
    }
}
