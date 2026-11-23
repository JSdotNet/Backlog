using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The AAC frames of an MP4 or M4A recording (local ADR 0049 §3), read so the remuxer can cut
/// them like ADTS: from a classic sample table, or from the movie fragments Chromium's
/// MediaRecorder writes behind an empty one. A cut-off file reads up to its last whole frame;
/// a file that holds no AAC track answers a reason, never an exception.
/// </summary>
public sealed class Mp4AacReaderTests
{
    /// <summary>AAC-LC (2), 48 kHz (index 3), mono (1).</summary>
    private static readonly byte[] Asc48kMono = [0x11, 0x88];

    [Fact]
    public void A_classic_m4a_written_from_adts_reads_back_frame_for_frame()
    {
        var payloads = Enumerable.Range(0, 7).Select(Payload).ToArray();
        var m4a = Assert.Single(AdtsRemuxer.Remux(AdtsStream(payloads))).M4a;

        var track = Read(m4a);

        Assert.Equal(16000, track.Timescale);
        Assert.Equal(16000, track.Config.SampleRate);
        Assert.Equal(1, track.Config.Channels);
        Assert.Equal(new byte[] { 0x14, 0x08 }, track.Config.AudioSpecificConfig);
        Assert.Equal(payloads, track.Frames.Select(frame => m4a[frame.Bytes]));
        Assert.All(track.Frames, frame => Assert.Equal(1024, frame.Duration));
    }

    [Fact]
    public void A_classic_sample_table_with_chunks_64_bit_offsets_and_a_fixed_size_reads_every_frame()
    {
        // Six frames of 12 bytes in chunks of 4 and 2, the chunk offsets in co64.
        var payloads = Enumerable.Range(0, 6).Select(i => Enumerable.Repeat((byte)(i + 1), 12).ToArray()).ToArray();
        var mdatBody = payloads.SelectMany(p => p).ToArray();

        byte[] File(ulong mdatData) => Mp4.Concat(
            Mp4.Ftyp(),
            Mp4.Moov(
                Mp4.Trak(48000, Asc48kMono,
                [
                    Mp4.FullBox("stts", 0, 0, Mp4.U32(2), Mp4.U32(4), Mp4.U32(1024), Mp4.U32(2), Mp4.U32(960)),
                    Mp4.FullBox("stsc", 0, 0, Mp4.U32(2), Mp4.U32(1), Mp4.U32(4), Mp4.U32(1), Mp4.U32(2), Mp4.U32(2), Mp4.U32(1)),
                    Mp4.FullBox("stsz", 0, 0, Mp4.U32(12), Mp4.U32(6)),
                    Mp4.FullBox("co64", 0, 0, Mp4.U32(2), Mp4.U64(mdatData), Mp4.U64(mdatData + 48)),
                ],
                hdlr: "soun")),
            Mp4.Box("mdat", mdatBody));

        var probe = File(0);
        var file = File((ulong)(probe.Length - mdatBody.Length));

        var track = Read(file);

        Assert.Equal(48000, track.Timescale);
        Assert.Equal(payloads, track.Frames.Select(frame => file[frame.Bytes]));
        Assert.Equal([1024, 1024, 1024, 1024, 960, 960], track.Frames.Select(frame => frame.Duration));
    }

    [Fact]
    public void A_fragmented_m4a_reads_its_frames_from_every_fragment_in_order()
    {
        var first = Enumerable.Range(0, 3).Select(Payload).ToArray();
        var second = Enumerable.Range(3, 2).Select(Payload).ToArray();

        var file = Fragmented([first, second]);

        var track = Read(file);

        Assert.Equal(48000, track.Timescale);
        Assert.Equal(first.Concat(second), track.Frames.Select(frame => file[frame.Bytes]));
        Assert.All(track.Frames, frame => Assert.Equal(1024, frame.Duration));
    }

