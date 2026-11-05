namespace Backlog.Mobile.UI.Services;

/// <summary>
/// What one listening turn produced: either words or a reason there were none.
/// </summary>
/// <remarks>
/// A failed turn is a result rather than an exception because every way this
/// fails is ordinary — a denied microphone, a quiet room, a recogniser the
/// device does not have. The screen has to say so either way, and an exception
/// would put that message on the wrong side of a try/catch.
/// </remarks>
/// <param name="Text">The recognised words, trimmed. Empty when <paramref name="Error"/> is set.</param>
/// <param name="Error">A message fit to show a person, or <c>null</c> when the turn succeeded.</param>
public sealed record SpeechTranscript(string Text, string? Error)
{
    /// <summary>A turn that produced words.</summary>
    public static SpeechTranscript Heard(string text) => new(text.Trim(), null);

    /// <summary>A turn that produced a reason instead of words.</summary>
    public static SpeechTranscript Failed(string error) => new(string.Empty, error);

    /// <summary>Whether this turn ended in a reason rather than words.</summary>
    public bool IsError => Error is not null;
}

/// <summary>
/// Speech capture, as the screens see it.
/// </summary>
/// <remarks>
/// There are two implementations behind this and they are not interchangeable
/// at runtime: the Web Speech API is generally absent from the Android System
/// WebView, and the Android recogniser cannot be reached from the browser
/// harness. Each host registers the one it can honour, so the Inbox screen is
/// the same code in the emulator, on a device, and under Playwright.
/// </remarks>
public interface ISpeechTranscriber : IAsyncDisposable
{
    /// <summary>
    /// Whether this device can recognise speech at all. Returns <c>false</c>
    /// rather than throwing when there is no recogniser, and while a Blazor
    /// component is still prerendering and has no browser to ask.
    /// </summary>
    ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts listening and completes when recognition ends — because
    /// <see cref="StopAsync"/> was called, because the recogniser timed out on
    /// silence, or because it failed.
    /// </summary>
    /// <remarks>One turn at a time: starting a second turn abandons the first.</remarks>
    Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="ListenAsync(CancellationToken)"/>, telling <paramref name="onStage"/> as
    /// each stage of the turn begins, the first included, so the screen words the turn the
    /// way the engine that took it runs it. An on-device recogniser listens and recognises
    /// at once: it keeps this default, which says <see cref="SpeechTurnStage.Listening"/>
    /// and nothing after. The cloud engine records and then transcribes (local ADR 0045 §6).
    /// </summary>
    /// <param name="onStage">Called as a stage begins, possibly off the render thread.</param>
    /// <param name="cancellationToken">Ends the turn.</param>
    Task<SpeechTranscript> ListenAsync(Action<SpeechTurnStage>? onStage, CancellationToken cancellationToken = default)
    {
        onStage?.Invoke(SpeechTurnStage.Listening);
        return ListenAsync(cancellationToken);
    }

    /// <summary>
    /// Asks the recogniser to finish and hand back whatever it heard. Safe to
    /// call when nothing is listening.
    /// </summary>
    ValueTask StopAsync();
}

/// <summary>
/// Where a turn has got to: "Listening…" for a recogniser that hears as it goes, and
/// "Recording…" then "Transcribing…" for one that records before it transcribes, rather
/// than one "Listening…" for both.
/// </summary>
public enum SpeechTurnStage
{
    /// <summary>The device's recogniser is hearing and recognising; nothing leaves the phone.</summary>
    Listening,

    /// <summary>The microphone is on; the words are not yet anywhere.</summary>
    Recording,

    /// <summary>The recording has ended and is on its way to be transcribed.</summary>
    Transcribing
}

/// <summary>What the screens say for a <see cref="SpeechTurnStage"/>.</summary>
public static class SpeechTurnStages
{
    /// <summary>The status line for <paramref name="stage"/>.</summary>
    public static string Describe(this SpeechTurnStage stage) => stage switch
    {
        SpeechTurnStage.Listening => "Listening…",
        SpeechTurnStage.Recording => "Recording…",
        SpeechTurnStage.Transcribing => "Transcribing…"
    };
}
