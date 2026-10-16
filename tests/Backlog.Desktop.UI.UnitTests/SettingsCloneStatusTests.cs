using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository card's clone status and Clone button while git is still
/// answering: the status is asked off the UI thread, a loader shows only once the
/// answer is late, an answer to a question since replaced is dropped, and Clone
/// is busy from the press to the end - restored, with the reason, when git fails.
/// <para>
/// Driven through <see cref="GatedLocalGit"/>, which holds an answer back until
/// the test lets it go, so each in-between frame is looked at rather than hoped
/// for.
/// </para>
/// </summary>
public sealed class SettingsCloneStatusTests
{
    private const string Field = "[data-testid='repo-clone-directory-input']";
    private const string Status = "[data-testid='repo-clone-status']";
    private const string CloneButton = "[data-testid='repo-clone-button']";

    [Fact]
    public async Task A_late_answer_shows_the_loader_and_an_answer_for_a_folder_since_replaced_is_dropped()
    {
        var git = new GatedLocalGit();
        using var settings = SettingsRepositoryColourTests.RenderSettings(git);
        var first = Path.Combine(settings.Root, "first");
        var second = Path.Combine(settings.Root, "second");
        git.Hold(first);
        SettingsMenu.OpenRepository(settings.Component, "backlog");

        await SetFolder(settings.Component, first);

        // Held past the threshold, so the loader shows.
        settings.Component.WaitForAssertion(() => Assert.Contains(
            "Checking the local clone", settings.Component.Find(Status).TextContent, StringComparison.Ordinal));

        await SetFolder(settings.Component, second);
        settings.Component.WaitForAssertion(() => Assert.Contains(
            $"Not cloned yet: {second}", settings.Component.Find(Status).TextContent, StringComparison.Ordinal));

        git.Release(first);
        await git.Answered(first);

        // Let the page's continuation for the first folder run, then look again.
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await settings.Component.InvokeAsync(() => { });

        var status = settings.Component.Find(Status).TextContent;
        Assert.Contains($"Not cloned yet: {second}", status, StringComparison.Ordinal);
        Assert.DoesNotContain(first, status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clone_is_busy_until_git_has_answered_and_a_second_press_clones_nothing()
    {
        var git = new GatedLocalGit();
        using var settings = SettingsRepositoryColourTests.RenderSettings(git);
        var target = Path.Combine(settings.Root, "clone");
        SettingsMenu.OpenRepository(settings.Component, "backlog");
        await SetFolder(settings.Component, target);
        settings.Component.WaitForAssertion(() => Assert.False(settings.Component.Find(CloneButton).HasAttribute("disabled")));

        settings.Component.Find(CloneButton).Click();

        settings.Component.WaitForAssertion(() =>
        {
            var button = settings.Component.Find(CloneButton);
            Assert.Equal("true", button.GetAttribute("aria-busy"));
            Assert.False(button.HasAttribute("disabled"));
            Assert.EndsWith("Clone repository", button.TextContent.Trim(), StringComparison.Ordinal);
        });

        // The page asks git on the pool and is busy from the press, so the busy
        // render can land before the clone has started and no render follows it.
        Assert.True(SpinWait.SpinUntil(() => git.CloneCalls == 1, TimeSpan.FromSeconds(5)));

        // Awaited: the second press returns at once, so its dispatch has run.
        await settings.Component.Find(CloneButton).ClickAsync(new());
        Assert.Equal(1, git.CloneCalls);

        git.CloneGate.SetResult(null);

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Null(settings.Component.Find(CloneButton).GetAttribute("aria-busy"));
            Assert.Contains("Local clone:", settings.Component.Find(Status).TextContent, StringComparison.Ordinal);
        });
        Assert.Equal(1, git.CloneCalls);
    }

    /// <summary>A missing drive or a protected folder makes git's parent folder
    /// impossible to create. The button comes back and the page says why, rather
    /// than the whole Settings screen falling to the error boundary.</summary>
    [Fact]
    public async Task A_clone_that_throws_restores_the_button_and_says_why()
    {
        var git = new GatedLocalGit();
        using var settings = SettingsRepositoryColourTests.RenderSettings(git);
        var target = Path.Combine(settings.Root, "clone");
        SettingsMenu.OpenRepository(settings.Component, "backlog");
        await SetFolder(settings.Component, target);
        settings.Component.WaitForAssertion(() => Assert.False(settings.Component.Find(CloneButton).HasAttribute("disabled")));

        settings.Component.Find(CloneButton).Click();
        git.CloneGate.SetResult(new IOException("The device is not ready."));

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Null(settings.Component.Find(CloneButton).GetAttribute("aria-busy"));
            Assert.Contains("The device is not ready.", settings.Component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>Types a folder and leaves the field, each step on the renderer's
    /// thread so a background answer cannot replace a handler between Find and
    /// its event.</summary>
    private static async Task SetFolder(IRenderedComponent<Settings> component, string folder)
    {
        await component.InvokeAsync(() => component.Find(Field).Input(folder));
        await component.InvokeAsync(() => component.Find(Field).Change(folder));
    }
}
