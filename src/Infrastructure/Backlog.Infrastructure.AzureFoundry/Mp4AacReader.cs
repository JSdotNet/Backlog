using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Reads the AAC frames out of an MP4 or M4A file so <see cref="AacRemuxer"/> can cut them
/// like an ADTS stream's (local ADR 0049 §3). The track is the first sound track
/// (<c>hdlr</c> <c>soun</c>) whose sample entry is <c>mp4a</c> carrying AAC; its frames come
/// from the classic sample table (<c>stsz</c>, <c>stsc</c>, <c>stco</c> or <c>co64</c>,
/// <c>stts</c>), from the movie fragments behind it (<c>moof</c>/<c>trun</c>, as Chromium's
/// MediaRecorder writes), or both, in that order.
/// <para>
/// A cut-off file reads up to its last whole frame. A file whose boxes do not hold an AAC
/// track answers a reason a person can read, never an exception.
/// </para>
/// </summary>
internal static class Mp4AacReader
{
    internal const string NotMp4 = "The recording could not be read as an M4A file.";
    internal const string NoAacTrack = "The recording holds no AAC audio track.";

    /// <summary>Reads the AAC track of <paramref name="file"/>; false with a reason when the
    /// file holds none.</summary>
    internal static bool TryRead(ReadOnlySpan<byte> file, [NotNullWhen(true)] out AacTrack? track, [NotNullWhen(false)] out string? reason)
    {
        track = null;
        try
        {
            var top = Children(file, 0, file.Length);
            if (First(top, "moov") is not { } moov)
            {
                reason = NotMp4;
                return false;
            }

            if (ChooseTrack(file, moov) is not { } chosen)
            {
                reason = First(Children(file, moov.BodyStart, moov.End), "trak") is null ? NotMp4 : NoAacTrack;
                return false;
            }

            // A torn file keeps the frames before the tear, so a table that ends there
            // leaves no fragment to read after it.
            var frames = new List<AacFrame>();
            if (ReadSampleTable(file, chosen.Stbl, frames)) ReadFragments(file, top, moov, chosen.TrackId, frames);

            // An AAC frame codes at most 2048 samples, a fraction of a second at any rate. One
            // of no length, or of more than a second, is not a frame: it would defeat the cut
            // into parts, holding a whole recording in one part or outlasting a part on its own.
            if (frames.Exists(frame => frame.Duration <= 0 || frame.Duration > chosen.Timescale))
            {
                reason = NotMp4;
                return false;
            }

            track = new AacTrack(chosen.Config, chosen.Timescale, frames);
            reason = null;
            return true;
        }
        catch (MalformedException)
        {
            reason = NotMp4;
            return false;
        }
    }

    private static (AacConfig Config, int Timescale, uint TrackId, Box Stbl)? ChooseTrack(ReadOnlySpan<byte> file, Box moov)
    {
        foreach (var trak in Children(file, moov.BodyStart, moov.End).Where(box => box.Type == "trak"))
        {
            if (Child(file, trak, "mdia") is not { } mdia) continue;
            if (Child(file, mdia, "hdlr") is not { } hdlr || Ascii(file, hdlr.BodyStart + 8, hdlr.End) != "soun") continue;
            if (Child(file, mdia, "mdhd") is not { } mdhd || Child(file, mdia, "minf") is not { } minf) continue;
            if (Child(file, minf, "stbl") is not { } stbl || Child(file, stbl, "stsd") is not { } stsd) continue;

            if (SampleEntryConfig(file, stsd) is not { } config) continue;

            var timescale = MdhdTimescale(file, mdhd);
            if (timescale <= 0) continue;

            var trackId = Child(file, trak, "tkhd") is { } tkhd ? TkhdTrackId(file, tkhd) : 0;
            return (config, timescale, trackId, stbl);
        }

        return null;
    }

