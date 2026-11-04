namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What a recogniser is asked for, from the Dictation language setting, the device's own
/// language and whether the recogniser can detect the language spoken (Android 14 and later).
/// </summary>
public sealed class DictationRequestTests
{
    [Fact]
    public void Dutch_and_English_detects_between_the_two_where_the_recogniser_can()
    {
        var request = DictationRequest.For(DictationLanguage.DutchAndEnglish, "fr-FR", supportsDetection: true);

        Assert.Equal("nl-NL", request.Language);
        Assert.Equal(["nl-NL", "en-US"], request.DetectionLanguages);
        Assert.True(request.DetectsLanguage);
        Assert.True(request.SwitchesLanguage);
    }

    [Fact]
    public void Dutch_and_English_asks_for_Dutch_alone_where_the_recogniser_cannot_detect()
    {
        var request = DictationRequest.For(DictationLanguage.DutchAndEnglish, "en-GB", supportsDetection: false);

        Assert.Equal("nl-NL", request.Language);
        Assert.Empty(request.DetectionLanguages);
        Assert.False(request.DetectsLanguage);
        Assert.False(request.SwitchesLanguage);
    }

    [Theory]
    [InlineData(DictationLanguage.Dutch, "nl-NL")]
    [InlineData(DictationLanguage.English, "en-US")]
    public void A_single_language_is_asked_for_without_detection(DictationLanguage choice, string expected)
    {
        foreach (var supportsDetection in new[] { true, false })
        {
            var request = DictationRequest.For(choice, "de-DE", supportsDetection);

            Assert.Equal(expected, request.Language);
            Assert.False(request.DetectsLanguage);
            Assert.False(request.SwitchesLanguage);
        }
    }

    [Fact]
    public void Phone_language_asks_for_the_device_language()
    {
        var request = DictationRequest.For(DictationLanguage.PhoneLanguage, "de-DE", supportsDetection: true);

        Assert.Equal("de-DE", request.Language);
        Assert.False(request.DetectsLanguage);
        Assert.False(request.SwitchesLanguage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Phone_language_leaves_the_language_to_the_recogniser_when_the_device_names_none(string? deviceLanguage)
    {
        var request = DictationRequest.For(DictationLanguage.PhoneLanguage, deviceLanguage, supportsDetection: false);

        Assert.Null(request.Language);
    }

    [Fact]
    public void An_unknown_choice_reads_as_the_default()
    {
        var request = DictationRequest.For((DictationLanguage)99, "de-DE", supportsDetection: true);

        Assert.Equal("nl-NL", request.Language);
        Assert.Equal(["nl-NL", "en-US"], request.DetectionLanguages);
        Assert.True(request.SwitchesLanguage);
    }

    [Fact]
    public void Dutch_and_English_is_the_default_choice() =>
        Assert.Equal(DictationLanguage.DutchAndEnglish, default(DictationLanguage));
}
