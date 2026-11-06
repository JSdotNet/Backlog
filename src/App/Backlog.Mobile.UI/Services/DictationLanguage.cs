namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Which language dictation listens for: the phone's Dictation setting.
/// </summary>
/// <remarks>
/// Stored by name (<see cref="ISpeechSettingsStore"/>), so the order here can change; the
/// default is the first member, because a phone that has never been asked is a phone
/// whose owner speaks Dutch and English.
/// </remarks>
public enum DictationLanguage
{
    /// <summary>Detects whether Dutch or English is spoken where the recogniser can
    /// (Android 14 and later), and listens for Dutch where it cannot.</summary>
    DutchAndEnglish = 0,

    /// <summary>Dutch only.</summary>
    Dutch,

    /// <summary>English only.</summary>
    English,

    /// <summary>The phone's own language — what dictation did before the setting existed.</summary>
    PhoneLanguage
}

/// <summary>
/// What one listening turn asks a recogniser for, worked out from the Dictation setting.
/// Each transcriber turns it into its own platform's request; neither decides the
/// language on its own.
/// </summary>
/// <param name="Language">The BCP 47 tag to listen for, or <c>null</c> to leave it to the
/// recogniser's own default — the phone's language when the device names none.</param>
/// <param name="DetectionLanguages">The languages a recogniser that can detect the spoken
/// language chooses among; empty when it is not to detect.</param>
/// <param name="SwitchesLanguage">Whether the recogniser may change language part-way
/// through a turn, among <paramref name="DetectionLanguages"/>.</param>
public sealed record DictationRequest(string? Language, IReadOnlyList<string> DetectionLanguages, bool SwitchesLanguage)
{
    /// <summary>Dutch as the Netherlands speaks it.</summary>
    public const string Dutch = "nl-NL";

    /// <summary>English as the recognisers know it best.</summary>
    public const string English = "en-US";

    private static readonly IReadOnlyList<string> DutchAndEnglish = [Dutch, English];

    /// <summary>Whether the recogniser is asked to work out which language is spoken.</summary>
    public bool DetectsLanguage => DetectionLanguages.Count > 0;

    /// <summary>
    /// The request for <paramref name="choice"/>.
    /// </summary>
    /// <param name="choice">The Dictation setting. A value this build does not know reads
    /// as the default.</param>
    /// <param name="deviceLanguage">The device's own language tag, for
    /// <see cref="DictationLanguage.PhoneLanguage"/>.</param>
    /// <param name="supportsDetection">Whether the recogniser takes the language detection
    /// extras at all — Android 14 (API 34) and later. Where it does not, Dutch and English
    /// asks for Dutch: the language the on-device recogniser gets most wrong when it is
    /// left to guess from the phone's language.</param>
    public static DictationRequest For(DictationLanguage choice, string? deviceLanguage, bool supportsDetection) =>
        choice switch
        {
            DictationLanguage.Dutch => Single(Dutch),
            DictationLanguage.English => Single(English),
            DictationLanguage.PhoneLanguage => Single(string.IsNullOrWhiteSpace(deviceLanguage) ? null : deviceLanguage),
            _ => supportsDetection
                ? new DictationRequest(Dutch, DutchAndEnglish, SwitchesLanguage: true)
                : Single(Dutch)
        };

    /// <summary>
    /// The language hint a cloud turn sends with its recording (local ADR 0045 §6): one of
    /// the route's <c>nl</c> and <c>en</c>, or <c>null</c> to let the model detect it.
    /// </summary>
    /// <param name="choice">The Dictation setting. Only a single language is hinted: Dutch and
    /// English is what the model's own detection is for, and the phone's language may be one
    /// the route does not take.</param>
    public static string? TranscriptionHintFor(DictationLanguage choice) => choice switch
    {
        DictationLanguage.Dutch => "nl",
        DictationLanguage.English => "en",
        _ => null
    };

    private static DictationRequest Single(string? language) => new(language, [], SwitchesLanguage: false);
}
