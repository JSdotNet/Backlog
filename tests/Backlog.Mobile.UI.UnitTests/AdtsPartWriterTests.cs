using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A meeting recording rolls over to a new part with no gap (local ADR 0048,
/// .devbook/domain/inbox/requirements.md#recordings-on-a-note): the ADTS stream the
/// microphone writes is cut only between frames, before a part would pass its cap, so the
/// parts played one after the other are the whole meeting.
/// </summary>
public sealed class AdtsPartWriterTests
{
    [Fact]
    public void The_part_cap_is_24_MiB()
    {
        Assert.Equal(25_165_824L, AdtsPartWriter.DefaultPartCap);
    }

    [Fact]
    public void The_parts_put_together_are_the_stream_and_none_passes_the_cap()
    {
        var stream = AdtsStream.Of(Enumerable.Range(0, 60).Select(i => 90 + (i * 37 % 160)));
        var parts = new PartSink();

        using (var writer = new AdtsPartWriter(parts.Open, partCap: 1_000))
        {
            foreach (var chunk in stream.Chunk(37)) writer.Write(chunk);
        }

        Assert.True(parts.Bytes.Count > 1);
        Assert.Equal(stream, parts.Bytes.SelectMany(part => part));
        Assert.All(parts.Bytes, part => Assert.InRange(part.Length, 1, 1_000));
        Assert.All(parts.Bytes, part => Assert.True(part[0] == 0xFF && (part[1] & 0xF0) == 0xF0, "Each part starts on a frame."));
    }

    [Fact]
    public void A_part_closes_only_when_the_next_frame_would_take_it_past_the_cap()
    {
        var stream = AdtsStream.Of([100, 100, 100, 100, 100]);
        var parts = new PartSink();

        IReadOnlyList<AdtsPart> finished;
        using (var writer = new AdtsPartWriter(parts.Open, partCap: 300))
        {
            writer.Write(stream);
            finished = writer.Complete();
        }

        // 300 is the cap, not past it: three frames fit, the fourth starts part 2.
        Assert.Equal([300, 200], parts.Bytes.Select(part => part.Length));
        Assert.Equal([1, 2], finished.Select(part => part.Number));
        Assert.Equal([300L, 200L], finished.Select(part => part.SizeBytes));
        Assert.Equal([3, 2], finished.Select(part => part.Frames));
    }

    [Fact]
    public void A_parts_duration_is_1024_samples_a_frame_at_16_kHz()
    {
        // 125 frames × 1024 samples / 16 000 Hz = 8 s.
        var stream = AdtsStream.Of(Enumerable.Repeat(64, 125));
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        var part = Assert.Single(writer.Complete());
        Assert.Equal(TimeSpan.FromSeconds(8), part.Duration);
        Assert.Equal(TimeSpan.FromSeconds(8), writer.Duration);
    }

    [Fact]
    public void A_frame_split_across_writes_is_kept_whole()
    {
        var stream = AdtsStream.Of([120, 80, 200, 50, 150]);
        var parts = new PartSink();

        IReadOnlyList<AdtsPart> finished;
        using (var writer = new AdtsPartWriter(parts.Open, partCap: 250))
        {
            foreach (var single in stream) writer.Write([single]);
            finished = writer.Complete();
        }

        // 120 + 80 | 200 + 50 (exactly the cap) | 150.
        Assert.Equal([200, 250, 150], parts.Bytes.Select(part => part.Length));
        Assert.Equal([2, 2, 1], finished.Select(part => part.Frames));
        Assert.Equal(stream, parts.Bytes.SelectMany(part => part));
    }

    [Fact]
    public void A_frame_cut_short_by_a_killed_write_stays_in_the_file_but_not_in_the_duration()
    {
        var whole = AdtsStream.Of([100, 100]);
        var cut = AdtsStream.Of([100]).Take(40);
        byte[] stream = [.. whole, .. cut];
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        var part = Assert.Single(writer.Complete());
        Assert.Equal(240L, part.SizeBytes);
        Assert.Equal(2, part.Frames);
        Assert.Equal(stream, Assert.Single(parts.Bytes));
    }

    [Fact]
    public void Nothing_written_opens_no_part()
    {
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);

        Assert.Empty(writer.Complete());
        Assert.Empty(parts.Bytes);
    }

    [Fact]
    public void Bytes_before_the_first_frame_are_kept_but_not_counted()
    {
        byte[] stream = [0x00, 0x13, 0xFF, 0x00, 0x42, .. AdtsStream.Of([100, 100, 100])];
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        Assert.Equal(3, Assert.Single(writer.Complete()).Frames);
        Assert.Equal(stream, Assert.Single(parts.Bytes));
    }

    [Fact]
    public void A_false_sync_word_before_the_first_frame_swallows_no_real_frame()
    {
        // A header-shaped run claiming 150 bytes, which would reach into the second real frame;
        // no frame follows it, so it is not one.
        byte[] stream = [0x00, 0xFF, 0xF1, 0x60, 0x40, 0x12, 0xDF, 0xFC, .. AdtsStream.Of([100, 100, 100])];
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        Assert.Equal(3, Assert.Single(writer.Complete()).Frames);
        Assert.Equal(stream, Assert.Single(parts.Bytes));
    }

    [Fact]
    public void A_sync_word_inside_a_frames_payload_is_not_a_frame()
    {
        var stream = AdtsStream.Of([100, 100, 100], plantSyncInPayload: true);
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open, partCap: 200);
        writer.Write(stream);

        Assert.Equal([2, 1], writer.Complete().Select(part => part.Frames));
        Assert.Equal([200, 100], parts.Bytes.Select(part => part.Length));
    }

    [Fact]
    public void A_frame_with_a_CRC_is_a_frame()
    {
        var stream = AdtsStream.Of([100, 100, 100], crc: true);
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        Assert.Equal(3, Assert.Single(writer.Complete()).Frames);
    }

    [Fact]
    public void A_header_with_its_layer_bits_set_is_not_a_frame()
    {
        var stream = AdtsStream.Of([100, 100, 100]);
        foreach (var start in new[] { 0, 100, 200 }) stream[start + 1] = 0xF7;
        var parts = new PartSink();

        using var writer = new AdtsPartWriter(parts.Open);
        writer.Write(stream);

        Assert.Equal(0, Assert.Single(writer.Complete()).Frames);
        Assert.Equal(stream, Assert.Single(parts.Bytes));
    }

    [Fact]
    public void When_the_next_part_cannot_be_opened_the_parts_written_are_still_handed_back()
    {
        var stream = AdtsStream.Of([100, 100, 100, 100, 100]);
        var parts = new PartSink { FailFrom = 2 };

        using var writer = new AdtsPartWriter(parts.Open, partCap: 300);
        Assert.Throws<IOException>(() => writer.Write(stream));
        writer.Write(AdtsStream.Of([100]));

        var part = Assert.Single(writer.Complete());
        Assert.Equal(1, part.Number);
        Assert.Equal(300L, part.SizeBytes);
        Assert.Equal(3, part.Frames);
        Assert.Equal(stream[..300], Assert.Single(parts.Bytes));
    }

    [Fact]
    public void When_a_part_file_fails_mid_write_complete_still_hands_back_what_was_written()
    {
        var stream = AdtsStream.Of([100, 100, 100]);
        var full = new FullDisk(capacity: 150);

        using var writer = new AdtsPartWriter(_ => full);
        Assert.Throws<IOException>(() => writer.Write(stream));

        var part = Assert.Single(writer.Complete());
        Assert.Equal(100L, part.SizeBytes);
        Assert.Equal(1, part.Frames);
        Assert.True(full.Disposed);
    }

    /// <summary>A part file on a disk that fills after <c>capacity</c> bytes: every write
    /// past it, and every flush after that, fails.</summary>
    private sealed class FullDisk(int capacity) : MemoryStream
    {
        private bool _full;

        public bool Disposed { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_full || Length + count > capacity)
            {
                _full = true;
                throw new IOException("There is not enough space on the disk.");
            }

            base.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            if (_full) throw new IOException("There is not enough space on the disk.");
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>The part files, in memory, as the writer opened them.</summary>
    private sealed class PartSink
    {
        private readonly List<MemoryStream> _opened = [];

        public List<byte[]> Bytes => [.. _opened.Select(stream => stream.ToArray())];

        /// <summary>The first part that cannot be opened, as on a full disk.</summary>
        public int? FailFrom { get; init; }

        public Stream Open(int number)
        {
            Assert.Equal(_opened.Count + 1, number);
            if (number >= FailFrom) throw new IOException("There is not enough space on the disk.");

            var stream = new MemoryStream();
            _opened.Add(stream);
            return stream;
        }
    }
}

