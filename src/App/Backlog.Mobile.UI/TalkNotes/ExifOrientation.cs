using System.Buffers.Binary;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// The one EXIF field a downscaled picture keeps: which way up it is.
/// <para>
/// A phone camera stores the pixels as the sensor read them and says in EXIF
/// how to turn them. Drop that and a portrait slide photo arrives on its side;
/// keep the rest and the GPS position, the device serial and the capture time
/// go with it. So the re-encoded JPEG gets a minimal APP1 segment holding the
/// orientation tag and nothing else.
/// </para>
/// </summary>
public static class ExifOrientation
{
    /// <summary>The TIFF tag number of Orientation.</summary>
    public const ushort Tag = 0x0112;

    /// <summary>Upright, which is also what no tag at all means.</summary>
    public const ushort Upright = 1;

    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;

    /// <summary>
    /// <paramref name="jpeg"/> with an APP1 segment carrying only
    /// <paramref name="orientation"/> — after the JFIF APP0 when there is one,
    /// otherwise straight after the start-of-image marker. An upright picture
    /// needs no tag and is returned as it is.
    /// </summary>
    public static byte[] Stamp(byte[] jpeg, ushort orientation)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (orientation is Upright or 0 or > 8) return jpeg;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return jpeg;

        var insertAt = 2;
        if (jpeg.Length >= 6 && jpeg[2] == 0xFF && jpeg[3] == 0xE0)
        {
            insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4, 2));
        }

        var segment = Segment(orientation);
        var stamped = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(0, insertAt).CopyTo(stamped);
        segment.CopyTo(stamped.AsSpan(insertAt));
        jpeg.AsSpan(insertAt).CopyTo(stamped.AsSpan(insertAt + segment.Length));

        return stamped;
    }

    private static byte[] Segment(ushort orientation)
    {
        // FF E1, length, "Exif\0\0", a big-endian TIFF header, one IFD with one
        // SHORT entry, and no next IFD.
        const int tiffLength = 8 + 2 + 12 + 4;
        var segment = new byte[4 + 6 + tiffLength];
        var span = segment.AsSpan();

        span[0] = 0xFF;
        span[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)(segment.Length - 2));
        ExifHeader.CopyTo(span[4..]);

        var tiff = span[10..];
        tiff[0] = (byte)'M';
        tiff[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(tiff[2..], 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[4..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[8..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[10..], Tag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[12..], 3);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[14..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff[18..], orientation);
        BinaryPrimitives.WriteUInt32BigEndian(tiff[22..], 0);

        return segment;
    }
}