    /// <summary>The configuration of an <c>mp4a</c> first sample entry carrying AAC; null for
    /// any other entry.</summary>
    private static AacConfig? SampleEntryConfig(ReadOnlySpan<byte> file, Box stsd)
    {
        // stsd: version and flags, entry count, then the entries as boxes.
        var entries = Children(file, stsd.BodyStart + 8, stsd.End);
        if (entries.Count == 0 || entries[0].Type != "mp4a") return null;

        var mp4a = entries[0];
        var body = mp4a.BodyStart;
        Need(file, body + 28, mp4a.End);

        // A QuickTime sound description of version 1 or 2 carries more fields before its boxes.
        var version = BinaryPrimitives.ReadUInt16BigEndian(file[(body + 8)..]);
        var channels = BinaryPrimitives.ReadUInt16BigEndian(file[(body + 16)..]);
        var boxes = body + 28 + version switch { 1 => 16, 2 => 36, _ => 0 };
        if (boxes > mp4a.End) return null;

        // A QuickTime file may wrap the esds in a wave box.
        var children = Children(file, boxes, mp4a.End);
        var esds = First(children, "esds")
            ?? (First(children, "wave") is { } wave ? First(Children(file, wave.BodyStart, wave.End), "esds") : null);

        if (esds is null || AudioSpecificConfig(file, esds.Value) is not { } asc) return null;

        // The ASC's channel configuration is the count a decoder uses; the entry's field is a
        // placeholder in a QuickTime v2 description, so it stands in only for a configuration
        // of 0, which defers to a program config element.
        if (ReadAudioSpecificConfig(asc) is not { } read) return null;

        var count = read.Channels switch
        {
            >= 1 and <= 6 => read.Channels,
            7 => 8, // 7.1
            _ => channels,
        };

        return new AacConfig(read.SampleRate, count, asc);
    }

    /// <summary>The DecoderSpecificInfo of an AAC elementary stream; null when the stream is
    /// not MPEG-4 or MPEG-2 AAC.</summary>
    private static byte[]? AudioSpecificConfig(ReadOnlySpan<byte> file, Box esds)
    {
        var at = esds.BodyStart + 4;
        var end = esds.End;

        if (!Descriptor(file, ref at, end, out var tag, out var esEnd) || tag != 0x03) return null;
        Need(file, at + 3, esEnd);
        var flags = file[at + 2];
        at += 3;
        if ((flags & 0x80) != 0) at += 2;
        if ((flags & 0x40) != 0)
        {
            Need(file, at + 1, esEnd);
            at += 1 + file[at];
        }

        if ((flags & 0x20) != 0) at += 2;

        while (at < esEnd)
        {
            if (!Descriptor(file, ref at, esEnd, out tag, out var descriptorEnd)) return null;
            if (tag != 0x04)
            {
                at = descriptorEnd;
                continue;
            }

            Need(file, at + 13, descriptorEnd);
            var objectType = file[at];
            if (objectType is not (0x40 or 0x66 or 0x67 or 0x68)) return null;

            at += 13;
            while (at < descriptorEnd)
            {
                if (!Descriptor(file, ref at, descriptorEnd, out tag, out var infoEnd)) return null;
                if (tag == 0x05 && infoEnd > at) return file[at..infoEnd].ToArray();
                at = infoEnd;
            }

            return null;
        }

        return null;
    }

    /// <summary>Reads an MPEG-4 descriptor's tag and its expandable length, leaving
    /// <paramref name="at"/> on its body.</summary>
    private static bool Descriptor(ReadOnlySpan<byte> file, ref int at, int end, out byte tag, out int bodyEnd)
    {
        tag = 0;
        bodyEnd = 0;
        if (at >= end) return false;

        tag = file[at++];
        var length = 0;
        for (var i = 0; i < 4; i++)
        {
            if (at >= end) return false;
            var next = file[at++];
            length = (length << 7) | (next & 0x7F);
            if ((next & 0x80) == 0) break;
        }

        bodyEnd = at + length;
        return bodyEnd <= end;
    }

    /// <summary>The sampling frequency an AudioSpecificConfig names, by index or as the
    /// explicit 24-bit form, and its channel configuration; null for a reserved index.</summary>
    private static (int SampleRate, int Channels)? ReadAudioSpecificConfig(byte[] asc)
    {
        var bits = new BitReader(asc);
        var objectType = bits.Read(5);
        if (objectType == 31) bits.Read(6);

        var index = bits.Read(4);
        int sampleRate;
        if (index == 0x0F)
        {
            if (bits.Remaining < 24) return null;
            sampleRate = bits.Read(24);
        }
        else if (index < AacConfig.SampleRates.Length)
        {
            sampleRate = AacConfig.SampleRates[index];
        }
        else
        {
            return null;
        }

        return (sampleRate, bits.Remaining >= 4 ? bits.Read(4) : 0);
    }

