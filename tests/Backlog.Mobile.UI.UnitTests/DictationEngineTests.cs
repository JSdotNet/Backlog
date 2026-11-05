namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Dictation engine setting (local ADR 0045 §6): on-device until the person chooses the
/// cloud, kept on this phone; and the language hint a cloud turn sends, from the Dictation
/// language.
/// </summary>
public sealed class DictationEngineTests
{
    [Fact]
    public void A_phone_that_was_never_asked_dictates_on_the_device()
    {
        var settings = new InMemorySpeechSettingsStore();

        Assert.Equal(DictationEngine.OnDevice, settings.Engine);
        Assert.Equal(DictationEngine.OnDevice, default(DictationEngine));
    }

    [Fact]
    public void The_engine_chosen_is_the_one_the_store_keeps()
    {
        var settings = new InMemorySpeechSettingsStore { Engine = DictationEngine.Cloud };

        Assert.Equal(DictationEngine.Cloud, settings.Engine);
    }

    [Fact]
    public void The_engine_and_the_language_are_kept_apart()
    {
        var settings = new InMemorySpeechSettingsStore { Language = DictationLanguage.English };

        settings.Engine = DictationEngine.Cloud;

        Assert.Equal(DictationLanguage.English, settings.Language);
    }

    [Theory]
    [InlineData(DictationLanguage.Dutch, "nl")]
    [InlineData(DictationLanguage.English, "en")]
    [InlineData(DictationLanguage.DutchAndEnglish, null)]
    [InlineData(DictationLanguage.PhoneLanguage, null)]
    [InlineData((DictationLanguage)99, null)]
    public void A_cloud_turn_hints_only_a_single_language(DictationLanguage choice, string? expected) =>
        Assert.Equal(expected, DictationRequest.TranscriptionHintFor(choice));
}
