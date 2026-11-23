using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>One part of a recording, remuxed: an M4A file and where it starts in the
/// recording.</summary>
/// <param name="M4a">A complete MP4 file (<c>audio/mp4</c>) holding the part's AAC frames as they were.</param>
/// <param name="Offset">The part's start in its recording, from the frames before the cut.</param>
public sealed record RemuxedPart(byte[] M4a, TimeSpan Offset);

/// <summary>
/// Cuts an <see cref="AacTrack"/> into parts of at most a given length on frame boundaries
/// and writes each part into an MP4 container of its own, the frames byte for byte (local
/// ADR 0049 §3). Where the frames came from — <see cref="AdtsRemuxer"/> or
/// <see cref="Mp4AacReader"/> — does not change the cut, the offsets or the file.
/// </summary>
internal static class AacRemuxer
{
    /// <summary>The part length ADR 0049 §3 sends: under the 15-to-30-minute synchronous limit
    /// with room to spare.</summary>
    internal static readonly TimeSpan DefaultPartLength = TimeSpan.FromMinutes(5);

    /// <summary>Cuts and remuxes <paramref name="track"/>, whose frames lie in
    /// <paramref name="source"/>. Empty when the track holds no frame.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partLength"/> is shorter
    /// than a frame.</exception>
    internal static IReadOnlyList<RemuxedPart> Remux(ReadOnlySpan<byte> source, AacTrack track, TimeSpan? partLength = null)
    {
        var frames = track.Frames;
        if (frames.Count == 0) return [];

        var limit = (long)Math.Floor((partLength ?? DefaultPartLength).TotalSeconds * track.Timescale);

        var parts = new List<RemuxedPart>();
        long elapsed = 0;
        for (var first = 0; first < frames.Count;)
        {
            // A part takes frames while they fit its length: a cut at or before the mark.
            var count = 0;
            long length = 0;
            while (first + count < frames.Count && length + frames[first + count].Duration <= limit)
            {
                length += frames[first + count].Duration;
                count++;
            }

            if (count == 0) throw new ArgumentOutOfRangeException(nameof(partLength), "A part must hold at least one frame.");

            var offset = TimeSpan.FromSeconds((double)elapsed / track.Timescale);
            parts.Add(new RemuxedPart(WriteM4a(source, track, first, count, length), offset));
            elapsed += length;
            first += count;
        }

        return parts;
    }

    private static byte[] WriteM4a(ReadOnlySpan<byte> source, AacTrack track, int first, int count, long duration)
    {
        var config = track.Config;
        var sizes = new int[count];
        var payload = 0;
        for (var i = 0; i < count; i++)
        {
            sizes[i] = track.Frames[first + i].Bytes.GetOffsetAndLength(source.Length).Length;
            payload += sizes[i];
        }

        var timeToSample = TimeToSample(track.Frames, first, count);
        var ftyp = Box("ftyp", Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("mp42"), Ascii("isom"));

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
                            FullBox("stsd", 0, 0, U32(1), Mp4a(config)),
                            FullBox("stts", 0, 0, U32((uint)timeToSample.Count), Concat(timeToSample.Select(run => Concat([U32((uint)run.Count), U32((uint)run.Delta)])))),
                            FullBox("stsc", 0, 0, U32(1), U32(1), U32((uint)count), U32(1)),
                            FullBox("stsz", 0, 0, U32(0), U32((uint)count), Concat(sizes.Select(size => U32((uint)size)))),
                            FullBox("stco", 0, 0, U32(1), U32(chunkOffset)))))));

        var moovLength = Moov(0).Length;
        var mdatHeader = 8;
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
            var frame = source[track.Frames[first + i].Bytes];
            frame.CopyTo(file.AsSpan(at));
            at += frame.Length;
        }

        return file;
    }

    /// <summary>The frames' durations as <c>stts</c> runs: one run when every frame is as long.</summary>
    private static List<(int Count, int Delta)> TimeToSample(IReadOnlyList<AacFrame> frames, int first, int count)
    {
        var runs = new List<(int Count, int Delta)>();
        for (var i = first; i < first + count; i++)
        {
            var delta = frames[i].Duration;
            if (runs.Count > 0 && runs[^1].Delta == delta) runs[^1] = (runs[^1].Count + 1, delta);
            else runs.Add((1, delta));
        }

        return runs;
    }

    private static byte[] Mvhd(int timescale, long duration) => FullBox("mvhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U32(0x00010000), U16(0x0100), U16(0), U32(0), U32(0),
        Matrix(), new byte[24], U32(2));

    // The movie timescale is the track's, so the track's duration is in its units too.
    private static byte[] Tkhd(long duration) => FullBox("tkhd", 0, 0x000003,
        U32(0), U32(0), U32(1), U32(0), U32((uint)duration),
        U32(0), U32(0), U16(0), U16(0), U16(0x0100), U16(0),
        Matrix(), U32(0), U32(0));

    private static byte[] Mdhd(int timescale, long duration) => FullBox("mdhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U16(0x55C4), // "und": no language is claimed for the audio.
        U16(0));

    private static byte[] Mp4a(AacConfig config) => Box("mp4a",
        new byte[6], U16(1),           // reserved, data reference index
        U32(0), U32(0),                // reserved
        U16((ushort)config.Channels), U16(16), U16(0), U16(0),
        U32((uint)config.SampleRate << 16),
        Esds(config));

    /// <summary>The elementary stream descriptor: MPEG-4 audio, and the track's
    /// AudioSpecificConfig.</summary>
    private static byte[] Esds(AacConfig config)
    {
        var decoderSpecific = Descriptor(0x05, config.AudioSpecificConfig);
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

    private static byte[] Descriptor(byte tag, byte[] body) => Concat([[tag, (byte)body.Length], body]);

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
