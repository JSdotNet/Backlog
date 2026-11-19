using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>An AAC track lifted out of a recording, whatever framed it: where each frame's
/// raw bytes lie in the source, how long each lasts, and the decoder config they need.</summary>
/// <param name="AudioSpecificConfig">The MPEG-4 AudioSpecificConfig, written into the new
/// file's esds as it is.</param>
/// <param name="Timescale">The units <paramref name="Durations"/> count in.</param>
/// <param name="Frames">Each frame's bytes, by position in the source.</param>
/// <param name="Durations">Each frame's length, one per frame.</param>
internal sealed record AacTrack(
    byte[] AudioSpecificConfig,
    int SampleRate,
    int Channels,
    int Timescale,
    IReadOnlyList<Range> Frames,
    IReadOnlyList<uint> Durations);

/// <summary>
/// Cuts an <see cref="AacTrack"/> into parts of at most a given length on frame boundaries,
/// and writes each part as a new M4A with the frames as they were (local ADR 0049 §3). What
/// <see cref="AdtsRemuxer"/> and <see cref="Mp4Demuxer"/> share: each only finds the frames.
/// </summary>
internal static class M4aParts
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partLength"/> is shorter
    /// than a frame.</exception>
    public static IReadOnlyList<RemuxedPart> Cut(ReadOnlySpan<byte> source, AacTrack track, TimeSpan partLength)
    {
        if (track.Frames.Count == 0) return [];

        // In the track's own units, so a cut lands at or before the mark however the frames run.
        var limit = (long)Math.Floor(partLength.TotalSeconds * track.Timescale);

        var parts = new List<RemuxedPart>();
        long elapsed = 0;
        for (var first = 0; first < track.Frames.Count;)
        {
            long length = 0;
            var count = 0;
            while (first + count < track.Frames.Count && length + track.Durations[first + count] <= limit)
            {
                length += track.Durations[first + count];
                count++;
            }

            if (count == 0) throw new ArgumentOutOfRangeException(nameof(partLength), "A part must hold at least one frame.");

            parts.Add(new RemuxedPart(Write(source, track, first, count), TimeSpan.FromSeconds((double)elapsed / track.Timescale)));
            elapsed += length;
            first += count;
        }

        return parts;
    }

    private static byte[] Write(ReadOnlySpan<byte> source, AacTrack track, int first, int count)
    {
        var sizes = new int[count];
        var payload = 0;
        long duration = 0;
        for (var i = 0; i < count; i++)
        {
            sizes[i] = track.Frames[first + i].GetOffsetAndLength(source.Length).Length;
            payload += sizes[i];
            duration += track.Durations[first + i];
        }

        var ftyp = Box("ftyp", Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("mp42"), Ascii("isom"));
        var stts = TimeToSample(track.Durations, first, count);

        // The moov box is written once with a placeholder chunk offset to learn its size, then
        // again with the real one; its size does not depend on the offset's value.
        byte[] Moov(uint chunkOffset) => Box("moov",
            Mvhd(track.Timescale, duration),
            Box("trak",
                Tkhd(duration),
                Box("mdia",
                    Mdhd(track.Timescale, duration),
                    FullBox("hdlr", 0, 0, U32(0), Ascii("soun"), U32(0), U32(0), U32(0), Encoding.ASCII.GetBytes("SoundHandler\0")),
                    Box("minf",
                        FullBox("smhd", 0, 0, U16(0), U16(0)),
                        Box("dinf", FullBox("dref", 0, 0, U32(1), FullBox("url ", 0, 1))),
                        Box("stbl",
                            FullBox("stsd", 0, 0, U32(1), Mp4a(track)),
                            stts,
                            FullBox("stsc", 0, 0, U32(1), U32(1), U32((uint)count), U32(1)),
                            FullBox("stsz", 0, 0, U32(0), U32((uint)count), Concat(sizes.Select(size => U32((uint)size)))),
                            FullBox("stco", 0, 0, U32(1), U32(chunkOffset)))))));

        var moovLength = Moov(0).Length;
        const int mdatHeader = 8;
        var moov = Moov((uint)(ftyp.Length + moovLength + mdatHeader));

        var file = new byte[ftyp.Length + moov.Length + mdatHeader + payload];
        var at = 0;
        ftyp.CopyTo(file, at);
        at += ftyp.Length;
        moov.CopyTo(file, at);
        at += moov.Length;
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(at), (uint)(mdatHeader + payload));
        Encoding.ASCII.GetBytes("mdat").CopyTo(file, at + 4);
        at += mdatHeader;

        for (var i = 0; i < count; i++)
        {
            var frame = source[track.Frames[first + i]];
            frame.CopyTo(file.AsSpan(at));
            at += frame.Length;
        }

        return file;
    }

    /// <summary>The stts box: the part's durations, a run per stretch of equal ones.</summary>
    private static byte[] TimeToSample(IReadOnlyList<uint> durations, int first, int count)
    {
        var runs = new List<(uint Count, uint Delta)>();
        for (var i = first; i < first + count; i++)
        {
            if (runs.Count > 0 && runs[^1].Delta == durations[i]) runs[^1] = (runs[^1].Count + 1, durations[i]);
            else runs.Add((1, durations[i]));
        }

        return FullBox("stts", 0, 0, U32((uint)runs.Count), Concat(runs.SelectMany(run => new[] { U32(run.Count), U32(run.Delta) })));
    }

    private static byte[] Mvhd(int timescale, long duration) => FullBox("mvhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U32(0x00010000), U16(0x0100), U16(0), U32(0), U32(0),
        Matrix(), new byte[24], U32(2));

    // The movie timescale is the track's, so the track's duration is in the same units.
    private static byte[] Tkhd(long duration) => FullBox("tkhd", 0, 0x000003,
        U32(0), U32(0), U32(1), U32(0), U32((uint)duration),
        U32(0), U32(0), U16(0), U16(0), U16(0x0100), U16(0),
        Matrix(), U32(0), U32(0));

    private static byte[] Mdhd(int timescale, long duration) => FullBox("mdhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U16(0x55C4), // "und": no language is claimed for the audio.
        U16(0));

    private static byte[] Mp4a(AacTrack track) => Box("mp4a",
        new byte[6], U16(1),           // reserved, data reference index
        U32(0), U32(0),                // reserved
        U16((ushort)track.Channels), U16(16), U16(0), U16(0),
        // A 16.16 field: a rate past 65535 cannot be written in it, and the esds says it anyway.
        U32(track.SampleRate <= ushort.MaxValue ? (uint)track.SampleRate << 16 : 0),
        Esds(track.AudioSpecificConfig));

    /// <summary>The elementary stream descriptor: MPEG-4 audio and its AudioSpecificConfig.</summary>
    private static byte[] Esds(byte[] audioSpecificConfig)
    {
        var decoderSpecific = Descriptor(0x05, audioSpecificConfig);
        var decoderConfig = Descriptor(0x04, Concat(
        [
            [0x40, 0x15], // MPEG-4 audio; audio stream, upstream 0, reserved 1
            [0, 0, 0],    // buffer size
            U32(0), U32(0),
            decoderSpecific,
        ]));
        var slConfig = Descriptor(0x06, [0x02]);
        var es = Descriptor(0x03, Concat([U16(1), [0], decoderConfig, slConfig]));

        return FullBox("esds", 0, 0, es);
    }

    /// <summary>A descriptor: its tag, its length in 7-bit groups with the top bit set on all
    /// but the last (ISO/IEC 14496-1 expandable size), then its body.</summary>
    private static byte[] Descriptor(byte tag, byte[] body) => Concat([[tag], Length(body.Length), body]);

    private static byte[] Length(int length)
    {
        var groups = new List<byte> { (byte)(length & 0x7F) };
        for (length >>= 7; length > 0; length >>= 7) groups.Insert(0, (byte)(0x80 | (length & 0x7F)));
        return [.. groups];
    }

    private static byte[] Matrix() => Concat(
    [
        U32(0x00010000), U32(0), U32(0),
        U32(0), U32(0x00010000), U32(0),
        U32(0), U32(0), U32(0x40000000),
    ]);

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = Concat(content);
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] FullBox(string type, byte version, int flags, params byte[][] content) =>
        Box(type, Concat([[version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags], .. content]));

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] Concat(IEnumerable<byte[]> parts) => parts.SelectMany(part => part).ToArray();
}
