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
/// <para>
/// The cut and the M4A writer are <see cref="M4aParts"/>, which <see cref="Mp4Demuxer"/>
/// shares for a recording that is already MP4.
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
    /// caller uses to tell a stream to remux from a container.</summary>
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

        // The movie timescale is the sample rate, so each frame lasts its samples.
        var track = new AacTrack(
            AudioSpecificConfig(config),
            config.SampleRate,
            config.Channels,
            config.SampleRate,
            frames,
            Enumerable.Repeat((uint)(config.BlocksPerFrame * SamplesPerBlock), frames.Count).ToArray());

        return M4aParts.Cut(adts, track, partLength ?? DefaultPartLength);
    }

    /// <summary>The two-byte AudioSpecificConfig rebuilt from the ADTS header.</summary>
    private static byte[] AudioSpecificConfig(AdtsConfig config) =>
    [
        (byte)((config.ObjectType << 3) | (config.RateIndex >> 1)),
        (byte)(((config.RateIndex & 0x01) << 7) | (config.Channels << 3)),
    ];

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

    /// <summary>What every frame of the stream shares, read from its first header.</summary>
    internal sealed record AdtsConfig(int ObjectType, int RateIndex, int SampleRate, int Channels, int BlocksPerFrame);
}
