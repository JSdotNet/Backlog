using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane on a Brain it cannot read (<c>.devbook/domain/devbook/requirements.md#brain-scope</c>):
/// with no folder set it asks for one — through the Settings card that owns the
/// setting — and with one set that is missing or not a path it says what is wrong in
/// the source's own words. Neither draws a tab, and neither throws.
/// </summary>
public sealed class DevbookPaneBrainFolderStateTests
{
    [Fact]
    public async Task With_no_folder_set_the_pane_asks_for_one_and_links_to_settings()
    {
        await using var harness = DevbookPaneBrainHarness.Create(brainFolder: null);

        var component = harness.RenderBrain();

        var prompt = component.WaitForElement("[data-testid='devbook-brain-first-use']");
        Assert.Equal("status", prompt.GetAttribute("role"));
        Assert.Contains(BrainSetup.SetUpLabel, prompt.TextContent, StringComparison.Ordinal);
        var setUp = component.Find("[data-testid='devbook-brain-set-up']");
        Assert.Equal(BrainSetup.SettingsHref, setUp.GetAttribute("href"));

        // Settings is a page of the app: no new window, which the WebView would hand
        // to the system browser.
        Assert.False(setUp.HasAttribute("target"));

        Assert.Empty(DevbookPaneBrainHarness.Tabs(component));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-path']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-menu-heading']"));

        // Still the Brain, by name: the badge does not wait on a folder.
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-brain-badge']"));
    }

    [Fact]
    public async Task A_folder_set_and_missing_says_where_it_was_looked_for()
    {
        var missing = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-brain-tests", Guid.NewGuid().ToString("n"), "Gone");
        await using var harness = DevbookPaneBrainHarness.Create(brainFolder: missing);

        var component = harness.RenderBrain();

        var message = component.WaitForElement("[data-testid='devbook-brain-unavailable']");
        Assert.Equal($"The Brain folder was not found at {missing}.", message.TextContent.Trim());
        Assert.Equal("status", message.GetAttribute("role"));
        Assert.Empty(DevbookPaneBrainHarness.Tabs(component));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-first-use']"));
    }

    [Fact]
    public async Task A_folder_that_is_not_a_path_says_so()
    {
        await using var harness = DevbookPaneBrainHarness.Create(brainFolder: "notes");

        var component = harness.RenderBrain();

        var message = component.WaitForElement("[data-testid='devbook-brain-unavailable']");
        Assert.Equal("The Brain folder path is not valid: notes", message.TextContent.Trim());
        Assert.Empty(DevbookPaneBrainHarness.Tabs(component));
    }

    /// <summary>Setting a folder while the pane is open fills it, without a restart:
    /// the watch announces the setting through the Brain's change event.</summary>
    [Fact]
    public async Task Setting_a_folder_while_the_pane_is_open_fills_it()
    {
        await using var harness = DevbookPaneBrainHarness.Create(brainFolder: null);
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/standup.md", "# Standup\n\nToday.\n");

        var component = harness.RenderBrain();
        component.WaitForElement("[data-testid='devbook-brain-first-use']");

        harness.Settings.TrySetBrainFolder(harness.BrainFolder);
        await Task.Run(harness.Brain.NotifyContentChanged, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["Work"], DevbookPaneBrainHarness.Tabs(component));
            Assert.Contains("Today.", component.Markup, StringComparison.Ordinal);
            Assert.Empty(component.FindAll("[data-testid='devbook-brain-first-use']"));
        });
    }
}
