using SkiaSharp;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// Makes a slide photo small enough to send from a conference hall: the longest
/// edge at most <see cref="LongestEdge"/> pixels, re-encoded as JPEG, every EXIF
/// field dropped but the orientation.
/// <para>
/// A twelve-megapixel photo is four or five megabytes and says nothing more
/// about a slide than a 1600-pixel one does; the uploads it would cost are the
/// ones a hall's shared Wi-Fi drops. The person can keep a note's pictures as
/// taken with its "send originals" switch, which skips this entirely.
/// </para>
/// </summary>
public static class PictureDownscaler
{
    /// <summary>The longest edge a downscaled picture has.</summary>
    public const int LongestEdge = 1600;

    /// <summary>The JPEG quality it is written at.</summary>
    public const int JpegQuality = 85;

    /// <summary>The type every downscaled picture has.</summary>
    public const string ContentType = "image/jpeg";

    private static readonly string[] Downscalable = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    /// <summary>Whether a picture of this type is downscaled. A GIF is not: it may
    /// be animated, and a JPEG of its first frame is not the same picture.</summary>
    public static bool Handles(string contentType) =>
        Downscalable.Contains(contentType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The downscaled JPEG, or null when the bytes are not a picture this device
    /// can decode — a HEIC on a platform without the codec, say — in which case
    /// the caller sends the original.
    /// </summary>
    public static byte[]? Downscale(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var data = SKData.Create(source);
        if (data is null) return null;

        using var codec = SKCodec.Create(data);
        if (codec is null) return null;

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width <= 0 || height <= 0) return null;

        var scale = Math.Min(1.0, LongestEdge / (double)Math.Max(width, height));
        var target = new SKSizeI(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));

        using var decoded = Decode(codec, target);
        if (decoded is null) return null;

        // Onto white, because a JPEG has no alpha: a transparent PNG screenshot
        // would otherwise come out with a black background.
        using var surface = SKSurface.Create(new SKImageInfo(target.Width, target.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using (var image = SKImage.FromBitmap(decoded))
        {
            canvas.DrawImage(image, SKRect.Create(target.Width, target.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        if (jpeg is null) return null;

        // The pixels stay as the sensor read them, so the tag that says how to
        // turn them has to travel with them.
        return ExifOrientation.Stamp(jpeg.ToArray(), (ushort)codec.EncodedOrigin);
    }

    /// <summary>
    /// Decodes at the smallest size the codec can produce that is still at least
    /// <paramref name="target"/> — a JPEG decoder can skip most of the work of a
    /// twelve-megapixel photo — and at full size otherwise.
    /// </summary>
    private static SKBitmap? Decode(SKCodec codec, SKSizeI target)
    {
        var info = codec.Info;
        var scaled = codec.GetScaledDimensions(target.Width / (float)info.Width);

        var size = scaled.Width >= target.Width && scaled.Height >= target.Height ? scaled : info.Size;
        var decodeInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        var bitmap = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());

        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput) return bitmap;

        bitmap.Dispose();
        return null;
    }
}
