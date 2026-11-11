using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>One part of a recording, remuxed: an M4A file and where it starts in the
/// recording.</summary>
/// <param name="M4a">A complete MP4 file (<c>audio/mp4</c>) holding the part's AAC frames as they were.</param>
/// <param name="Offset">The part's start in its recording, from the frames before the cut.</param>
public sealed record RemuxedPart(byte[] M4a, TimeSpan Offset);

/// <summary>
/// Lifts the AAC frames out of an ADTS stream and writes them into MP4 containers as they
/// are, in parts of at most a given length (local ADR 0049 §3). Nothing is decoded or
/// re-encoded: an ADTS frame stands alone, so a cut on a frame boundary needs no codec, and
/// the audio is the same bytes in a different framing.
/// <para>
/// A cut-off stream still remuxes up to its last whole frame, so whatever an interrupted
/// recording wrote is sent. Bytes that are not a frame — a torn tail, junk before the first
/// sync word — are skipped.
/// </para>
/// </summary>
public static class AdtsRemuxer
{
    /// <summary>The part length ADR 0049 §3 sends: under the 15-to-30-minute synchronous limit
    /// with room to spare.</summary>
    public static readonly TimeSpan DefaultPartLength = TimeSpan.FromMinutes(5);

    /// <summary>AAC-LC and its siblings code 1024 samples per raw data block.</summary>
    private const int SamplesPerBlock = 1024;

