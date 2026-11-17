using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// ADTS to M4A without re-encoding (local ADR 0049 §3): the AAC frames come out of the stream
/// byte for byte into an MP4 container, cut into parts of at most five minutes on frame
/// boundaries, each part stamped with its offset, and a torn tail dropped rather than sent.
/// </summary>
public sealed class AdtsRemuxerTests
{
    /// <summary>16 kHz, the rate ADR 0048 records at: 1024 samples a frame is 64 ms.</summary>
    private const int SampleRate = 16000;

    [Fact]
    public void An_adts_stream_is_told_apart_from_an_mp4_file()
    {
        Assert.True(AdtsRemuxer.LooksLikeAdts(Stream(1)));
        Assert.False(AdtsRemuxer.LooksLikeAdts(Encoding.ASCII.GetBytes("\0\0\0\x20ftypM4A ")));
        Assert.False(AdtsRemuxer.LooksLikeAdts([]));
    }

    [Fact]
    public void The_frames_are_carried_into_the_mdat_as_they_were()
    {
        var payloads = Enumerable.Range(0, 5).Select(Payload).ToArray();

        var part = Assert.Single(AdtsRemuxer.Remux(Stream(payloads)));

        Assert.Equal(TimeSpan.Zero, part.Offset);
        var boxes = TopLevel(part.M4a);
        Assert.Equal(["ftyp", "moov", "mdat"], boxes.Select(box => box.Type));
        Assert.Equal(payloads.SelectMany(p => p), boxes[2].Body);
    }

    [Fact]
    public void The_sample_table_names_every_frame_and_points_at_the_mdat()
    {
        var payloads = Enumerable.Range(0, 3).Select(Payload).ToArray();
        var m4a = Assert.Single(AdtsRemuxer.Remux(Stream(payloads))).M4a;

        var stsz = Find(m4a, "stsz");
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(stsz.AsSpan(8)));
        for (var i = 0; i < payloads.Length; i++)
        {
            Assert.Equal((uint)payloads[i].Length, BinaryPrimitives.ReadUInt32BigEndian(stsz.AsSpan(12 + (4 * i))));
        }

        var chunkOffset = BinaryPrimitives.ReadUInt32BigEndian(Find(m4a, "stco").AsSpan(8));
        Assert.Equal(payloads[0], m4a.AsSpan((int)chunkOffset, payloads[0].Length).ToArray());

        var mdhd = Find(m4a, "mdhd");
        Assert.Equal((uint)SampleRate, BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(12)));
        Assert.Equal(3u * 1024, BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(16)));
    }

    [Fact]
    public void The_decoder_config_is_rebuilt_from_the_adts_header()
    {
        var esds = Find(Assert.Single(AdtsRemuxer.Remux(Stream(1))).M4a, "esds");

        // AAC-LC (2), 16 kHz (index 8), mono (1): 00010 1000 0001 000.
        var asc = new byte[] { 0x14, 0x08 };
        Assert.True(esds.AsSpan().IndexOf(asc) > 0);
    }

    [Fact]
    public void A_long_stream_is_cut_into_parts_of_at_most_the_length_with_their_offsets()
    {
        // 64 ms a frame: a one-second part holds 15 frames, so 40 frames make 15 + 15 + 10.
        var parts = AdtsRemuxer.Remux(Stream(40), TimeSpan.FromSeconds(1));

        Assert.Equal(3, parts.Count);
        Assert.Equal([15u, 15u, 10u], parts.Select(part => BinaryPrimitives.ReadUInt32BigEndian(Find(part.M4a, "stsz").AsSpan(8))));
        Assert.Equal(TimeSpan.Zero, parts[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(15 * 0.064), parts[1].Offset);
        Assert.Equal(TimeSpan.FromSeconds(30 * 0.064), parts[2].Offset);
    }

    [Fact]
    public void Five_minutes_of_frames_fit_one_default_part()
    {
        // 300 s at 64 ms a frame is 4687.5, so 4687 frames are one part and the next starts a second.
        Assert.Single(AdtsRemuxer.Remux(Stream(4687)));
        Assert.Equal(2, AdtsRemuxer.Remux(Stream(4688)).Count);
    }

    [Fact]
    public void A_torn_last_frame_is_dropped_and_the_whole_ones_kept()
    {
        var whole = Stream(4);
        var torn = whole.Concat(Frame(Payload(9)).Take(10)).ToArray();

        var part = Assert.Single(AdtsRemuxer.Remux(torn));

        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(Find(part.M4a, "stsz").AsSpan(8)));
    }

    [Fact]
    public void Junk_before_the_first_frame_is_skipped()
    {
        var stream = new byte[] { 1, 2, 3 }.Concat(Stream(2)).ToArray();

        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(Find(Assert.Single(AdtsRemuxer.Remux(stream)).M4a, "stsz").AsSpan(8)));
    }

    [Fact]
    public void A_stream_with_no_whole_frame_remuxes_to_nothing()
    {
        Assert.Empty(AdtsRemuxer.Remux(Frame(Payload(0)).Take(5).ToArray()));
        Assert.Empty(AdtsRemuxer.Remux([]));
    }

    private static byte[] Payload(int seed) =>
        Enumerable.Range(0, 20 + (seed % 7)).Select(i => (byte)(seed * 31 + i)).ToArray();

    private static byte[] Stream(int frames) => Stream(Enumerable.Range(0, frames).Select(Payload).ToArray());

    private static byte[] Stream(byte[][] payloads) => payloads.SelectMany(Frame).ToArray();

    /// <summary>One ADTS frame: AAC-LC, 16 kHz, mono, no CRC, one raw data block.</summary>
    private static byte[] Frame(byte[] payload)
    {
        var length = 7 + payload.Length;
        var header = new byte[]
        {
            0xFF,
            0xF1,                                        // MPEG-4, layer 0, protection absent
            (byte)((1 << 6) | (8 << 2) | 0),              // profile LC (1), 16 kHz (8), channel high bit 0
            (byte)((1 << 6) | ((length >> 11) & 0x03)),   // mono, frame length high bits
            (byte)((length >> 3) & 0xFF),
            (byte)(((length & 0x07) << 5) | 0x1F),
            0xFC,                                         // buffer fullness, one block
        };

        return header.Concat(payload).ToArray();
    }

    private static List<(string Type, byte[] Body)> TopLevel(byte[] file)
    {
        var boxes = new List<(string, byte[])>();
        for (var at = 0; at < file.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            boxes.Add((Encoding.ASCII.GetString(file, at + 4, 4), file[(at + 8)..(at + size)]));
            at += size;
        }

        return boxes;
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
}
