using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <c>Brain | repo</c> switch beside the pane's title (local ADR 0027 §4): drawn
/// while the Brain and a repository are both in the scope, a radio group of two
/// toggle buttons, pressed on the side the pane shows. The pane only asks — the
/// choice is the shell's, which is what keeps it off the scope and the task filter;
/// <see cref="HomeRepositoryScopeTests"/> proves that half.
/// </summary>
public sealed class DevbookPaneBrainSwitchTests
{
    [Fact]
    public async Task On_the_brain_side_the_switch_is_a_radio_group_with_the_brain_checked()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("projects/plan.md", "# Plan\n");

        var component = harness.RenderBrain(switchRepositoryAlias: harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.NotEmpty(DevbookPaneBrainHarness.Tabs(component)));

        var group = component.Find("[data-testid='devbook-brain-switch']");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(group.GetAttribute("aria-label")));

        var brain = component.Find("[data-testid='devbook-brain-switch-brain']");
        var repository = component.Find("[data-testid='devbook-brain-switch-repository']");
        Assert.Equal("radio", brain.GetAttribute("role"));
        Assert.Equal("true", brain.GetAttribute("aria-checked"));
        Assert.Equal("false", repository.GetAttribute("aria-checked"));
        Assert.Equal("Brain", brain.TextContent.Trim());
        Assert.Equal(harness.RepositoryAlias, repository.TextContent.Trim());

        // The Brain side wears the Brain's chrome.
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-brain-badge']"));
    }

    [Fact]
    public async Task Pressing_the_repository_side_asks_the_shell_for_the_repository()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("projects/plan.md", "# Plan\n");
        var asked = new List<bool>();

        var component = harness.RenderBrain(harness.RepositoryAlias, asked.Add);
        component.WaitForAssertion(() => Assert.NotEmpty(DevbookPaneBrainHarness.Tabs(component)));

        component.Find("[data-testid='devbook-brain-switch-repository']").Click();

        component.WaitForAssertion(() => Assert.Equal([false], asked));
    }

    [Fact]
    public async Task On_the_repository_side_the_switch_stays_and_the_brain_chrome_goes()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var asked = new List<bool>();

        var component = harness.Render(harness.RepositoryAlias, harness.RepositoryAlias, asked.Add);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        Assert.Equal("Devbook", component.Find("[data-testid='devbook-pane-title']").TextContent.Trim());
        Assert.Single(component.FindAll(".devbook-stack__strip--titled"));
        Assert.Equal("false", component.Find("[data-testid='devbook-brain-switch-brain']").GetAttribute("aria-checked"));
        Assert.Equal("true", component.Find("[data-testid='devbook-brain-switch-repository']").GetAttribute("aria-checked"));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-badge']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-brain-path']"));
        Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-select']"));

        component.Find("[data-testid='devbook-brain-switch-brain']").Click();
        component.WaitForAssertion(() => Assert.Equal([true], asked));
    }

    /// <summary>Flipping the pane over and back keeps each side's own tab: the
    /// two sides' keys never meet, so neither selection leaks into the other.</summary>
    [Fact]
    public async Task Flipping_over_and_back_returns_to_the_tab_that_side_had_open()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n- Home life — `home`\n");
        harness.WriteNote("work/standup.md", "# Standup\n");
        harness.WriteNote("home/garden.md", "# Garden\n\nTomatoes.\n");

        var component = harness.RenderBrain(harness.RepositoryAlias);
        // The first note on screen, not merely the tabs: the strip is drawn before
        // the menu load lands, and a press on a tab from that render is a press on
        // a handler the next render has already replaced.
        component.WaitForAssertion(() => Assert.Equal("Standup", component.Find("[data-testid='brain-note-file'] .file-view__name").TextContent.Trim()));
        component.FindAll(".devbook-stack__nav-item").Single(tab => tab.TextContent.Trim() == "Home life").Click();
        component.WaitForAssertion(() => Assert.Contains("Tomatoes.", component.Markup, StringComparison.Ordinal));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, harness.RepositoryAlias));
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-brain-badge']"));

            // The repository's menu, not the Brain's: a row under the open tab
            // exists only once the repository's own load has landed.
            Assert.NotEmpty(component.FindAll(".devbook-menu__item"));
        });

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));
        component.WaitForAssertion(() =>
        {
            Assert.Equal("Home life", component.Find(".devbook-stack__nav-item--active").TextContent.Trim());
            Assert.Contains("Tomatoes.", component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>Pressed twice before the repository's menu is in: whichever load
    /// finishes last, the pane ends on the Brain it was asked for last.</summary>
    [Fact]
    public async Task Flipping_back_before_the_repository_has_loaded_keeps_the_brain()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/standup.md", "# Standup\n\nToday.\n");

        var component = harness.RenderBrain(harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.Contains("Today.", component.Markup, StringComparison.Ordinal));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, harness.RepositoryAlias));
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));

        // Long enough for the repository's walk to have landed, had it been let.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["Work"], DevbookPaneBrainHarness.Tabs(component));
            Assert.Equal("Standup", component.Find(".devbook-menu__item--file").TextContent.Trim());
            Assert.Contains("Today.", component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>The atlas or the search, opened on the repository, does not follow
    /// the pane onto the Brain: the Brain draws no atlas, and a flip is the reader
    /// choosing what to read, so the flip closes them and the Brain opens on its
    /// note menu.</summary>
    [Theory]
    [InlineData("devbook-atlas-toggle")]
    [InlineData("devbook-search-toggle")]
    public async Task A_reading_open_on_the_repository_is_closed_by_the_flip_to_the_brain(string toggle)
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/standup.md", "# Standup\n\nToday.\n");

        var component = harness.Render(harness.RepositoryAlias, harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        await component.InvokeAsync(() => component.Find($"[data-testid='{toggle}']").Click());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-stack__section--atlas")));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, string.Empty));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Today.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-menu-heading']"));
            Assert.Empty(component.FindAll(".devbook-stack__section--atlas"));
        });

        // And back on the repository, the reading is not waiting there either.
        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, harness.RepositoryAlias));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Empty(component.FindAll(".devbook-stack__section--atlas"));
    }

    [Fact]
    public async Task With_only_the_brain_there_is_no_switch()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("projects/plan.md", "# Plan\n");

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotEmpty(DevbookPaneBrainHarness.Tabs(component)));

        Assert.Empty(component.FindAll("[data-testid='devbook-brain-switch']"));
    }

    [Fact]
    public async Task With_only_a_repository_there_is_no_switch()
    {
        await using var harness = DevbookPaneBrainHarness.Create();

        var component = harness.Render(harness.RepositoryAlias);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        Assert.Empty(component.FindAll("[data-testid='devbook-brain-switch']"));
    }
}