    private static int MdhdTimescale(ReadOnlySpan<byte> file, Box mdhd)
    {
        Need(file, mdhd.BodyStart + 1, mdhd.End);
        var at = mdhd.BodyStart + (file[mdhd.BodyStart] == 1 ? 20 : 12);
        Need(file, at + 4, mdhd.End);
        var timescale = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
        return timescale > int.MaxValue ? 0 : (int)timescale;
    }

    private static uint TkhdTrackId(ReadOnlySpan<byte> file, Box tkhd)
    {
        Need(file, tkhd.BodyStart + 1, tkhd.End);
        var at = tkhd.BodyStart + (file[tkhd.BodyStart] == 1 ? 20 : 12);
        Need(file, at + 4, tkhd.End);
        return BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
    }

    /// <summary>The frames the classic sample table names. False when one lies past the end of
    /// the file: the file is torn, and no frame after it is read.</summary>
    private static bool ReadSampleTable(ReadOnlySpan<byte> file, Box stbl, List<AacFrame> frames)
    {
        var boxes = Children(file, stbl.BodyStart, stbl.End);

        if (First(boxes, "stsz") is not { } stsz) return true;
        Need(file, stsz.BodyStart + 12, stsz.End);
        var fixedSize = BinaryPrimitives.ReadUInt32BigEndian(file[(stsz.BodyStart + 4)..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file[(stsz.BodyStart + 8)..]);
        if (count == 0) return true;

        var offsets = ChunkOffsets(file, First(boxes, "stco"), First(boxes, "co64"));
        var chunks = SampleToChunk(file, First(boxes, "stsc"));
        var durations = TimeToSample(file, First(boxes, "stts"), Math.Min(count, (uint)file.Length));
        if (offsets.Count == 0 || chunks.Count == 0) throw new MalformedException();

        var sample = 0L;
        var lastDuration = 1024;
        var run = 0;
        var perChunk = 0u;
        for (var chunk = 0; chunk < offsets.Count && sample < count; chunk++)
        {
            // The stsc run in force is the last whose first chunk (1-based) is at or before this one.
            while (run < chunks.Count && chunks[run].FirstChunk <= chunk + 1) perChunk = chunks[run++].SamplesPerChunk;
            var position = offsets[chunk];

            for (var i = 0L; i < perChunk && sample < count; i++, sample++)
            {
                long size = fixedSize;
                if (fixedSize == 0)
                {
                    var entry = stsz.BodyStart + 12 + (sample * 4);
                    if (entry + 4 > stsz.End) return false;
                    size = BinaryPrimitives.ReadUInt32BigEndian(file[(int)entry..]);
                }

                if (sample < durations.Count) lastDuration = durations[(int)sample];
                if (!Within(file, position, size)) return false;

                frames.Add(new AacFrame(new Range((int)position, (int)(position + size)), lastDuration));
                position += size;
            }
        }

        return true;
    }

    private static List<long> ChunkOffsets(ReadOnlySpan<byte> file, Box? stco, Box? co64)
    {
        var offsets = new List<long>();
        if ((stco ?? co64) is not { } box) return offsets;

        var wide = stco is null;
        Need(file, box.BodyStart + 8, box.End);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file[(box.BodyStart + 4)..]);
        var at = box.BodyStart + 8;
        for (var i = 0u; i < count && at + (wide ? 8 : 4) <= box.End; i++)
        {
            offsets.Add(wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(file[at..]) : BinaryPrimitives.ReadUInt32BigEndian(file[at..]));
            at += wide ? 8 : 4;
        }

        return offsets;
    }

    private static List<(uint FirstChunk, uint SamplesPerChunk)> SampleToChunk(ReadOnlySpan<byte> file, Box? stsc)
    {
        var runs = new List<(uint, uint)>();
        if (stsc is not { } box) return runs;

        Need(file, box.BodyStart + 8, box.End);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file[(box.BodyStart + 4)..]);
        var at = box.BodyStart + 8;
        for (var i = 0u; i < count && at + 12 <= box.End; i++, at += 12)
        {
            runs.Add((BinaryPrimitives.ReadUInt32BigEndian(file[at..]), BinaryPrimitives.ReadUInt32BigEndian(file[(at + 4)..])));
        }

