using System.Buffers.Binary;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Reads back what <c>ExifOrientation.Stamp</c> wrote, so a test can prove the
/// orientation survived a downscale and nothing else did. The app only writes
/// EXIF; reading it is the tests' business, so the reader lives here.
/// </summary>
internal static class ExifTags
{
    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;

    /// <summary>
    /// The IFD0 tags of the first EXIF APP1 segment in <paramref name="jpeg"/>,
    /// each with its SHORT value. Empty when there is none.
    /// </summary>
    public static IReadOnlyDictionary<ushort, ushort> Read(ReadOnlySpan<byte> jpeg)
    {
        var tags = new Dictionary<ushort, ushort>();
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return tags;

        var position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            var marker = jpeg[position + 1];
            if (marker is 0xDA or 0xD9) break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(position + 2, 2));
            if (position + 2 + length > jpeg.Length) break;

            var body = jpeg.Slice(position + 4, length - 2);
            if (marker == 0xE1 && body.StartsWith(ExifHeader))
            {
                ReadIfd0(body[ExifHeader.Length..], tags);
                return tags;
            }

            position += 2 + length;
        }

        return tags;
    }

    private static void ReadIfd0(ReadOnlySpan<byte> tiff, Dictionary<ushort, ushort> tags)
    {
        if (tiff.Length < 8) return;

        var bigEndian = tiff[0] == (byte)'M';
        ushort U16(ReadOnlySpan<byte> at) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(at) : BinaryPrimitives.ReadUInt16LittleEndian(at);
        uint U32(ReadOnlySpan<byte> at) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(at) : BinaryPrimitives.ReadUInt32LittleEndian(at);

        var ifd = (int)U32(tiff[4..]);
        if (ifd + 2 > tiff.Length) return;

        var count = U16(tiff[ifd..]);
        for (var index = 0; index < count; index++)
        {
            var entry = ifd + 2 + (index * 12);
            if (entry + 12 > tiff.Length) return;

            tags[U16(tiff[entry..])] = U16(tiff[(entry + 8)..]);
        }
    }
}
