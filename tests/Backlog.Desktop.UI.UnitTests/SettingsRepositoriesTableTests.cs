using System.Text.RegularExpressions;

using Backlog.Desktop.UI.Shell;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Repositories page as a table: one row per repository saying where it lives
/// and what it reads, and the repository's card opened in place under its row by
/// Edit. What each control on the card does is the other suites' subject; this one
/// pins the table, the headline over it, and the one-row-open rule.
/// </summary>
public sealed class SettingsRepositoriesTableTests
{
    private const string EditButtons = "[data-testid='repo-edit']";

    [Fact]
    public void The_headline_counts_the_repositories_and_those_with_a_devbook()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");

        Assert.Equal(
            "1 repository registered, 0 with a devbook.",
            settings.Component.Find("[data-testid='settings-headline']").TextContent.Trim());

        Retype(settings.Component, "JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        settings.Component.WaitForAssertion(() => Assert.Equal(
            "2 repositories registered, 0 with a devbook.",
            settings.Component.Find("[data-testid='settings-headline']").TextContent.Trim()));
    }

    [Fact]
    public void The_summary_puts_a_space_between_its_two_sentences()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");
        Retype(settings.Component, "backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        // Whitespace runs collapse to one space in the browser, so collapse them here
        // too: what must not happen is no whitespace at all between "more." and "Type".
        settings.Component.WaitForAssertion(() => Assert.StartsWith(
            "JSdotNet/Backlog is configured, and 1 more. Type @backlog on an entry",
            Regex.Replace(settings.Component.Find("[data-testid='github-repos-status']").TextContent, @"\s+", " ").Trim(),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_clone_with_a_devbook_is_counted_and_named_in_its_row()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        var clone = Path.Combine(settings.Root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".devbook", "arc42"));
        Directory.CreateDirectory(Path.Combine(clone, ".devbook", "domain"));

        SettingsMenu.OpenRepository(settings.Component, "backlog");

        // Found and raised on the renderer's thread: the folder typed is checked
        // with git in the background, and that answer re-renders the card.
        await settings.Component.InvokeAsync(() => settings.Component.Find("[data-testid='repo-clone-directory-input']").Input(clone));
        await settings.Component.InvokeAsync(() => settings.Component.Find("[data-testid='repo-clone-directory-input']").Change(clone));

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "1 repository registered, 1 with a devbook.",
                settings.Component.Find("[data-testid='settings-headline']").TextContent.Trim());
            Assert.Equal(".devbook", settings.Component.Find("[data-testid='repo-table-devbook']").TextContent.Trim());
            Assert.Equal(clone, settings.Component.Find("[data-testid='repo-table-folder']").TextContent.Trim());
        });
    }

    /// <summary>
    /// The clone folder's status is a git question, asked once per folder off
    /// the render rather than three times inside every render, and the answer
    /// lands on the card when it arrives - with the Clone button following it.
    /// </summary>
    [Fact]
    public async Task The_clone_status_arrives_from_the_background_and_wakes_the_clone_button()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        var target = Path.Combine(settings.Root, "fresh-clone");

        SettingsMenu.OpenRepository(settings.Component, "backlog");
        await settings.Component.InvokeAsync(() => settings.Component.Find("[data-testid='repo-clone-directory-input']").Input(target));
        await settings.Component.InvokeAsync(() => settings.Component.Find("[data-testid='repo-clone-directory-input']").Change(target));

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Contains($"Not cloned yet: {target}", settings.Component.Find("[data-testid='repo-clone-status']").TextContent, StringComparison.Ordinal);
            var clone = settings.Component.Find("[data-testid='repo-clone-button']");
            Assert.False(clone.HasAttribute("disabled"));
            Assert.Equal("Clone repository", clone.TextContent.Trim());
        });
    }

    [Fact]
    public void The_card_is_titled_and_described_and_offers_Add_repository_in_its_header()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");

        var card = settings.Component.Find("[data-testid='repositories-card']");

        Assert.Equal("Repositories", card.QuerySelector(".settings-card__title")!.TextContent.Trim());
        Assert.Equal(
            "The repositories whose devbooks, sessions and pull requests Backlog reads.",
            card.QuerySelector(".settings-card__description")!.TextContent.Trim());
        Assert.NotNull(card.QuerySelector(".settings-card__action [data-testid='add-repository-button']"));
    }

    [Fact]
    public void Each_row_says_alias_folder_devbook_and_account_with_the_band_colour()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        Assert.Null(settings.GitHub.SetRepositoryColour("backlog", 3));
        SettingsMenu.Open(settings.Component, "Repositories");

        Assert.Equal(
            ["Alias", "Local folder", "Devbook folder", "GitHub account", "Actions"],
            settings.Component.FindAll("[data-testid='repo-table-table'] thead th").Select(th => th.TextContent.Trim()));

        var row = settings.Component.Find("[data-testid='repo-table-table'] tbody tr");
        Assert.Equal("backlog", row.QuerySelector(".repo-table__alias")!.TextContent.Trim());
        Assert.Contains("repo-swatch--3", row.QuerySelector(".repo-table__mark")!.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", row.QuerySelector(".repo-table__mark")!.GetAttribute("aria-hidden"));
        Assert.Equal("Not set", row.QuerySelector("[data-testid='repo-table-folder']")!.TextContent.Trim());
        Assert.Contains("data-table__mono", row.QuerySelector("[data-testid='repo-table-folder']")!.ClassName, StringComparison.Ordinal);
        Assert.Equal("None", row.QuerySelector("[data-testid='repo-table-devbook']")!.TextContent.Trim());
        Assert.Equal("Default", row.QuerySelector("[data-testid='repo-table-account']")!.TextContent.Trim());
    }

    [Fact]
    public void Edit_opens_the_card_under_its_row_and_Done_closes_it()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");

        var edit = settings.Component.Find(EditButtons);
        Assert.Equal("Edit backlog", edit.GetAttribute("aria-label"));
        Assert.Equal("false", edit.GetAttribute("aria-expanded"));
        Assert.Equal("Edit", edit.TextContent.Trim());
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repo-card']"));

        edit.Click();

        edit = settings.Component.Find(EditButtons);
        Assert.Equal("true", edit.GetAttribute("aria-expanded"));
        Assert.Equal("Done", edit.TextContent.Trim());

        // In place: the card is in the full-width row right under the repository's own.
        var detail = settings.Component.Find("[data-testid='repo-table-table'] .data-table__row-detail");
        Assert.Equal("5", detail.QuerySelector("td")!.GetAttribute("colspan"));
        Assert.NotNull(detail.QuerySelector("[data-testid='github-repo-card']"));

        edit.Click();

        Assert.Equal("false", settings.Component.Find(EditButtons).GetAttribute("aria-expanded"));
        Assert.Empty(settings.Component.FindAll("[data-testid='github-repo-card']"));
    }

    [Fact]
    public void One_row_is_open_at_a_time()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");
        Retype(settings.Component, "backlog = JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        SettingsMenu.EditRepository(settings.Component, "backlog");
        SettingsMenu.EditRepository(settings.Component, "docs");

        Assert.Equal(
            ["false", "true"],
            settings.Component.FindAll(EditButtons).Select(button => button.GetAttribute("aria-expanded")));
        var card = Assert.Single(settings.Component.FindAll("[data-testid='github-repo-card']"));
        Assert.Equal("JSdotNet/Docs repository settings", card.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_repository_just_added_opens_its_row()
    {
        using var settings = SettingsRepositoryColourTests.RenderSettings();
        SettingsMenu.Open(settings.Component, "Repositories");

        settings.Component.Find("[data-testid='add-repository-button']").Click();
        Retype(settings.Component, "JSdotNet/Backlog\ndocs = JSdotNet/Docs");

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal("true", settings.Component.Find("[data-testid='repo-edit'][aria-label='Edit docs']").GetAttribute("aria-expanded"));
            Assert.Equal(
                "JSdotNet/Docs repository settings",
                Assert.Single(settings.Component.FindAll("[data-testid='github-repo-card']")).GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void The_page_declares_its_card_to_the_settings_search() =>
        Assert.Equal(
            ["Repositories"],
            SettingsPages.BuiltIn.Single(page => page.Id == SettingsPages.Repositories).CardTitles!);

    private static void Retype(IRenderedComponent<Settings> component, string text)
    {
        component.Find("[data-testid='github-repos-input']").Input(text);
        component.Find("[data-testid='github-repos-input']").Change(text);
    }
}