        return runs;
    }

    /// <summary>The first <paramref name="samples"/> durations, the <c>stts</c> runs expanded.</summary>
    private static List<int> TimeToSample(ReadOnlySpan<byte> file, Box? stts, uint samples)
    {
        var durations = new List<int>();
        if (stts is not { } box) return durations;

        Need(file, box.BodyStart + 8, box.End);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file[(box.BodyStart + 4)..]);
        var at = box.BodyStart + 8;
        for (var i = 0u; i < count && at + 8 <= box.End; i++, at += 8)
        {
            var run = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
            var delta = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(file[(at + 4)..]), int.MaxValue);
            for (var s = 0u; s < run && durations.Count < samples; s++) durations.Add(delta);
        }

        return durations;
    }

    /// <summary>The frames of <paramref name="trackId"/> in every <c>moof</c> after the
    /// <c>moov</c>. False when one lies past the end of the file.</summary>
    private static bool ReadFragments(ReadOnlySpan<byte> file, List<Box> top, Box moov, uint trackId, List<AacFrame> frames)
    {
        foreach (var moof in top.Where(box => box.Type == "moof"))
        {
            long previousEnd = moof.Start;
            foreach (var traf in Children(file, moof.BodyStart, moof.End).Where(box => box.Type == "traf"))
            {
                var trafBoxes = Children(file, traf.BodyStart, traf.End);
                if (First(trafBoxes, "tfhd") is not { } tfhd) continue;

                Need(file, tfhd.BodyStart + 8, tfhd.End);
                var tfFlags = Flags(file, tfhd);
                var trafTrack = BinaryPrimitives.ReadUInt32BigEndian(file[(tfhd.BodyStart + 4)..]);

                // Another track's traf is walked too, its frames dropped, so the traf after it
                // knows where its data ended.
                var kept = trafTrack == trackId ? frames : null;
                var (defaultDuration, defaultSize) = TrackExtends(file, moov, trafTrack);

                // The data is addressed from the moof when the flag says so, else from where
                // the traf before it ended (the moof itself for the first), unless tfhd names
                // a base outright.
                var at = tfhd.BodyStart + 8;
                long baseOffset = (tfFlags & 0x020000) != 0 ? moof.Start : previousEnd;
                if ((tfFlags & 0x01) != 0)
                {
                    Need(file, at + 8, tfhd.End);
                    var explicitBase = BinaryPrimitives.ReadUInt64BigEndian(file[at..]);
                    if (explicitBase > (ulong)file.Length) return false;
                    baseOffset = (long)explicitBase;
                    at += 8;
                }

                if ((tfFlags & 0x02) != 0) at += 4;

                var duration = defaultDuration;
                if ((tfFlags & 0x08) != 0)
                {
                    Need(file, at + 4, tfhd.End);
                    duration = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
                    at += 4;
                }

                var size = defaultSize;
                if ((tfFlags & 0x10) != 0)
                {
                    Need(file, at + 4, tfhd.End);
                    size = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
                }

                var position = baseOffset;
                foreach (var trun in trafBoxes.Where(box => box.Type == "trun"))
                {
                    if (!ReadRun(file, trun, baseOffset, ref position, duration, size, kept)) return false;
                }

                previousEnd = position;
            }
        }

        return true;
    }

    private static bool ReadRun(ReadOnlySpan<byte> file, Box trun, long baseOffset, ref long position, uint duration, uint size, List<AacFrame>? frames)
    {
        Need(file, trun.BodyStart + 8, trun.End);
        var flags = Flags(file, trun);
        var count = BinaryPrimitives.ReadUInt32BigEndian(file[(trun.BodyStart + 4)..]);
        var at = trun.BodyStart + 8;

        if ((flags & 0x01) != 0)
        {
            Need(file, at + 4, trun.End);
            position = baseOffset + BinaryPrimitives.ReadInt32BigEndian(file[at..]);
            at += 4;
        }

        if ((flags & 0x04) != 0) at += 4;

        var entry = ((flags & 0x100) != 0 ? 4 : 0) + ((flags & 0x200) != 0 ? 4 : 0) + ((flags & 0x400) != 0 ? 4 : 0) + ((flags & 0x800) != 0 ? 4 : 0);
        for (var i = 0u; i < count; i++)
        {
            // A trun cut short by a torn file: the samples it still names are all there is.
            if (at + entry > trun.End) return false;

            var sampleDuration = duration;
            var sampleSize = size;
            if ((flags & 0x100) != 0) { sampleDuration = BinaryPrimitives.ReadUInt32BigEndian(file[at..]); at += 4; }
            if ((flags & 0x200) != 0) { sampleSize = BinaryPrimitives.ReadUInt32BigEndian(file[at..]); at += 4; }
            if ((flags & 0x400) != 0) at += 4;
            if ((flags & 0x800) != 0) at += 4;

            if (!Within(file, position, sampleSize)) return false;

            frames?.Add(new AacFrame(new Range((int)position, (int)(position + sampleSize)), (int)Math.Min(sampleDuration, int.MaxValue)));
            position += sampleSize;
        }

        return true;
    }

    /// <summary>The <c>trex</c> defaults of <paramref name="trackId"/>: sample duration and size.</summary>
    private static (uint Duration, uint Size) TrackExtends(ReadOnlySpan<byte> file, Box moov, uint trackId)
    {
        if (Child(file, moov, "mvex") is not { } mvex) return (0, 0);

        foreach (var trex in Children(file, mvex.BodyStart, mvex.End).Where(box => box.Type == "trex"))
        {
            Need(file, trex.BodyStart + 20, trex.End);
            if (BinaryPrimitives.ReadUInt32BigEndian(file[(trex.BodyStart + 4)..]) != trackId) continue;
            return (BinaryPrimitives.ReadUInt32BigEndian(file[(trex.BodyStart + 12)..]), BinaryPrimitives.ReadUInt32BigEndian(file[(trex.BodyStart + 16)..]));
        }

        return (0, 0);
    }

    /// <summary>Whether <paramref name="size"/> bytes at <paramref name="position"/> lie in the
    /// file, compared without the sum, which an offset near the 64-bit limit overflows.</summary>
    private static bool Within(ReadOnlySpan<byte> file, long position, long size) =>
        position >= 0 && position <= file.Length && size <= file.Length - position;

    private static int Flags(ReadOnlySpan<byte> file, Box box) =>
        (file[box.BodyStart + 1] << 16) | (file[box.BodyStart + 2] << 8) | file[box.BodyStart + 3];

    private static Box? Child(ReadOnlySpan<byte> file, Box parent, string type) =>
        First(Children(file, parent.BodyStart, parent.End), type);

    private static Box? First(List<Box> boxes, string type)
    {
        foreach (var box in boxes)
        {
            if (box.Type == type) return box;
        }

        return null;
    }

    /// <summary>The boxes between <paramref name="start"/> and <paramref name="end"/>. A box
    /// that runs past its parent — a torn file — is cut at the parent's end; a size of 0 runs
    /// to it; a header that cannot be a box ends the list.</summary>
    private static List<Box> Children(ReadOnlySpan<byte> file, int start, int end)
    {
        var boxes = new List<Box>();
        end = Math.Min(end, file.Length);

        for (var at = (long)start; at + 8 <= end;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(file[(int)at..]);
            var header = 8;
            if (size == 1)
            {
                if (at + 16 > end) break;
                var large = BinaryPrimitives.ReadUInt64BigEndian(file[(int)(at + 8)..]);
                size = large > long.MaxValue ? long.MaxValue : (long)large;
                header = 16;
            }
            else if (size == 0)
            {
                size = end - at;
            }

            if (size < header) break;

            // Compared without the sum, which a 64-bit size near its limit overflows.
            var boxEnd = size >= end - at ? end : at + size;
            boxes.Add(new Box(Ascii(file, (int)at + 4, end), (int)at, (int)(at + header), (int)boxEnd));
            at = boxEnd;
        }

        return boxes;
    }

    private static string Ascii(ReadOnlySpan<byte> file, int at, int end)
    {
        Need(file, at + 4, end);
        return Encoding.ASCII.GetString(file.Slice(at, 4));
    }

    private static void Need(ReadOnlySpan<byte> file, long until, int end)
    {
        if (until > end || until > file.Length) throw new MalformedException();
    }

    /// <summary>A box: its four-character type, where it starts, where its body starts, and
    /// where it ends.</summary>
    private readonly record struct Box(string Type, int Start, int BodyStart, int End);

    /// <summary>A field read past the box that holds it: the file is not one this reader can use.</summary>
    private sealed class MalformedException : Exception;

    /// <summary>Reads big-endian bit fields, the way an AudioSpecificConfig is packed.</summary>
    private sealed class BitReader(byte[] bytes)
    {
        private int _at;

        public int Remaining => (bytes.Length * 8) - _at;

        public int Read(int width)
        {
            var value = 0;
            for (var i = 0; i < width; i++, _at++)
            {
                var bit = _at / 8 < bytes.Length ? (bytes[_at / 8] >> (7 - (_at % 8))) & 1 : 0;
                value = (value << 1) | bit;
            }

            return value;
        }
    }
}
