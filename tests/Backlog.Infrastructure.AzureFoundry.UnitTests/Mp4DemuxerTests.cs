using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// An MP4 or M4A recording cut like an ADTS one (local ADR 0049 §3): its AAC frames read from
/// the sample table of a plain file or from the movie fragments a browser's MediaRecorder
/// writes, cut on frame boundaries at or before each five-minute mark, each part a new M4A
/// whose mdat holds the frames byte for byte, with the decoder config carried over as it was.
/// </summary>
public sealed class Mp4DemuxerTests
{
    /// <summary>16 kHz: 1024 samples a frame is 64 ms, as in <see cref="AdtsRemuxerTests"/>.</summary>
    private const int SampleRate = 16000;

    /// <summary>AAC-LC, 16 kHz, mono, as the esds of a 16 kHz recording carries it.</summary>
    private static readonly byte[] Asc = [0x14, 0x08];

    [Fact]
    public void An_mp4_file_is_told_apart_from_an_adts_stream()
    {
        Assert.True(Mp4Demuxer.LooksLikeMp4(Plain(Payloads(2))));
        Assert.True(Mp4Demuxer.LooksLikeMp4(Fragmented(Payloads(2), perFragment: 1)));
        Assert.False(Mp4Demuxer.LooksLikeMp4([0xFF, 0xF1, 0x60, 0x40, 0, 0x1F, 0xFC, 0]));
        Assert.False(Mp4Demuxer.LooksLikeMp4([]));
    }

