namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Lifts the AAC frames out of an ADTS stream and writes them into MP4 containers as they
/// are, in parts of at most a given length (local ADR 0049 §3), through
/// <see cref="AacRemuxer"/>. Nothing is decoded or re-encoded: an ADTS frame stands alone,
/// so a cut on a frame boundary needs no codec, and the audio is the same bytes in a
/// different framing.
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
    public static readonly TimeSpan DefaultPartLength = AacRemuxer.DefaultPartLength;

    /// <summary>AAC-LC and its siblings code 1024 samples per raw data block.</summary>
    private const int SamplesPerBlock = 1024;

    /// <summary>Whether <paramref name="stream"/> opens with an ADTS sync word — the test a
    /// caller uses to tell an ADTS stream from an MP4 file.</summary>
    public static bool LooksLikeAdts(ReadOnlySpan<byte> stream) =>
        stream.Length >= 2 && stream[0] == 0xFF && (stream[1] & 0xF6) == 0xF0;

    /// <summary>Splits and remuxes <paramref name="adts"/>. Empty when the stream holds no
    /// whole frame.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partLength"/> is shorter
    /// than a frame.</exception>
    public static IReadOnlyList<RemuxedPart> Remux(ReadOnlySpan<byte> adts, TimeSpan? partLength = null)
    {
        var track = Read(adts);
        return track is null ? [] : AacRemuxer.Remux(adts, track, partLength);
    }

    /// <summary>The raw AAC payload of every whole frame, by position in the stream, with the
    /// stream's configuration from its first frame and the sample rate as its timescale.
    /// Null when the stream holds no whole frame.</summary>
    internal static AacTrack? Read(ReadOnlySpan<byte> adts)
    {
        AacConfig? config = null;
        var sampleRate = 0;
        var frames = new List<AacFrame>();
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

            if (rateIndex >= AacConfig.SampleRates.Length || frameLength <= headerLength)
            {
                position++;
                continue;
            }

            // A torn last frame: everything before it is whole, and that is what is kept.
            if (position + frameLength > adts.Length) break;

            if (config is null)
            {
                sampleRate = AacConfig.SampleRates[rateIndex];
                config = new AacConfig(sampleRate, channels, AudioSpecificConfig(objectType, rateIndex, channels));
            }

            frames.Add(new AacFrame(new Range(position + headerLength, position + frameLength), blocks * SamplesPerBlock));
            position += frameLength;
        }

        return config is null ? null : new AacTrack(config, sampleRate, frames);
    }

    /// <summary>The two-byte AudioSpecificConfig rebuilt from the ADTS header.</summary>
    private static byte[] AudioSpecificConfig(int objectType, int rateIndex, int channels) =>
    [
        (byte)((objectType << 3) | (rateIndex >> 1)),
        (byte)(((rateIndex & 0x01) << 7) | (channels << 3)),
    ];
}
