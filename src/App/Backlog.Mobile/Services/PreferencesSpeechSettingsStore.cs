using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="ISpeechSettingsStore"/> in the app's preferences, so the Dictation language
/// a person chose is still the one dictation uses after the app is closed.
/// </summary>
/// <remarks>
/// Kept by name rather than by number, so a later build that reorders or adds languages
/// reads an old choice as the same language; a name it no longer knows reads as the
/// default.
/// </remarks>
public sealed class PreferencesSpeechSettingsStore(IPreferences preferences) : ISpeechSettingsStore
{
    private const string LanguageKey = "dictation.language";
    private const string EngineKey = "dictation.engine";

    public DictationLanguage Language
    {
        get => Read<DictationLanguage>(LanguageKey);
        set => preferences.Set(LanguageKey, value.ToString());
    }

    /// <summary>On-device unless the person chose the cloud: a name this build does not know
    /// reads as the default, so no recording leaves the phone on a guess.</summary>
    public DictationEngine Engine
    {
        get => Read<DictationEngine>(EngineKey);
        set => preferences.Set(EngineKey, value.ToString());
    }

    private TEnum Read<TEnum>(string key) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(preferences.Get(key, string.Empty), ignoreCase: false, out var value)
        && Enum.IsDefined(value)
            ? value
            : default;
}