    [Fact]
    public void A_plain_m4a_over_five_minutes_is_cut_at_the_mark_with_its_offsets()
    {
        // 4688 frames of 64 ms are one frame past five minutes: 4687 + 1.
        var parts = Mp4Demuxer.Remux(Plain(Payloads(4688)));

        Assert.Equal(2, parts.Count);
        Assert.Equal([4687u, 1u], parts.Select(part => SampleCount(part.M4a)));
        Assert.Equal(TimeSpan.Zero, parts[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(4687 * 1024 / (double)SampleRate), parts[1].Offset);
    }

    [Fact]
    public void A_plain_m4a_in_several_chunks_keeps_every_frame_byte_for_byte()
    {
        var payloads = Payloads(7);

        var part = Assert.Single(Mp4Demuxer.Remux(Plain(payloads, samplesPerChunk: 3)));

        Assert.Equal(payloads.SelectMany(p => p), Mdat(part.M4a));
        Assert.True(Find(part.M4a, "esds").AsSpan().IndexOf(Asc) > 0);
    }

    [Fact]
    public void A_plain_m4a_with_64_bit_chunk_offsets_is_read()
    {
        var payloads = Payloads(4);

        var part = Assert.Single(Mp4Demuxer.Remux(Plain(payloads, samplesPerChunk: 2, co64: true)));

        Assert.Equal(payloads.SelectMany(p => p), Mdat(part.M4a));
    }

    [Fact]
    public void A_fragmented_m4a_from_a_browser_is_cut_into_parts_with_its_offsets()
    {
        // One second a part holds 15 frames of 64 ms: 40 frames in fragments of 4 make 15 + 15 + 10.
        var payloads = Payloads(40);

        var parts = Mp4Demuxer.Remux(Fragmented(payloads, perFragment: 4), TimeSpan.FromSeconds(1));

        Assert.Equal(3, parts.Count);
        Assert.Equal([15u, 15u, 10u], parts.Select(part => SampleCount(part.M4a)));
        Assert.Equal(TimeSpan.FromSeconds(15 * 1024 / (double)SampleRate), parts[1].Offset);
        Assert.Equal(TimeSpan.FromSeconds(30 * 1024 / (double)SampleRate), parts[2].Offset);
        Assert.Equal(payloads.SelectMany(p => p), parts.SelectMany(part => Mdat(part.M4a)));
    }

    [Fact]
    public void A_fragmented_m4a_with_defaults_from_trex_is_read()
    {
        var payloads = Enumerable.Range(0, 6).Select(_ => new byte[] { 9, 8, 7, 6, 5 }).ToArray();

        var part = Assert.Single(Mp4Demuxer.Remux(Fragmented(payloads, perFragment: 3, sizesInTrex: true)));

        Assert.Equal(6u, SampleCount(part.M4a));
        Assert.Equal(payloads.SelectMany(p => p), Mdat(part.M4a));
    }

    [Fact]
    public void A_cut_off_fragmented_m4a_keeps_its_whole_frames()
    {
        var payloads = Payloads(8);
        var file = Fragmented(payloads, perFragment: 4);
        var torn = file[..^3];

        var part = Assert.Single(Mp4Demuxer.Remux(torn));

        Assert.Equal(7u, SampleCount(part.M4a));
        Assert.Equal(payloads.Take(7).SelectMany(p => p), Mdat(part.M4a));
    }

    [Fact]
    public void A_file_that_is_not_an_m4a_remuxes_to_nothing()
    {
        // An ftyp and nothing a moov would name.
        Assert.Empty(Mp4Demuxer.Remux(Concat([Box("ftyp", Ascii("M4A ")), Ascii("rest of the file")])));
        Assert.Empty(Mp4Demuxer.Remux(Concat([Box("ftyp", Ascii("M4A ")), Box("moov", Box("trak"))])));
        Assert.Empty(Mp4Demuxer.Remux([1, 2, 3]));
        Assert.Empty(Mp4Demuxer.Remux([]));
    }

    [Fact]
    public void The_m4a_the_adts_remuxer_writes_is_read_back_to_the_same_frames()
    {
        var adts = Enumerable.Range(0, 5).SelectMany(seed =>
        {
            var payload = Payload(seed);
            var length = 7 + payload.Length;
            return new byte[] { 0xFF, 0xF1, 0x60, 0x40, (byte)((length >> 3) & 0xFF), (byte)(((length & 0x07) << 5) | 0x1F), 0xFC }.Concat(payload);
        }).ToArray();
        var m4a = Assert.Single(AdtsRemuxer.Remux(adts)).M4a;

        var again = Assert.Single(Mp4Demuxer.Remux(m4a));

        Assert.Equal(Mdat(m4a), Mdat(again.M4a));
    }

    [Fact]
    public async Task A_box_whose_64_bit_size_overflows_ends_the_read_instead_of_looping()
    {
        // A 12-byte ftyp, then a box claiming a largesize of 2^63 - 12: at + size wraps to
        // long.MinValue, whose low 32 bits put the read back at the start of the file.
        var huge = Concat([U32(1), Ascii("free"), U64((1UL << 63) - 12), new byte[16]]);
        var file = Concat([Box("ftyp", Ascii("M4A ")), huge, Plain(Payloads(2))]);

        // WaitAsync throws TimeoutException when the read does not end.
        var parts = await Task.Run(() => Mp4Demuxer.Remux(file), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(parts);
    }

    [Fact]
    public void A_file_cut_at_any_byte_never_throws()
    {
        foreach (var file in new[] { Plain(Payloads(9), samplesPerChunk: 4), Fragmented(Payloads(9), perFragment: 4) })
        {
            for (var length = 0; length <= file.Length; length++)
            {
                var cut = file[..length];
                var exception = Record.Exception(() => Mp4Demuxer.Remux(cut));
                Assert.True(exception is null, $"cut at {length} of {file.Length}: {exception}");
            }
        }
    }

    [Fact]
    public void A_file_with_any_byte_corrupted_never_throws()
    {
        foreach (var file in new[] { Plain(Payloads(9), samplesPerChunk: 4), Fragmented(Payloads(9), perFragment: 4) })
        {
            for (var at = 0; at < file.Length; at++)
            {
                foreach (var value in new byte[] { 0x00, 0xFF })
                {
                    var corrupt = file.ToArray();
                    corrupt[at] = value;
                    var exception = Record.Exception(() => Mp4Demuxer.Remux(corrupt));
                    Assert.True(exception is null, $"byte {at} set to {value:X2}: {exception}");
                }
            }
        }
    }

    [Fact]
    public void A_frame_duration_past_any_part_length_is_read_as_one_frame()
    {
        // An stts delta of 0xFFFFFFFF: no frame lasts more than a second, so it is a corrupt
        // table, and the frame is given an AAC frame's length rather than failing the cut.
        var file = Plain(Payloads(3));
        var stts = file.AsSpan().IndexOf(Ascii("stts"));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(stts + 16), uint.MaxValue);

        var part = Assert.Single(Mp4Demuxer.Remux(file));

        Assert.Equal(3u, SampleCount(part.M4a));
    }

    [Fact]
    public void A_decoder_config_of_128_bytes_or_more_is_written_with_a_long_descriptor_length_and_read_back()
    {
        // AAC-LC, 16 kHz, mono, then padding past what a one-byte descriptor length can say.
        var config = new byte[] { 0x14, 0x08 }.Concat(Enumerable.Repeat((byte)0, 200)).ToArray();
        var source = Payloads(3).SelectMany(p => p).ToArray();
        var frames = new List<Range>();
        var at = 0;
        foreach (var payload in Payloads(3))
        {
            frames.Add(new Range(at, at + payload.Length));
            at += payload.Length;
        }

        var part = Assert.Single(M4aParts.Cut(source, new AacTrack(config, SampleRate, 1, SampleRate, frames, [1024, 1024, 1024]), TimeSpan.FromMinutes(5)));

        var read = Mp4Demuxer.Read(part.M4a);
        Assert.NotNull(read);
        Assert.Equal(config, read.AudioSpecificConfig);
        Assert.Equal(3, read.Frames.Count);
    }

    private static byte[] Payload(int seed) =>
        Enumerable.Range(0, 20 + (seed % 7)).Select(i => (byte)(seed * 31 + i)).ToArray();

    private static byte[][] Payloads(int count) => Enumerable.Range(0, count).Select(Payload).ToArray();

    /// <summary>A plain MP4: ftyp, mdat with the frames, then moov whose sample table names
    /// them in chunks of <paramref name="samplesPerChunk"/>.</summary>
    private static byte[] Plain(byte[][] payloads, int samplesPerChunk = int.MaxValue, bool co64 = false)
    {
        var ftyp = Box("ftyp", Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("isom"));
        var data = payloads.SelectMany(p => p).ToArray();
        var mdat = Box("mdat", data);
        var dataStart = ftyp.Length + 8;

        var perChunk = Math.Min(samplesPerChunk, payloads.Length);
        var offsets = new List<long>();
        long at = dataStart;
        for (var i = 0; i < payloads.Length; i++)
        {
            if (i % perChunk == 0) offsets.Add(at);
            at += payloads[i].Length;
        }

        var stbl = Box("stbl",
            FullBox("stsd", U32(1), Mp4a()),
            FullBox("stts", U32(1), U32((uint)payloads.Length), U32(1024)),
            FullBox("stsc", U32(1), U32(1), U32((uint)perChunk), U32(1)),
            FullBox("stsz", U32(0), U32((uint)payloads.Length), Concat(payloads.Select(p => U32((uint)p.Length)))),
            co64
                ? FullBox("co64", U32((uint)offsets.Count), Concat(offsets.Select(o => U64((ulong)o))))
                : FullBox("stco", U32((uint)offsets.Count), Concat(offsets.Select(o => U32((uint)o)))));

        return Concat([ftyp, mdat, Moov(stbl, mvex: null)]);
    }

    /// <summary>A fragmented MP4 as a MediaRecorder writes one: ftyp, a moov with an empty
    /// sample table and a trex, then a moof and mdat per <paramref name="perFragment"/> frames,
    /// each trun's data offset measured from its moof.</summary>
    private static byte[] Fragmented(byte[][] payloads, int perFragment, bool sizesInTrex = false)
    {
        var ftyp = Box("ftyp", Ascii("iso5"), U32(0), Ascii("iso5"), Ascii("mp41"));
        var stbl = Box("stbl",
            FullBox("stsd", U32(1), Mp4a()),
            FullBox("stts", U32(0)),
            FullBox("stsc", U32(0)),
            FullBox("stsz", U32(0), U32(0)),
            FullBox("stco", U32(0)));
        var trex = FullBox("trex", U32(1), U32(1), U32(1024), U32(sizesInTrex ? (uint)payloads[0].Length : 0), U32(0));
        var file = new List<byte[]> { ftyp, Moov(stbl, Box("mvex", trex)) };

        for (var first = 0; first < payloads.Length; first += perFragment)
        {
            var frames = payloads.Skip(first).Take(perFragment).ToArray();

            // tfhd flags 0x020000: default-base-is-moof. trun flags: data-offset, and a size per
            // sample unless the trex gives it; the duration always comes from trex.
            byte[] Moof(uint dataOffset) => Box("moof",
                FullBox("mfhd", U32((uint)(first / perFragment) + 1)),
                Box("traf",
                    FullBox("tfhd", 0x020000, U32(1)),
                    FullBox("trun", sizesInTrex ? 0x000001 : 0x000201, U32((uint)frames.Length), U32(dataOffset),
                        sizesInTrex ? Array.Empty<byte>() : Concat(frames.Select(f => U32((uint)f.Length))))));

            var moofLength = Moof(0).Length;
            file.Add(Moof((uint)(moofLength + 8)));
            file.Add(Box("mdat", frames.SelectMany(f => f).ToArray()));
        }

        return Concat(file);
    }

    private static byte[] Moov(byte[] stbl, byte[]? mvex)
    {
        var trak = Box("trak",
            FullBox("tkhd", 0x000003, U32(0), U32(0), U32(1), U32(0), U32(0), new byte[60]),
            Box("mdia",
                FullBox("mdhd", U32(0), U32(0), U32(SampleRate), U32(0), U16(0x55C4), U16(0)),
                FullBox("hdlr", U32(0), Ascii("soun"), new byte[12], Ascii("Sound\0")),
                Box("minf", FullBox("smhd", U32(0)), stbl)));

        return mvex is null
            ? Box("moov", FullBox("mvhd", new byte[96]), trak)
            : Box("moov", FullBox("mvhd", new byte[96]), trak, mvex);
    }

    private static byte[] Mp4a() => Box("mp4a",
        new byte[6], U16(1), new byte[8],
        U16(1), U16(16), U16(0), U16(0), U32((uint)SampleRate << 16),
        FullBox("esds", Descriptor(0x03, Concat(
        [
            U16(1), [0],
            Descriptor(0x04, Concat([[0x40, 0x15, 0, 0, 0], U32(0), U32(0), Descriptor(0x05, Asc)])),
            Descriptor(0x06, [0x02]),
        ]))));

    private static byte[] Descriptor(byte tag, byte[] body) => Concat([[tag, (byte)body.Length], body]);

    private static uint SampleCount(byte[] m4a) => BinaryPrimitives.ReadUInt32BigEndian(Find(m4a, "stsz").AsSpan(8));

    /// <summary>The body of the file's top-level mdat.</summary>
    private static byte[] Mdat(byte[] file)
    {
        for (var at = 0; at < file.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            if (Encoding.ASCII.GetString(file, at + 4, 4) == "mdat") return file[(at + 8)..(at + size)];
            at += size;
        }

        throw new InvalidOperationException("no mdat");
    }

    /// <summary>The first box of <paramref name="type"/> anywhere in the file, its body from
    /// just after the type.</summary>
    private static byte[] Find(byte[] file, string type)
    {
        var at = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(type));
        Assert.True(at >= 4, $"no {type} box");
        var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at - 4));
        return file[(at + 4)..(at - 4 + size)];
    }

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = Concat(content);
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] FullBox(string type, params byte[][] content) => FullBox(type, 0, content);

    private static byte[] FullBox(string type, int flags, params byte[][] content) =>
        Box(type, Concat([[0, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags], .. content]));

    private static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

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