    private static readonly int[] SampleRates =
        [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    /// <summary>Whether <paramref name="stream"/> opens with an ADTS sync word — the test a
    /// caller uses to tell a stream to remux from a container to send as it is.</summary>
    public static bool LooksLikeAdts(ReadOnlySpan<byte> stream) =>
        stream.Length >= 2 && stream[0] == 0xFF && (stream[1] & 0xF6) == 0xF0;

    /// <summary>Splits and remuxes <paramref name="adts"/>. Empty when the stream holds no
    /// whole frame.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partLength"/> is shorter
    /// than a frame.</exception>
    public static IReadOnlyList<RemuxedPart> Remux(ReadOnlySpan<byte> adts, TimeSpan? partLength = null)
    {
        var frames = ReadFrames(adts, out var config);
        if (frames.Count == 0 || config is null) return [];

        var length = partLength ?? DefaultPartLength;
        var framesPerPart = (int)Math.Floor(length.TotalSeconds * config.SampleRate / (config.BlocksPerFrame * SamplesPerBlock));
        if (framesPerPart < 1) throw new ArgumentOutOfRangeException(nameof(partLength), "A part must hold at least one frame.");

        var parts = new List<RemuxedPart>();
        for (var first = 0; first < frames.Count; first += framesPerPart)
        {
            var count = Math.Min(framesPerPart, frames.Count - first);
            var offset = TimeSpan.FromSeconds((double)first * config.BlocksPerFrame * SamplesPerBlock / config.SampleRate);
            parts.Add(new RemuxedPart(WriteM4a(adts, frames, first, count, config), offset));
        }

        return parts;
    }

    /// <summary>The raw AAC payload of every whole frame, by position in the stream, and the
    /// stream's configuration from its first frame.</summary>
    internal static List<Range> ReadFrames(ReadOnlySpan<byte> adts, out AdtsConfig? config)
    {
        config = null;
        var frames = new List<Range>();
        var position = 0;

        while (position + 7 <= adts.Length)
        {
            var header = adts[position..];
            if (!LooksLikeAdts(header))
            {
                position++;
                continue;
            }

            var protectionAbsent = (header[1] & 0x01) == 1;
            var headerLength = protectionAbsent ? 7 : 9;
            var objectType = ((header[2] >> 6) & 0x03) + 1;
            var rateIndex = (header[2] >> 2) & 0x0F;
            var channels = ((header[2] & 0x01) << 2) | ((header[3] >> 6) & 0x03);
            var frameLength = ((header[3] & 0x03) << 11) | (header[4] << 3) | ((header[5] >> 5) & 0x07);
            var blocks = (header[6] & 0x03) + 1;

            if (rateIndex >= SampleRates.Length || frameLength <= headerLength)
            {
                position++;
                continue;
            }

            // A torn last frame: everything before it is whole, and that is what is kept.
            if (position + frameLength > adts.Length) break;

            config ??= new AdtsConfig(objectType, rateIndex, SampleRates[rateIndex], channels, blocks);
            frames.Add(new Range(position + headerLength, position + frameLength));
            position += frameLength;
        }

        return frames;
    }

    private static byte[] WriteM4a(ReadOnlySpan<byte> adts, List<Range> frames, int first, int count, AdtsConfig config)
    {
        var sizes = new int[count];
        var payload = 0;
        for (var i = 0; i < count; i++)
        {
            sizes[i] = frames[first + i].GetOffsetAndLength(adts.Length).Length;
            payload += sizes[i];
        }

        var samplesPerFrame = config.BlocksPerFrame * SamplesPerBlock;
        var duration = (long)count * samplesPerFrame;

        var ftyp = Box("ftyp", Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("mp42"), Ascii("isom"));

        // The moov box is written once with a placeholder chunk offset to learn its size, then
        // again with the real one; its size does not depend on the offset's value.
        byte[] Moov(uint chunkOffset) => Box("moov",
            Mvhd(config.SampleRate, duration),
            Box("trak",
                Tkhd(duration),
                Box("mdia",
                    Mdhd(config.SampleRate, duration),
                    FullBox("hdlr", 0, 0, U32(0), Ascii("soun"), U32(0), U32(0), U32(0), Encoding.ASCII.GetBytes("SoundHandler\0")),
                    Box("minf",
                        FullBox("smhd", 0, 0, U16(0), U16(0)),
                        Box("dinf", FullBox("dref", 0, 0, U32(1), FullBox("url ", 0, 1))),
                        Box("stbl",
                            FullBox("stsd", 0, 0, U32(1), Mp4a(config)),
                            FullBox("stts", 0, 0, U32(1), U32((uint)count), U32((uint)samplesPerFrame)),
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
            var frame = adts[frames[first + i]];
            frame.CopyTo(file.AsSpan(at));
            at += frame.Length;
        }

        return file;
    }

    private static byte[] Mvhd(int timescale, long duration) => FullBox("mvhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U32(0x00010000), U16(0x0100), U16(0), U32(0), U32(0),
        Matrix(), new byte[24], U32(2));

    // The movie timescale is the sample rate, so the track's duration is in samples too.
    private static byte[] Tkhd(long duration) =>FullBox("tkhd", 0, 0x000003,
        U32(0), U32(0), U32(1), U32(0), U32((uint)duration),
        U32(0), U32(0), U16(0), U16(0), U16(0x0100), U16(0),
        Matrix(), U32(0), U32(0));

    private static byte[] Mdhd(int timescale, long duration) => FullBox("mdhd", 0, 0,
        U32(0), U32(0), U32((uint)timescale), U32((uint)duration),
        U16(0x55C4), // "und": no language is claimed for the audio.
        U16(0));

    private static byte[] Mp4a(AdtsConfig config) => Box("mp4a",
        new byte[6], U16(1),           // reserved, data reference index
        U32(0), U32(0),                // reserved
        U16((ushort)config.Channels), U16(16), U16(0), U16(0),
        U32((uint)config.SampleRate << 16),
        Esds(config));

    /// <summary>The elementary stream descriptor: MPEG-4 audio, and the two-byte
    /// AudioSpecificConfig rebuilt from the ADTS header.</summary>
    private static byte[] Esds(AdtsConfig config)
    {
        var audioSpecificConfig = new[]
        {
            (byte)((config.ObjectType << 3) | (config.RateIndex >> 1)),
            (byte)(((config.RateIndex & 0x01) << 7) | (config.Channels << 3)),
        };

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

    /// <summary>What every frame of the stream shares, read from its first header.</summary>
    internal sealed record AdtsConfig(int ObjectType, int RateIndex, int SampleRate, int Channels, int BlocksPerFrame);
}
