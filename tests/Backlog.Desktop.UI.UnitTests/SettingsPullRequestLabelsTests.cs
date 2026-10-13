using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository card's "Pull request labels" field: the pull requests pane's label
/// filter, saved on the registry row as the field is left. Asserted against the store,
/// which is what the pane reads.
/// </summary>
public sealed class SettingsPullRequestLabelsTests
{
    private const string Field = "[data-testid='repo-pull-request-labels-input']";

    [Fact]
    public void Leaving_the_field_saves_its_comma_separated_labels()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsRepositoryColourTests.OpenRepositoriesTab(settings.Component);

        settings.Component.Find(Field).Input(" frontend, design ,, Frontend ");
        settings.Component.Find(Field).Change(" frontend, design ,, Frontend ");

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal(["frontend", "design"], settings.GitHub.Current.Find("backlog")!.PullRequestLabels);
            Assert.Equal("frontend, design", settings.Component.Find(Field).GetAttribute("value"));
        });
    }

    /// <summary>
    /// The page is the Tasks module's section now, and a section that kept its
    /// settings in fields of its own would lose them on a tab switch the way the
    /// Devices page once did. What the card shows is read from the settings store:
    /// the labels saved before the switch are there after it, and so is a colour
    /// the store took while another tab was open.
    /// </summary>
    [Fact]
    public void A_saved_label_filter_is_still_shown_after_switching_tabs_and_back()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsRepositoryColourTests.OpenRepositoriesTab(settings.Component);
        settings.Component.Find(Field).Input("frontend, design");
        settings.Component.Find(Field).Change("frontend, design");

        OpenTab(settings.Component, "Storage");
        Assert.Null(settings.GitHub.SetRepositoryColour("backlog", 3));
        SettingsRepositoryColourTests.OpenRepositoriesTab(settings.Component);

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal(["frontend", "design"], settings.GitHub.Current.Find("backlog")!.PullRequestLabels);
            Assert.Equal("frontend, design", settings.Component.Find(Field).GetAttribute("value"));
            Assert.Equal("true", settings.Component.Find("[data-testid='repo-colour-3']").GetAttribute("aria-checked"));
        });
    }

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == title).Click();

    /// <summary>A registry edit is the screen's reset point: a label draft typed and not
    /// yet saved goes with it, so an alias that now names another repository cannot
    /// have the old draft saved onto it.</summary>
    [Fact]
    public void A_registry_edit_resets_an_unsaved_label_draft_to_what_is_stored()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsRepositoryColourTests.OpenRepositoriesTab(settings.Component);
        settings.Component.Find(Field).Input("frontend");
        settings.Component.Find(Field).Change("frontend");
        settings.Component.WaitForAssertion(() => Assert.Equal(["frontend"], settings.GitHub.Current.Find("backlog")!.PullRequestLabels));

        // Typed, not yet saved.
        settings.Component.Find(Field).Input("design");

        settings.Component.Find("[data-testid='github-repos-input']").Input("backlog = JSdotNet/Other");
        settings.Component.Find("[data-testid='github-repos-input']").Change("backlog = JSdotNet/Other");

        settings.Component.WaitForAssertion(() =>
        {
            var stored = settings.GitHub.Current.Find("backlog")!;
            Assert.Equal("JSdotNet/Other", stored.FullName);
            Assert.Equal(string.Join(", ", stored.PullRequestLabels), settings.Component.Find(Field).GetAttribute("value") ?? string.Empty);
        });
    }
}
