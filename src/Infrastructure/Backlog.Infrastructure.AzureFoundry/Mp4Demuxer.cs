using System.Buffers.Binary;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Lifts the AAC frames out of an MP4 or M4A recording and cuts them into M4A parts of at
/// most a given length, as <see cref="AdtsRemuxer"/> does for ADTS (local ADR 0049 §3): the
/// browser harness records one M4A with no rollover, which may run past the synchronous
/// limit. Nothing is decoded; the frames and the AudioSpecificConfig are carried as they are.
/// <para>
/// Two layouts, read alike: a plain file, whose frames the sample table names
/// (<c>moov/trak/mdia/minf/stbl</c>: <c>stsd</c>, <c>stts</c>, <c>stsz</c>, <c>stsc</c>,
/// <c>stco</c> or <c>co64</c>), and a fragmented one, as a browser's MediaRecorder writes,
/// whose frames each <c>moof/traf</c> names (<c>tfhd</c>, <c>trun</c>, with the <c>trex</c>
/// defaults). The first sound track with an <c>mp4a</c> entry is the one read.
/// </para>
/// <para>
/// A cut-off file keeps its frames up to the last whole one. A file that holds no readable
/// AAC track — no <c>moov</c>, no <c>mp4a</c>, no frame — remuxes to nothing, which the
/// caller reports as a recording it could not read.
/// </para>
/// </summary>
public static class Mp4Demuxer
{
    /// <summary>AAC-LC codes 1024 samples a frame: the length a frame with no duration of its
    /// own is given.</summary>
    private const int SamplesPerFrame = 1024;

