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

    public DictationLanguage Language
    {
        get => Enum.TryParse<DictationLanguage>(preferences.Get(LanguageKey, string.Empty), ignoreCase: false, out var language)
               && Enum.IsDefined(language)
            ? language
            : default;
        set => preferences.Set(LanguageKey, value.ToString());
    }
}