/// <summary>A synthetic ADTS stream: AAC-LC, 16 kHz, mono, one raw block a frame — what the
/// phone's <c>MediaRecorder</c> writes — with no CRC unless asked.</summary>
internal static class AdtsStream
{
    public static byte[] Of(IEnumerable<int> frameLengths, bool crc = false, bool plantSyncInPayload = false) =>
        [.. frameLengths.SelectMany((length, index) => Frame(length, index, crc, plantSyncInPayload))];

    public static byte[] Frame(int length, int seed = 0, bool crc = false, bool plantSyncInPayload = false)
    {
        const int sampleRateIndex16kHz = 8;
        const int mono = 1;

        var frame = new byte[length];
        frame[0] = 0xFF;
        frame[1] = crc ? (byte)0xF0 : (byte)0xF1;
        frame[2] = (byte)((1 << 6) | (sampleRateIndex16kHz << 2) | (mono >> 2));
        frame[3] = (byte)(((mono & 0x3) << 6) | ((length >> 11) & 0x3));
        frame[4] = (byte)((length >> 3) & 0xFF);
        frame[5] = (byte)(((length & 0x7) << 5) | 0x1F);
        frame[6] = 0xFC;

        var payload = crc ? 9 : 7;

        // Payload that never looks like a sync word, so a test's bytes are unambiguous.
        for (var i = payload; i < length; i++) frame[i] = (byte)((i * 7 + seed) & 0x7F);

        // Unless asked for one: a header-shaped run in the middle of the audio.
        if (plantSyncInPayload) Array.Copy(new byte[] { 0xFF, 0xF1, 0x60, 0x40, 0x04, 0x1F, 0xFC }, 0, frame, payload + 10, 7);

        return frame;
    }
}
