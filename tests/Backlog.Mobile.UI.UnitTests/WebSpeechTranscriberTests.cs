namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The browser harness's transcriber hands the Web Speech recogniser the Dictation
/// setting's language, read at the start of every turn.
/// </summary>
public sealed class WebSpeechTranscriberTests
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/speech.js";

    [Theory]
    [InlineData(DictationLanguage.DutchAndEnglish, "nl-NL")]
    [InlineData(DictationLanguage.Dutch, "nl-NL")]
    [InlineData(DictationLanguage.English, "en-US")]
    [InlineData(DictationLanguage.PhoneLanguage, null)]
    public async Task A_turn_listens_in_the_dictation_language(DictationLanguage choice, string? expected)
    {
        using var context = new BunitContext();
        var module = Speech(context);
        var settings = new InMemorySpeechSettingsStore { Language = choice };
        await using var transcriber = new WebSpeechTranscriber(context.JSInterop.JSRuntime, settings);

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.OnRecognizedAsync("hallo");

        Assert.Equal("hallo", (await turn).Text);
        Assert.Equal(expected, module.VerifyInvoke("start").Arguments[1]);
    }

    [Fact]
    public async Task A_change_of_setting_applies_from_the_next_turn()
    {
        using var context = new BunitContext();
        var module = Speech(context);
        var settings = new InMemorySpeechSettingsStore { Language = DictationLanguage.Dutch };
        await using var transcriber = new WebSpeechTranscriber(context.JSInterop.JSRuntime, settings);

        var first = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.OnRecognizedAsync("een");
        await first;

        settings.Language = DictationLanguage.English;
        var second = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.OnRecognizedAsync("two");
        await second;

        Assert.Equal(
            ["nl-NL", "en-US"],
            module.VerifyInvoke("start", calledTimes: 2).Select(invocation => (string?)invocation.Arguments[1]));
    }

    private static BunitJSModuleInterop Speech(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        module.SetupVoid("start", _ => true).SetVoidResult();
        return module;
    }
}
