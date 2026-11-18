using Backlog.UI.Components.Inputs;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The sound the audio pages play: <c>AudioPlayerPage</c> on its own and
/// <c>AttachmentStripPage</c> on a recording's tile. Generated here, so the
/// storybook ships no recording and both pages play the same second.
/// </summary>
internal static class AudioFixtures
{
    /// <summary>A second of tone the player can really play.</summary>
    public static readonly AudioSource Tone = new("audio/wav", _ => Task.FromResult<Stream?>(new MemoryStream(ToneWav())));

    /// <summary>A file gone since it was listed.</summary>
    public static readonly AudioSource Gone = new("audio/aac", _ => Task.FromResult<Stream?>(null));

    /// <summary>One second of a quiet 440 Hz tone as 8 kHz 16-bit mono PCM.</summary>
    private static byte[] ToneWav()
    {
        const int rate = 8000;
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + rate * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(rate * 2);
            for (var i = 0; i < rate; i++)
            {
                writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 3000));
            }
        }

        return buffer.ToArray();
    }
}