    [Fact]
    public void A_fragment_with_its_own_durations_and_a_64_bit_mdat_reads_them()
    {
        // tfhd carries no default: the trun names each sample's size and duration, and the
        // mdat is written with a 64-bit size.
        var payloads = Enumerable.Range(0, 3).Select(Payload).ToArray();
        var durations = new uint[] { 1024, 1024, 512 };

        byte[] Moof(int dataOffset) => Mp4.Box("moof",
            Mp4.FullBox("mfhd", 0, 0, Mp4.U32(1)),
            Mp4.Box("traf",
                Mp4.FullBox("tfhd", 0, 0x020000, Mp4.U32(1)),
                Mp4.FullBox("trun", 0, 0x000301, Mp4.U32(3), Mp4.U32((uint)dataOffset),
                    Mp4.Concat(payloads.Select((p, i) => Mp4.Concat(Mp4.U32(durations[i]), Mp4.U32((uint)p.Length))).ToArray()))));

        var moofLength = Moof(0).Length;
        var body = payloads.SelectMany(p => p).ToArray();
        var file = Mp4.Concat(Mp4.Ftyp(), FragmentedMoov(), Moof(moofLength + 16), Mp4.LargeBox("mdat", body));

        var track = Read(file);

        Assert.Equal(payloads, track.Frames.Select(frame => file[frame.Bytes]));
        Assert.Equal([1024, 1024, 512], track.Frames.Select(frame => frame.Duration));
    }

    [Fact]
    public void A_cut_off_fragmented_file_reads_up_to_its_last_whole_frame()
    {
        var first = Enumerable.Range(0, 3).Select(Payload).ToArray();
        var second = Enumerable.Range(3, 3).Select(Payload).ToArray();
        var whole = Fragmented([first, second]);

        // The recording stopped halfway through the second fragment's last frame.
        var cut = whole[..(whole.Length - (second[2].Length / 2))];

        var track = Read(cut);

        Assert.Equal(first.Concat(second.Take(2)), track.Frames.Select(frame => cut[frame.Bytes]));
    }

    [Fact]
    public void A_last_mdat_sized_to_the_end_of_the_file_is_read()
    {
        var payloads = Enumerable.Range(0, 2).Select(Payload).ToArray();
        var file = Fragmented([payloads], lastMdatToEnd: true);

        Assert.Equal(payloads, Read(file).Frames.Select(frame => file[frame.Bytes]));
    }

    [Fact]
    public void The_audio_track_is_chosen_past_a_track_that_is_not_sound()
    {
        var payloads = Enumerable.Range(0, 2).Select(Payload).ToArray();
        var body = payloads.SelectMany(p => p).ToArray();

        byte[] File(uint mdatData) => Mp4.Concat(
            Mp4.Ftyp(),
            Mp4.Moov(
                Mp4.Trak(90000, Asc48kMono, Mp4.EmptyTables(), hdlr: "vide", trackId: 1),
                Mp4.Trak(48000, Asc48kMono, Mp4.Tables(payloads, mdatData), hdlr: "soun", trackId: 2)),
            Mp4.Box("mdat", body));

        var file = File((uint)(File(0).Length - body.Length));

        var track = Read(file);

        Assert.Equal(48000, track.Timescale);
        Assert.Equal(payloads, track.Frames.Select(frame => file[frame.Bytes]));
    }

    [Fact]
    public void An_explicit_sampling_frequency_in_the_audio_specific_config_is_read()
    {
        // AAC-LC (2), index 15 then 24 bits of 16000, mono: 00010 1111 <16000> 0001 + pad.
        var asc = Bits((2, 5), (15, 4), (16000, 24), (1, 4), (0, 3));
        var file = Mp4.Concat(Mp4.Ftyp(), Mp4.Moov(Mp4.Trak(16000, asc, Mp4.EmptyTables(), hdlr: "soun")));

        var track = Read(file);

        Assert.Equal(16000, track.Config.SampleRate);
        Assert.Equal(asc, track.Config.AudioSpecificConfig);
        Assert.Empty(track.Frames);
    }

