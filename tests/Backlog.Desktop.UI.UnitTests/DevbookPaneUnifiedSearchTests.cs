using Backlog.Infrastructure.Devbook;

using Bunit;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook pane's search over the Brain and the repository at once
/// (<c>.devbook/domain/devbook/requirements.md#unified-search</c>), against real
/// databases the builder writes for the harness's Brain folder and repository
/// clone — so what is asserted is the pane wired to retrieval, not to a fake.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookPaneUnifiedSearchTests
{
    private const string Word = "retrospective";

    /// <summary>Scenario: Everywhere — the Brain group comes first and the
    /// repository's after it.</summary>
    [Fact]
    public async Task Everywhere_lists_the_brain_group_first_and_the_repository_after_it()
    {
        await using var harness = CreateWithDatabases();

        var component = OpenSearch(harness, harness.RenderBrain());
        Ask(component, Word);

        component.WaitForAssertion(() =>
            Assert.Equal(["brain", harness.RepositoryAlias], Sources(component)));
        Assert.Contains("Everywhere", component.Find("[data-testid='devbook-search'] h3").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The chips are Everywhere, the Brain, and one per repository, in
    /// that order.</summary>
    [Fact]
    public async Task The_chips_are_everywhere_the_brain_and_one_per_repository()
    {
        await using var harness = CreateWithDatabases();

        var component = OpenSearch(harness, harness.Render(harness.RepositoryAlias));

        Assert.Equal(
            ["Everywhere", "Brain", harness.RepositoryAlias],
            component.FindAll("[data-testid='devbook-search-scopes'] button").Select(chip => chip.TextContent.Trim()));
    }

    /// <summary>Scenario: Brain only — under the Brain chip only the Brain note is
    /// listed.</summary>
    [Fact]
    public async Task The_brain_chip_lists_only_the_brain_note()
    {
        await using var harness = CreateWithDatabases();

        var component = OpenSearch(harness, harness.Render(harness.RepositoryAlias));
        component.Find("[data-testid='devbook-search-scope-brain']").Click();
        Ask(component, Word);

        component.WaitForAssertion(() => Assert.Equal(["brain"], Sources(component)));
        Assert.Equal("work/retro.md#retro", component.Find(".devbook-search__address").TextContent.Trim());
    }

    [Fact]
    public async Task A_repository_chip_lists_only_that_repository()
    {
        await using var harness = CreateWithDatabases();

        var component = OpenSearch(harness, harness.RenderBrain());
        component.Find($"[data-testid='devbook-search-scope-{harness.RepositoryAlias}']").Click();
        Ask(component, Word);

        component.WaitForAssertion(() => Assert.Equal([harness.RepositoryAlias], Sources(component)));
    }

    /// <summary>A Brain note found on the Brain opens in place, and the search
    /// closes on it.</summary>
    [Fact]
    public async Task A_brain_result_opens_the_note_on_the_brain()
    {
        await using var harness = CreateWithDatabases();

        var component = OpenSearch(harness, harness.RenderBrain());
        Ask(component, Word);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-source='brain'] .devbook-search__open")));

        component.Find("[data-source='brain'] .devbook-search__open").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-search']"));
            Assert.Contains("A retrospective of the sprint.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A repository chapter found while the pane shows the Brain is handed
    /// to the shell, with where it came from, to switch the pane to it.</summary>
    [Fact]
    public async Task A_result_from_another_devbook_is_handed_to_the_shell()
    {
        await using var harness = CreateWithDatabases();
        var handed = new List<DevbookSearchOpen>();

        var component = OpenSearch(harness, harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, string.Empty)
            .Add(pane => pane.OpenSearchResultElsewhere, (DevbookSearchOpen result) => handed.Add(result))));
        Ask(component, Word);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll($"[data-source='{harness.RepositoryAlias}'] .devbook-search__open")));

        await component.Find($"[data-source='{harness.RepositoryAlias}'] .devbook-search__open").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var result = Assert.Single(handed);
        Assert.Equal(harness.RepositoryAlias, result.Source.Key);
        Assert.Equal(".arc42/02-retro.md#retro", result.Reference);
    }

    /// <summary>A Brain reference handed back by the shell opens the note: the
    /// shell's half of the round trip above, in the other direction.</summary>
    [Fact]
    public async Task A_brain_reference_handed_back_opens_the_note()
    {
        await using var harness = CreateWithDatabases();

        var component = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, string.Empty)
            .Add(pane => pane.OpenReference, new DevbookOpenRequest("work/retro.md", 1)));

        component.WaitForAssertion(() =>
            Assert.Contains("A retrospective of the sprint.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
    }

    /// <summary>A Brain whose database has not been built is named as not
    /// searchable, beside the repository that answered.</summary>
    [Fact]
    public async Task A_brain_with_no_database_is_named_as_not_searchable()
    {
        await using var harness = CreateWithDatabases(brainDatabase: false);

        var component = OpenSearch(harness, harness.Render(harness.RepositoryAlias));
        Ask(component, Word);

        component.WaitForAssertion(() =>
        {
            var unavailable = component.Find("[data-testid='devbook-search-unavailable']");
            Assert.Contains("Brain is not searchable yet", unavailable.TextContent, StringComparison.Ordinal);
            Assert.Equal([harness.RepositoryAlias], Sources(component));
        });
    }

    private static DevbookPaneBrainHarness CreateWithDatabases(bool brainDatabase = true)
    {
        var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote("CONVENTIONS.md", "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/retro.md", $"# Retro\n\nA {Word} of the sprint.\n");

        var clone = Path.Combine(harness.Root, "clone");
        var chapter = Path.Combine(clone, ".arc42", "02-retro.md");
        File.WriteAllText(chapter, $"# Retro\n\nThe {Word} is where the team looks back.\n");

        var ct = TestContext.Current.CancellationToken;
        Assert.True(DevbookDatabaseBuilder.Build(clone, DevbookDatabaseLocation.ForRepositoryRoot(clone)!, ct));

        if (brainDatabase)
        {
            Assert.True(DevbookDatabaseBuilder.Build(harness.BrainFolder, DevbookDatabaseLocation.ForBrainFolder(harness.BrainFolder)!, DevbookCorpusKind.Brain, ct));
        }

        SqliteConnection.ClearAllPools();
        return harness;
    }

    private static IRenderedComponent<DevbookPane> OpenSearch(DevbookPaneBrainHarness harness, IRenderedComponent<DevbookPane> component)
    {
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));
        component.Find("[data-testid='devbook-search-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-input'] input")));
        return component;
    }

    private static void Ask(IRenderedComponent<DevbookPane> component, string query) =>
        component.Find("[data-testid='devbook-search-input'] input").Input(query);

    private static IReadOnlyList<string> Sources(IRenderedComponent<DevbookPane> component) =>
        [.. component.FindAll("[data-testid='devbook-search-group']").Select(group => group.GetAttribute("data-source") ?? string.Empty)];
}
