using Backlog.Mobile.UI.TalkNotes;

using SkiaSharp;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The fixture is a 3200 × 2400 slide photo carrying what a phone writes into
/// EXIF: the orientation (6, turn 90° clockwise to view), the camera make, the
/// capture time and a GPS block. Downscaled, it is a 1600 × 1200 JPEG with the
/// orientation and nothing else.
/// </summary>
public sealed class PictureDownscalerTests
{
    private static byte[] Fixture => File.ReadAllBytes(TestFolders.Fixture("slide-photo.jpg"));

    [Fact]
    public void The_fixture_carries_a_phone_s_exif_to_begin_with()
    {
        var tags = ExifOrientation.ReadTags(Fixture);

        Assert.Equal(6, tags[ExifOrientation.Tag]);
        Assert.True(tags.Count > 1, "The fixture should carry more than the orientation, or the test proves nothing.");
    }

    [Fact]
    public void The_longest_edge_becomes_1600_and_the_aspect_holds()
    {
        using var source = new MemoryStream(Fixture);
        var output = PictureDownscaler.Downscale(source);

        Assert.NotNull(output);
        using var codec = SKCodec.Create(new MemoryStream(output));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(1600, codec.Info.Width);
        Assert.Equal(1200, codec.Info.Height);
        Assert.True(output.Length < Fixture.Length * 2, "A downscaled photo is not larger than it started by much.");
    }

    [Fact]
    public void Every_exif_field_but_the_orientation_is_gone()
    {
        using var source = new MemoryStream(Fixture);
        var output = PictureDownscaler.Downscale(source)!;

        var tags = ExifOrientation.ReadTags(output);
        Assert.Equal([ExifOrientation.Tag], tags.Keys);
        Assert.Equal(6, tags[ExifOrientation.Tag]);

        // A decoder reads it the same way the camera meant it.
        using var codec = SKCodec.Create(new MemoryStream(output));
        Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);

        // No trace of the camera or the GPS block in the bytes either.
        Assert.DoesNotContain("FixtureCam", System.Text.Encoding.ASCII.GetString(output), StringComparison.Ordinal);
    }

    [Fact]
    public void A_small_upright_png_is_re_encoded_as_jpeg_at_its_own_size_with_no_exif()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(400, 300, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var output = PictureDownscaler.Downscale(new MemoryStream(png.ToArray()))!;

        using var codec = SKCodec.Create(new MemoryStream(output));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal((400, 300), (codec.Info.Width, codec.Info.Height));
        Assert.Empty(ExifOrientation.ReadTags(output));
    }

    [Fact]
    public void Bytes_that_are_not_a_picture_are_left_to_go_as_they_are()
    {
        var pdf = File.ReadAllBytes(TestFolders.Fixture("handout.pdf"));

        Assert.Null(PictureDownscaler.Downscale(new MemoryStream(pdf)));
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/PNG", true)]
    [InlineData("image/heic", true)]
    [InlineData("image/gif", false)]
    [InlineData("application/pdf", false)]
    public void Which_types_are_downscaled(string contentType, bool handled)
    {
        Assert.Equal(handled, PictureDownscaler.Handles(contentType));
    }
}