    [Fact]
    public void A_sound_track_that_is_not_aac_answers_a_reason()
    {
        var opus = Mp4.Concat(Mp4.Ftyp(), Mp4.Moov(Mp4.Trak(48000, Asc48kMono, Mp4.EmptyTables(), hdlr: "soun", sampleEntry: "Opus")));

        Assert.False(Mp4AacReader.TryRead(opus, out var track, out var reason));
        Assert.Null(track);
        Assert.Equal("The recording holds no AAC audio track.", reason);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ' })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })]
    [InlineData(new byte[] { 0, 0, 0, 0x10, (byte)'m', (byte)'o', (byte)'o', (byte)'v', 0, 0, 0, 0x30, (byte)'t', (byte)'r', (byte)'a', (byte)'k' })]
    public void A_file_that_is_not_an_mp4_answers_a_reason_without_throwing(byte[] file)
    {
        Assert.False(Mp4AacReader.TryRead(file, out var track, out var reason));
        Assert.Null(track);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void A_64_bit_box_size_past_the_end_answers_a_reason_without_throwing()
    {
        var file = Mp4.Concat(Mp4.Ftyp(), Mp4.U32(1), Encoding.ASCII.GetBytes("free"), Enumerable.Repeat((byte)0xFF, 8).ToArray());

        Assert.False(Mp4AacReader.TryRead(file, out _, out var reason));
        Assert.Equal(Mp4AacReader.NotMp4, reason);
    }

    [Fact]
    public void A_chunk_offset_near_the_64_bit_limit_reads_as_a_tear_without_throwing()
    {
        var file = Classic(delta: 1024, chunkOffset: 0x7FFF_FFFF_FFFF_FFFF);

        Assert.Empty(Read(file).Frames);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(48000u * 301)]
    [InlineData(0xFFFF_FFFFu)]
    public void A_frame_with_no_length_or_longer_than_a_part_answers_a_reason(uint delta)
    {
        Assert.False(Mp4AacReader.TryRead(Classic(delta, chunkOffset: null), out _, out var reason));
        Assert.Equal(Mp4AacReader.NotMp4, reason);
    }

    [Fact]
    public void A_fragment_default_duration_of_zero_answers_a_reason()
    {
        // trex and tfhd name no duration, and the trun names none per sample.
        var payloads = Enumerable.Range(0, 2).Select(Payload).ToArray();
        byte[] Moof(int dataOffset) => Mp4.Box("moof",
            Mp4.Box("traf",
                Mp4.FullBox("tfhd", 0, 0x020000, Mp4.U32(1)),
                Mp4.FullBox("trun", 0, 0x000201, Mp4.U32(2), Mp4.U32((uint)dataOffset), Mp4.U32((uint)payloads[0].Length), Mp4.U32((uint)payloads[1].Length))));

        var file = Mp4.Concat(Mp4.Ftyp(), FragmentedMoov(), Moof(Moof(0).Length + 8), Mp4.Box("mdat", payloads.SelectMany(p => p).ToArray()));

        Assert.False(Mp4AacReader.TryRead(file, out _, out _));
    }

    [Fact]
    public void An_audio_traf_after_another_tracks_traf_reads_from_where_that_data_ended()
    {
        // No base flag on either traf: the audio's data follows the video's (ISO/IEC 14496-12 §8.8.7).
        var video = new[] { Enumerable.Repeat((byte)0xEE, 30).ToArray() };
        var audio = Enumerable.Range(0, 2).Select(Payload).ToArray();

        byte[] Moof(int dataOffset) => Mp4.Box("moof",
            Mp4.Box("traf",
                Mp4.FullBox("tfhd", 0, 0x000008, Mp4.U32(2), Mp4.U32(3000)),
                Mp4.FullBox("trun", 0, 0x000201, Mp4.U32(1), Mp4.U32((uint)dataOffset), Mp4.U32(30))),
            Mp4.Box("traf",
                Mp4.FullBox("tfhd", 0, 0x000008, Mp4.U32(1), Mp4.U32(1024)),
                Mp4.FullBox("trun", 0, 0x000200, Mp4.U32(2), Mp4.U32((uint)audio[0].Length), Mp4.U32((uint)audio[1].Length))));

        var body = video[0].Concat(audio.SelectMany(p => p)).ToArray();
        var file = Mp4.Concat(Mp4.Ftyp(), FragmentedMoov(), Moof(Moof(0).Length + 8), Mp4.Box("mdat", body));

        Assert.Equal(audio, Read(file).Frames.Select(frame => file[frame.Bytes]));
    }

    [Fact]
    public void The_channel_count_comes_from_the_audio_specific_config()
    {
        // A QuickTime v2 sound description holds a placeholder 3 where the count would be.
        var file = Mp4.Concat(Mp4.Ftyp(), Mp4.Moov(Mp4.Trak(48000, Asc48kMono, Mp4.EmptyTables(), hdlr: "soun", channels: 3)));

        Assert.Equal(1, Read(file).Config.Channels);
    }

    [Fact]
    public void A_fragmented_m4a_is_cut_into_parts_with_offsets_from_its_timescale()
    {
        // 48 kHz at 1024 samples a frame: a one-second part holds 46 frames, so 100 make 46 + 46 + 8.
        var fragments = Enumerable.Range(0, 10).Select(f => Enumerable.Range(f * 10, 10).Select(Payload).ToArray()).ToArray();
        var file = Fragmented(fragments);

        var parts = AacRemuxer.Remux(file, Read(file), TimeSpan.FromSeconds(1));

        Assert.Equal(3, parts.Count);
        Assert.Equal([46, 46, 8], parts.Select(part => Read(part.M4a).Frames.Count));
        Assert.Equal(TimeSpan.Zero, parts[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(46 * 1024 / 48000.0), parts[1].Offset);
        Assert.Equal(TimeSpan.FromSeconds(92 * 1024 / 48000.0), parts[2].Offset);

        var written = parts.SelectMany(part => Read(part.M4a).Frames.Select(frame => part.M4a[frame.Bytes]));
        Assert.Equal(fragments.SelectMany(f => f), written);
    }

    private static AacTrack Read(byte[] file)
    {
        Assert.True(Mp4AacReader.TryRead(file, out var track, out var reason), reason);
        return track!;
    }

    /// <summary>A classic M4A of two 12-byte frames of <paramref name="delta"/> each, its one
    /// chunk at <paramref name="chunkOffset"/> (in co64), or at its mdat when null.</summary>
    internal static byte[] Classic(uint delta, ulong? chunkOffset)
    {
        var body = new byte[24];
        byte[] File(ulong offset) => Mp4.Concat(
            Mp4.Ftyp(),
            Mp4.Moov(Mp4.Trak(48000, Asc48kMono,
            [
                Mp4.FullBox("stts", 0, 0, Mp4.U32(1), Mp4.U32(2), Mp4.U32(delta)),
                Mp4.FullBox("stsc", 0, 0, Mp4.U32(1), Mp4.U32(1), Mp4.U32(2), Mp4.U32(1)),
                Mp4.FullBox("stsz", 0, 0, Mp4.U32(12), Mp4.U32(2)),
                Mp4.FullBox("co64", 0, 0, Mp4.U32(1), Mp4.U64(offset)),
            ],
            hdlr: "soun")),
            Mp4.Box("mdat", body));

        return File(chunkOffset ?? (ulong)(File(0).Length - body.Length));
    }

    private static byte[] Payload(int seed) =>
        Enumerable.Range(0, 20 + (seed % 7)).Select(i => (byte)(seed * 31 + i)).ToArray();

    /// <summary>A Chromium-shaped fragmented M4A: an empty sample table with <c>mvex</c>
    /// defaults, then one <c>moof</c>/<c>mdat</c> pair per fragment, the <c>trun</c> naming
    /// each sample's size and its data offset from the <c>moof</c>.</summary>
    private static byte[] Fragmented(byte[][][] fragments, bool lastMdatToEnd = false)
    {
        var parts = new List<byte[]> { Mp4.Ftyp(), FragmentedMoov() };
        for (var f = 0; f < fragments.Length; f++)
        {
            var payloads = fragments[f];
            var sequence = (uint)(f + 1);

            byte[] Moof(int dataOffset) => Mp4.Box("moof",
                Mp4.FullBox("mfhd", 0, 0, Mp4.U32(sequence)),
                Mp4.Box("traf",
                    Mp4.FullBox("tfhd", 0, 0x020008, Mp4.U32(1), Mp4.U32(1024)),
                    Mp4.FullBox("tfdt", 0, 0, Mp4.U32(0)),
                    Mp4.FullBox("trun", 0, 0x000201, Mp4.U32((uint)payloads.Length), Mp4.U32((uint)dataOffset),
                        Mp4.Concat(payloads.Select(p => Mp4.U32((uint)p.Length)).ToArray()))));

            var moof = Moof(Moof(0).Length + 8);
            var body = payloads.SelectMany(p => p).ToArray();
            var mdat = Mp4.Box("mdat", body);
            if (lastMdatToEnd && f == fragments.Length - 1) BinaryPrimitives.WriteUInt32BigEndian(mdat, 0);

            parts.Add(moof);
            parts.Add(mdat);
        }

        return Mp4.Concat(parts.ToArray());
    }

    private static byte[] FragmentedMoov() => Mp4.Moov(
        Mp4.Trak(48000, Asc48kMono, Mp4.EmptyTables(), hdlr: "soun"),
        Mp4.Box("mvex", Mp4.FullBox("trex", 0, 0, Mp4.U32(1), Mp4.U32(1), Mp4.U32(0), Mp4.U32(0), Mp4.U32(0))));

    private static byte[] AdtsStream(byte[][] payloads) => payloads.SelectMany(payload =>
    {
        var length = 7 + payload.Length;
        var header = new byte[]
        {
            0xFF, 0xF1, (byte)((1 << 6) | (8 << 2)), (byte)((1 << 6) | ((length >> 11) & 0x03)),
            (byte)((length >> 3) & 0xFF), (byte)(((length & 0x07) << 5) | 0x1F), 0xFC,
        };
        return header.Concat(payload);
    }).ToArray();

    private static byte[] Bits(params (int Value, int Width)[] fields)
    {
        var total = fields.Sum(field => field.Width);
        var bytes = new byte[(total + 7) / 8];
        var at = 0;
        foreach (var (value, width) in fields)
        {
            for (var bit = width - 1; bit >= 0; bit--, at++)
            {
                if (((value >> bit) & 1) == 1) bytes[at / 8] |= (byte)(0x80 >> (at % 8));
            }
        }

        return bytes;
    }

    /// <summary>Writes the MP4 boxes the fixtures are made of.</summary>
    private static class Mp4
    {
        public static byte[] Ftyp() => Box("ftyp", Ascii("M4A "), U32(0), Ascii("M4A "), Ascii("isom"));

        public static byte[] Moov(params byte[][] children) =>
            Box("moov", [FullBox("mvhd", 0, 0, new byte[96]), .. children]);

        public static byte[] Trak(int timescale, byte[] asc, byte[][] tables, string hdlr, uint trackId = 1, string sampleEntry = "mp4a", ushort channels = 1) =>
            Box("trak",
                FullBox("tkhd", 0, 3, U32(0), U32(0), U32(trackId), new byte[68]),
                Box("mdia",
                    FullBox("mdhd", 0, 0, U32(0), U32(0), U32((uint)timescale), U32(0), U16(0x55C4), U16(0)),
                    FullBox("hdlr", 0, 0, U32(0), Ascii(hdlr), new byte[12], Ascii("Handler\0")),
                    Box("minf",
                        Box("stbl",
                            FullBox("stsd", 0, 0, U32(1), SampleEntry(sampleEntry, asc, channels)),
                            Concat(tables)))));

        public static byte[][] EmptyTables() =>
        [
            FullBox("stts", 0, 0, U32(0)),
            FullBox("stsc", 0, 0, U32(0)),
            FullBox("stsz", 0, 0, U32(0), U32(0)),
            FullBox("stco", 0, 0, U32(0)),
        ];

        /// <summary>One chunk of <paramref name="payloads"/> at <paramref name="chunkOffset"/>.</summary>
        public static byte[][] Tables(byte[][] payloads, uint chunkOffset) =>
        [
            FullBox("stts", 0, 0, U32(1), U32((uint)payloads.Length), U32(1024)),
            FullBox("stsc", 0, 0, U32(1), U32(1), U32((uint)payloads.Length), U32(1)),
            FullBox("stsz", 0, 0, U32(0), U32((uint)payloads.Length), Concat(payloads.Select(p => U32((uint)p.Length)).ToArray())),
            FullBox("stco", 0, 0, U32(1), U32(chunkOffset)),
        ];

        private static byte[] SampleEntry(string type, byte[] asc, ushort channels)
        {
            byte[] Descriptor(byte tag, byte[] body) => Concat([tag, (byte)body.Length], body);

            var esds = FullBox("esds", 0, 0, Descriptor(0x03, Concat(
                U16(1), [0],
                Descriptor(0x04, Concat([0x40, 0x15, 0, 0, 0], U32(0), U32(0), Descriptor(0x05, asc))),
                Descriptor(0x06, [0x02]))));

            return Box(type, new byte[6], U16(1), new byte[8], U16(channels), U16(16), U16(0), U16(0), U32(48000u << 16), esds);
        }

        public static byte[] Box(string type, params byte[][] content)
        {
            var body = Concat(content);
            var box = new byte[8 + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Ascii(type).CopyTo(box, 4);
            body.CopyTo(box, 8);
            return box;
        }

        /// <summary>A box whose size is written in the 64-bit field after the type.</summary>
        public static byte[] LargeBox(string type, byte[] body) =>
            Concat(U32(1), Ascii(type), U64((ulong)(16 + body.Length)), body);

        public static byte[] FullBox(string type, byte version, int flags, params byte[][] content) =>
            Box(type, [[version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags], .. content]);

        public static byte[] U64(ulong value)
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            return bytes;
        }

        public static byte[] U32(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }

        public static byte[] U16(ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            return bytes;
        }

        public static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

        private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    }
}
