namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The phone's dictation settings, kept on this device.
/// </summary>
/// <remarks>
/// A device setting rather than one that syncs: which language a person dictates in on
/// this phone is this phone's, not every device's. The Android head keeps it in the app's
/// preferences, so it outlives a launch; the browser harness and the tests keep it for as
/// long as the process. Transcribers read it at the start of every turn, so a change made
/// in the Settings sheet applies to the next press of the mic.
/// </remarks>
public interface ISpeechSettingsStore
{
    /// <summary>Which language dictation listens for.</summary>
    DictationLanguage Language { get; set; }
}

/// <summary>
/// <see cref="ISpeechSettingsStore"/> for as long as the process: the browser harness's
/// and the tests'.
/// </summary>
public sealed class InMemorySpeechSettingsStore : ISpeechSettingsStore
{
    private int _language;

    /// <inheritdoc />
    public DictationLanguage Language
    {
        get => (DictationLanguage)Volatile.Read(ref _language);
        set => Volatile.Write(ref _language, (int)value);
    }
}
