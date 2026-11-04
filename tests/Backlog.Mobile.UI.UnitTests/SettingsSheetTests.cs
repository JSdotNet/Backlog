using Backlog.Mobile.UI.Components;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's Settings sheet, through the whole shell: the app bar's settings button opens
/// it, its Dictation group offers the four languages, and a choice is kept in the phone's
/// settings store and used by the next dictation.
/// </summary>
public sealed class SettingsSheetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_app_bar_opens_a_settings_sheet_with_the_dictation_language()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        var button = app.Find("[data-testid='app-settings']");
        Assert.Equal("Settings", button.GetAttribute("aria-label"));
        Assert.Equal("false", app.Find("[data-testid='app-settings-sheet']").GetAttribute("data-open"));

        button.Click();

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='app-settings-sheet']").GetAttribute("data-open")));
        var group = app.Find("[data-testid='app-settings-dictation']");
        Assert.Equal("Dictation", group.QuerySelector("h3")!.TextContent.Trim());
        Assert.Contains("Dutch and English detects which you speak, on Android 14 and later.", group.TextContent);
        Assert.Equal(
            ["Dutch and English", "Dutch", "English", "Phone language"],
            app.FindAll("[data-testid='dictation-language'] [role='radio']").Select(option => option.TextContent.Trim()));
    }

    [Fact]
    public void Dutch_and_English_is_chosen_until_the_person_chooses_otherwise()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.Find("[data-testid='app-settings']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "true",
            app.Find("[data-testid='dictation-language-dutchandenglish']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void A_choice_is_kept_in_the_phone_settings_and_shown_on_the_next_opening()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.Find("[data-testid='app-settings']").Click();
        app.WaitForAssertion(() => app.Find("[data-testid='dictation-language-english']").Click());

        Assert.Equal(DictationLanguage.English, host.SpeechSettings.Language);

        app.Find("[data-testid='app-settings-sheet'] [aria-label='Close settings']").Click();
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='app-settings-sheet']").GetAttribute("data-open")));
        app.Find("[data-testid='app-settings']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "true",
            app.Find("[data-testid='dictation-language-english']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void The_next_dictation_listens_in_the_language_chosen()
    {
        using var host = ShellHost.Paired(clock: new FakeTimeProvider(Now));
        host.Speech.Supported = true;
        var app = host.Open();

        app.Find("[data-testid='app-settings']").Click();
        app.WaitForAssertion(() => app.Find("[data-testid='dictation-language-dutch']").Click());

        host.Navigation.NavigateTo("capture");
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='capture-sheet-dictate']").HasAttribute("disabled")));
        app.Find("[data-testid='capture-sheet-dictate']").Click();

        app.WaitForAssertion(() => Assert.Equal([DictationLanguage.Dutch], host.Speech.TurnLanguages));
    }
}