    private static readonly int[] SampleRates =
        [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    /// <summary>Whether <paramref name="file"/> opens with an MP4 box — <c>ftyp</c>, or a
    /// <c>moov</c> or <c>mdat</c> a writer put first.</summary>
    public static bool LooksLikeMp4(ReadOnlySpan<byte> file) =>
        file.Length >= 8 && Type(file, 4) is "ftyp" or "moov" or "mdat" or "styp";

    /// <summary>Splits and remuxes <paramref name="mp4"/>. Empty when the file holds no
    /// readable AAC track.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partLength"/> is shorter
    /// than a frame.</exception>
    public static IReadOnlyList<RemuxedPart> Remux(ReadOnlySpan<byte> mp4, TimeSpan? partLength = null)
    {
        var track = Read(mp4);
        return track is null ? [] : M4aParts.Cut(mp4, track, partLength ?? AdtsRemuxer.DefaultPartLength);
    }

    /// <summary>The file's AAC track, or null when it has none that can be read.</summary>
    internal static AacTrack? Read(ReadOnlySpan<byte> file)
    {
        if (!LooksLikeMp4(file)) return null;

        List<Box> top;
        Box moov;
        Audio? audio;
        try
        {
            top = Boxes(file, 0, file.Length);
            if (Find(top, "moov") is not { } found) return null;
            moov = found;
            audio = AudioTrack(file, moov);
        }
        catch (Exception failure) when (IsMalformed(failure))
        {
            // A box that claims more than it holds: the track cannot be trusted.
            return null;
        }

        if (audio is null) return null;

        var frames = new List<Range>();
        var durations = new List<uint>();
        var track = new Durations(audio);

        try
        {
            SampleTable(file, audio, frames, durations, track);
            Fragments(file, top, moov, audio, frames, durations, track);
        }
        catch (Exception failure) when (IsMalformed(failure))
        {
            // A cut-off file: what was read before the tear is whole, and that is what is kept.
        }

        return frames.Count == 0
            ? null
            : new AacTrack(audio.Config, audio.SampleRate, audio.Channels, audio.Timescale, frames, durations);
    }

    /// <summary>What reading past a box's end throws: a slice out of range, or an index.</summary>
    private static bool IsMalformed(Exception failure) => failure is ArgumentOutOfRangeException or IndexOutOfRangeException;

    /// <summary>
    /// Frame lengths a cut can trust. An AAC frame lasts 1024 or 2048 samples, never a
    /// second, so a duration of zero or past one second of the track is a corrupt table and
    /// the frame is given an AAC-LC frame's length instead; a part can then always hold it.
    /// </summary>
    private sealed class Durations(Audio audio)
    {
        private readonly uint _fallback = (uint)Math.Clamp((long)SamplesPerFrame * audio.Timescale / audio.SampleRate, 1, audio.Timescale);

        public uint Of(uint duration) => duration > 0 && duration <= audio.Timescale ? duration : _fallback;
    }

    /// <summary>The first sound track whose sample entry is <c>mp4a</c> with an esds.</summary>
    private static Audio? AudioTrack(ReadOnlySpan<byte> file, Box moov)
    {
        foreach (var trak in Children(file, moov))
        {
            if (trak.Type != "trak") continue;

            var tkhd = Child(file, trak, "tkhd");
            var mdia = Child(file, trak, "mdia");
            if (tkhd is null || mdia is null) continue;

            var hdlr = Child(file, mdia.Value, "hdlr");
            if (hdlr is null || Type(file, hdlr.Value.Body + 8) != "soun") continue;

            var mdhd = Child(file, mdia.Value, "mdhd");
            var stbl = Child(file, mdia.Value, "minf") is { } minf ? Child(file, minf, "stbl") : null;
            if (mdhd is null || stbl is null) continue;

            var stsd = Child(file, stbl.Value, "stsd");
            if (stsd is null) continue;

            // stsd: version and flags, an entry count, then the entries.
            var entries = Boxes(file, stsd.Value.Body + 8, stsd.Value.End);
            if (Find(entries, "mp4a") is not { } mp4a) continue;

            var entry = SampleEntry(file, mp4a);
            if (entry is null) continue;

            var trackId = file[tkhd.Value.Body] == 1 ? U32(file, tkhd.Value.Body + 20) : U32(file, tkhd.Value.Body + 12);
            var timescale = (int)U32(file, mdhd.Value.Body + (file[mdhd.Value.Body] == 1 ? 20 : 12));
            var (config, sampleRate, channels) = entry.Value;

            return new Audio(trackId, config, sampleRate, channels, timescale > 0 ? timescale : sampleRate, stbl.Value);
        }

        return null;
    }

    /// <summary>The AudioSpecificConfig of an mp4a entry, with the rate and channels it says
    /// (or the entry's own fields where it says none).</summary>
    private static (byte[] Config, int SampleRate, int Channels)? SampleEntry(ReadOnlySpan<byte> file, Box mp4a)
    {
        // An audio sample entry: 6 reserved, a data reference index, then a version that says
        // how long the rest of its fixed fields are (0 for ISO, 1 and 2 for QuickTime).
        var at = mp4a.Body;
        var version = U16(file, at + 8);
        var channelsField = U16(file, at + 16);
        var rateField = (int)(U32(file, at + 24) >> 16);
        var childrenAt = at + 28 + (version switch { 1 => 16, 2 => 36, _ => 0 });

        var esds = Find(Boxes(file, childrenAt, mp4a.End), "esds");
        if (esds is null) return null;

        var config = AudioSpecificConfig(file[(esds.Value.Body + 4)..esds.Value.End]);
        if (config is null || config.Length == 0) return null;

        var (rate, channels) = Parse(config);
        rate = rate > 0 ? rate : rateField;
        channels = channels > 0 ? channels : channelsField;
        return rate > 0 ? (config, rate, Math.Max(1, channels)) : null;
    }

    /// <summary>The DecoderSpecificInfo (tag 5) inside the ES_Descriptor (tag 3) and its
    /// DecoderConfigDescriptor (tag 4).</summary>
    private static byte[]? AudioSpecificConfig(ReadOnlySpan<byte> descriptors)
    {
        var at = 0;
        if (!Descriptor(descriptors, ref at, 0x03, out _)) return null;

        // ES_ID, then the flags that say which optional fields follow.
        var flags = descriptors[at + 2];
        at += 3;
        if ((flags & 0x80) != 0) at += 2;
        if ((flags & 0x40) != 0) at += 1 + descriptors[at];
        if ((flags & 0x20) != 0) at += 2;

        if (!Descriptor(descriptors, ref at, 0x04, out _)) return null;

        // objectTypeIndication, streamType, bufferSizeDB, maxBitrate, avgBitrate.
        at += 13;
        if (!Descriptor(descriptors, ref at, 0x05, out var length)) return null;

        return descriptors.Slice(at, length).ToArray();
    }

    /// <summary>Reads a descriptor's tag and its length, of one to four bytes, leaving
    /// <paramref name="at"/> at its body.</summary>
    private static bool Descriptor(ReadOnlySpan<byte> descriptors, ref int at, byte tag, out int length)
    {
        length = 0;
        if (descriptors[at] != tag) return false;
        at++;

        for (var i = 0; i < 4; i++)
        {
            var b = descriptors[at++];
            length = (length << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) break;
        }

        return true;
    }

    /// <summary>The sample rate and channel configuration an AudioSpecificConfig says; zero
    /// for what it leaves to elsewhere.</summary>
    private static (int SampleRate, int Channels) Parse(byte[] config)
    {
        var reader = new Bits(config);
        if (reader.Read(5) == 31) reader.Read(6);

        var index = reader.Read(4);
        var rate = index == 15 ? reader.Read(24) : index < SampleRates.Length ? SampleRates[index] : 0;
        var channels = reader.Read(4);
        return (rate, channels);
    }

    /// <summary>The frames a plain file's sample table names, in order.</summary>
    private static void SampleTable(ReadOnlySpan<byte> file, Audio audio, List<Range> frames, List<uint> durations, Durations track)
    {
        var stsz = Child(file, audio.SampleTable, "stsz");
        var stsc = Child(file, audio.SampleTable, "stsc");
        var stco = Child(file, audio.SampleTable, "stco");
        var co64 = Child(file, audio.SampleTable, "co64");
        if (stsz is null || stsc is null || (stco ?? co64) is null) return;

        var constantSize = U32(file, stsz.Value.Body + 4);
        var sampleCount = (long)U32(file, stsz.Value.Body + 8);
        if (sampleCount == 0) return;
        if (constantSize == 0) sampleCount = Math.Min(sampleCount, (stsz.Value.End - stsz.Value.Body - 12) / 4);

        var chunks = new List<long>();
        if (stco is { } chunkOffsets)
        {
            var count = Math.Min(U32(file, chunkOffsets.Body + 4), (uint)((chunkOffsets.End - chunkOffsets.Body - 8) / 4));
            for (var i = 0; i < count; i++) chunks.Add(U32(file, chunkOffsets.Body + 8 + (4 * i)));
        }
        else
        {
            var wide = co64!.Value;
            var count = Math.Min(U32(file, wide.Body + 4), (uint)((wide.End - wide.Body - 8) / 8));
            for (var i = 0; i < count; i++) chunks.Add((long)BinaryPrimitives.ReadUInt64BigEndian(file[(wide.Body + 8 + (8 * i))..]));
        }

        var runs = new List<(uint FirstChunk, uint Samples)>();
        var runCount = Math.Min(U32(file, stsc.Value.Body + 4), (uint)((stsc.Value.End - stsc.Value.Body - 8) / 12));
        for (var i = 0; i < runCount; i++)
        {
            runs.Add((U32(file, stsc.Value.Body + 8 + (12 * i)), U32(file, stsc.Value.Body + 12 + (12 * i))));
        }

        var deltas = TimeToSample(file, audio.SampleTable, sampleCount);

        long sample = 0;
        var run = -1;
        for (var chunk = 0; chunk < chunks.Count && sample < sampleCount; chunk++)
        {
            // The runs are in chunk order: move on to the last one that starts at or before
            // this chunk, without scanning from the first again.
            while (run + 1 < runs.Count && runs[run + 1].FirstChunk <= chunk + 1) run++;
            var perChunk = run >= 0 ? runs[run].Samples : 0;
            var offset = chunks[chunk];
            for (var k = 0; k < perChunk && sample < sampleCount; k++, sample++)
            {
                var size = constantSize != 0 ? constantSize : U32(file, stsz.Value.Body + 12 + (int)(4 * sample));
                if (size == 0 || offset < 0 || offset > file.Length - size || frames.Count >= file.Length) return;

                frames.Add(new Range((int)offset, (int)(offset + size)));
                durations.Add(track.Of(sample < deltas.Count ? deltas[(int)sample] : 0));
                offset += size;
            }
        }
    }

    /// <summary>The stts durations, one per sample, as far as the table runs and no further
    /// than the <paramref name="samples"/> the stsz names.</summary>
    private static List<uint> TimeToSample(ReadOnlySpan<byte> file, Box stbl, long samples)
    {
        var deltas = new List<uint>();
        if (Child(file, stbl, "stts") is not { } stts) return deltas;

        var count = Math.Min(U32(file, stts.Body + 4), (uint)((stts.End - stts.Body - 8) / 8));
        for (var i = 0; i < count; i++)
        {
            var run = U32(file, stts.Body + 8 + (8 * i));
            var delta = U32(file, stts.Body + 12 + (8 * i));

            // No more than the samples the stsz names, nor than the file has bytes for.
            for (var s = 0u; s < run && deltas.Count < samples && deltas.Count < file.Length; s++) deltas.Add(delta);
        }

        return deltas;
    }

    /// <summary>The frames every movie fragment names for the track, in file order.</summary>
    private static void Fragments(ReadOnlySpan<byte> file, List<Box> top, Box moov, Audio audio, List<Range> frames, List<uint> durations, Durations track)
    {
        uint trexDuration = 0, trexSize = 0;
        if (Child(file, moov, "mvex") is { } mvex)
        {
            foreach (var trex in Children(file, mvex))
            {
                if (trex.Type != "trex" || U32(file, trex.Body + 4) != audio.TrackId) continue;
                trexDuration = U32(file, trex.Body + 12);
                trexSize = U32(file, trex.Body + 16);
            }
        }

        foreach (var moof in top)
        {
            if (moof.Type != "moof") continue;

            // Without an explicit base or default-base-is-moof, a traf's data follows the data
            // of the one before it in the moof; the first starts at the moof (ISO/IEC 14496-12).
            long previousEnd = moof.Start;
            foreach (var traf in Children(file, moof))
            {
                if (traf.Type != "traf" || Child(file, traf, "tfhd") is not { } tfhd) continue;

                var flags = U24(file, tfhd.Body + 1);
                if (U32(file, tfhd.Body + 4) != audio.TrackId) continue;

                var at = tfhd.Body + 8;
                var baseOffset = (flags & 0x020000) != 0 ? moof.Start : previousEnd;
                if ((flags & 0x000001) != 0) { baseOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(file[at..]); at += 8; }
                if ((flags & 0x000002) != 0) at += 4;
                var defaultDuration = trexDuration;
                var defaultSize = trexSize;
                if ((flags & 0x000008) != 0) { defaultDuration = U32(file, at); at += 4; }
                if ((flags & 0x000010) != 0) defaultSize = U32(file, at);

                var dataAt = baseOffset;
                foreach (var trun in Children(file, traf))
                {
                    if (trun.Type != "trun") continue;
                    if (!Run(file, trun, baseOffset, ref dataAt, defaultDuration, defaultSize, track, frames, durations)) return;
                }

                previousEnd = dataAt;
            }
        }
    }

    /// <summary>One trun's frames. False when a frame runs past the file, so nothing after
    /// it is read.</summary>
    private static bool Run(
        ReadOnlySpan<byte> file,
        Box trun,
        long baseOffset,
        ref long dataAt,
        uint defaultDuration,
        uint defaultSize,
        Durations track,
        List<Range> frames,
        List<uint> durations)
    {
        var flags = U24(file, trun.Body + 1);
        var count = (long)U32(file, trun.Body + 4);
        var at = trun.Body + 8;
        if ((flags & 0x000001) != 0) { dataAt = baseOffset + BinaryPrimitives.ReadInt32BigEndian(file[at..]); at += 4; }
        if ((flags & 0x000004) != 0) at += 4;

        var hasDuration = (flags & 0x000100) != 0;
        var hasSize = (flags & 0x000200) != 0;
        var perSample = 4 * ((hasDuration ? 1 : 0) + (hasSize ? 1 : 0) + ((flags & 0x000400) != 0 ? 1 : 0) + ((flags & 0x000800) != 0 ? 1 : 0));
        if (perSample > 0) count = Math.Min(count, (trun.End - at) / perSample);

        for (long i = 0; i < count; i++)
        {
            var duration = defaultDuration;
            var size = defaultSize;
            var field = at;
            if (hasDuration) { duration = U32(file, field); field += 4; }
            if (hasSize) size = U32(file, field);
            at += perSample;

            if (size == 0 || dataAt < 0 || dataAt > file.Length - size || frames.Count >= file.Length) return false;

            frames.Add(new Range((int)dataAt, (int)(dataAt + size)));
            durations.Add(track.Of(duration));
            dataAt += size;
        }

        return true;
    }

    /// <summary>The boxes between <paramref name="start"/> and <paramref name="end"/>. A box
    /// that claims past the end is cut to it, so a torn file's last box is still read as far
    /// as it goes; a header that does not fit, or a size that is not one, ends the list. Each
    /// box moves the read on by at least its header, so the list always ends.</summary>
    private static List<Box> Boxes(ReadOnlySpan<byte> file, int start, int end)
    {
        var boxes = new List<Box>();
        var at = start;
        while (at + 8 <= end)
        {
            long size = U32(file, at);
            var header = 8;
            if (size == 1)
            {
                if (at + 16 > end) break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(file[(at + 8)..]);
                header = 16;
            }
            else if (size == 0)
            {
                size = end - at;
            }

            // A 64-bit size past the end — or past long.MaxValue, read as negative — is cut to
            // the end before it is added to anything, so it cannot wrap the read backwards.
            if (size < header) break;
            size = Math.Min(size, end - at);
            if (size < header) break;

            var boxEnd = at + (int)size;
            boxes.Add(new Box(Type(file, at + 4), at, at + header, boxEnd));
            at = boxEnd;
        }

        return boxes;
    }

    private static List<Box> Children(ReadOnlySpan<byte> file, Box parent) => Boxes(file, parent.Body, parent.End);

    private static Box? Child(ReadOnlySpan<byte> file, Box parent, string type) => Find(Children(file, parent), type);

    private static Box? Find(List<Box> boxes, string type)
    {
        foreach (var box in boxes)
        {
            if (box.Type == type) return box;
        }

        return null;
    }

    private static string Type(ReadOnlySpan<byte> file, int at) => Encoding.ASCII.GetString(file.Slice(at, 4));

    private static uint U32(ReadOnlySpan<byte> file, int at) => BinaryPrimitives.ReadUInt32BigEndian(file[at..]);

    private static ushort U16(ReadOnlySpan<byte> file, int at) => BinaryPrimitives.ReadUInt16BigEndian(file[at..]);

    private static int U24(ReadOnlySpan<byte> file, int at) => (file[at] << 16) | (file[at + 1] << 8) | file[at + 2];

    /// <summary>One box: its type, where it starts, where its body starts, and where it ends.</summary>
    private readonly record struct Box(string Type, int Start, int Body, int End);

    /// <summary>The track read from the moov, and its sample table.</summary>
    private sealed record Audio(uint TrackId, byte[] Config, int SampleRate, int Channels, int Timescale, Box SampleTable);

    /// <summary>Reads an AudioSpecificConfig most significant bit first; past its end, zeros.</summary>
    private ref struct Bits(byte[] bytes)
    {
        private int _bit;

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++, _bit++)
            {
                var index = _bit / 8;
                var bit = index < bytes.Length ? (bytes[index] >> (7 - (_bit % 8))) & 1 : 0;
                value = (value << 1) | bit;
            }

            return value;
        }
    }
}
