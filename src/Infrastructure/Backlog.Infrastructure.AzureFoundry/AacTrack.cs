namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>The AAC frames of one recording, wherever they were framed — an ADTS stream
/// or an MP4 file — as the remuxer needs them: where each lies in its source, how long it
/// plays, and what a decoder needs to play them.</summary>
/// <param name="Config">What every frame shares.</param>
/// <param name="Timescale">The units per second <see cref="AacFrame.Duration"/> counts in.</param>
/// <param name="Frames">The whole frames, in play order.</param>
internal sealed record AacTrack(AacConfig Config, int Timescale, IReadOnlyList<AacFrame> Frames);

/// <summary>One raw AAC frame: its payload's place in the source, without any framing, and
/// its length in the track's timescale.</summary>
internal readonly record struct AacFrame(Range Bytes, int Duration);

/// <summary>The decoder configuration of a track.</summary>
/// <param name="SampleRate">The sampling frequency the AudioSpecificConfig names.</param>
/// <param name="Channels">The channel count the sample entry carries.</param>
/// <param name="AudioSpecificConfig">The MPEG-4 AudioSpecificConfig, written into the
/// part's <c>esds</c> as it is.</param>
internal sealed record AacConfig(int SampleRate, int Channels, byte[] AudioSpecificConfig)
{
    /// <summary>The MPEG-4 sampling frequencies by index, as both an ADTS header and an
    /// AudioSpecificConfig number them.</summary>
    internal static readonly int[] SampleRates =
        [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
}
