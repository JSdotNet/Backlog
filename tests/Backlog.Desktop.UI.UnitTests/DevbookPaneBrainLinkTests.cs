using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Brain note's links into repository devbooks, and their reverse (local ADR 0027
/// §6, <c>.devbook/domain/devbook/features.md#repo-links-and-backlinks</c>): a
/// <c>repo:</c> link this machine can follow is a gold chip that opens that
/// repository's devbook at the chapter; one it cannot is a dashed chip reading
/// "not on this machine", never an error; and a repository chapter shows its owner
/// the Brain notes linking to it, From your Brain.
/// </summary>
public sealed class DevbookPaneBrainLinkTests
{
    private const string Introduction = ".arc42/01-introduction-and-goals.md";

    [Fact]
    public void A_configured_alias_with_its_section_resolves()
    {
        var resolver = new BrainRepoLinkResolver(new FixedFolders("backlog"));

        var resolution = resolver.Resolve("repo:backlog/.devbook/arc42/05-building-block-view.md#devbook");

        Assert.NotNull(resolution);
        Assert.True(resolution.Resolved);
        Assert.Null(resolution.Problem);
        Assert.Equal(".devbook/arc42/05-building-block-view.md#devbook", resolution.Link.Reference);
    }

    [Fact]
    public void An_alias_this_machine_has_not_got_is_unresolved_and_says_so()
    {
        var resolver = new BrainRepoLinkResolver(new FixedFolders("backlog"));

        var resolution = resolver.Resolve("repo:specmanager/.devbook/domain/specs/domain.md");

        Assert.NotNull(resolution);
        Assert.False(resolution.Resolved);
        Assert.Equal("not on this machine", resolution.Problem);
    }

    [Fact]
    public void A_path_in_no_devbook_section_is_unresolved_too()
    {
        var resolver = new BrainRepoLinkResolver(new FixedFolders("backlog"));

        var resolution = resolver.Resolve("repo:backlog/docs/handbook.md");

        Assert.NotNull(resolution);
        Assert.False(resolution.Resolved);
        Assert.Equal(BrainRepoLinkResolver.NotADevbookChapter, resolution.Problem);
        Assert.Null(resolver.Resolve("https://example.com"));
    }

    [Fact]
    public async Task A_note_draws_its_repo_links_as_chips_gold_when_resolved_and_dashed_when_not()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", $$"""
            # Plan

            ```meta
            related: [repo:{{harness.RepositoryAlias}}/{{Introduction}}]
            ```

            See [the goals](repo:{{harness.RepositoryAlias}}/{{Introduction}}#goals) and [their spec](repo:specmanager/.devbook/domain/specs/domain.md).
            """);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Equal(3, component.FindAll("[data-testid='brain-repo-link']").Count));

        var chips = component.FindAll("[data-testid='brain-repo-link']");

        // The note's own `related`, above the body, under the chapter's short label.
        var related = chips[0];
        Assert.Contains("repo-link-chip--resolved", related.ClassList);
        Assert.Equal(harness.RepositoryAlias, related.QuerySelector(".repo-link-chip__alias")!.TextContent.Trim());
        Assert.NotNull(component.Find("[data-testid='brain-note-repo-links']").QuerySelector("[data-testid='brain-repo-link']"));

        // In prose, under the author's own words.
        var goals = chips[1];
        Assert.Contains("repo-link-chip--resolved", goals.ClassList);
        Assert.Equal("the goals", goals.QuerySelector(".repo-link-chip__label")!.TextContent.Trim());
        Assert.NotNull(goals.QuerySelector("button.repo-link-chip__open"));

        // Another machine's repository: dashed, in words, and nothing to press.
        var spec = chips[2];
        Assert.Contains("repo-link-chip--unresolved", spec.ClassList);
        Assert.Equal("not on this machine", spec.QuerySelector(".repo-link-chip__problem")!.TextContent.Trim());
        Assert.Null(spec.QuerySelector("button"));
        Assert.Empty(component.FindAll(".alert--error"));
    }

    [Fact]
    public async Task Following_a_gold_chip_asks_for_the_repository_chapter()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", $"# Plan\n\nSee [the goals](repo:{harness.RepositoryAlias}/{Introduction}#goals).\n");

        var asked = new List<BrainRepoLink>();
        var component = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, string.Empty)
            .Add(pane => pane.OnOpenRepositoryChapter, (BrainRepoLink link) => asked.Add(link)));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='brain-repo-link'] button")));

        component.Find("[data-testid='brain-repo-link'] button").Click();

        component.WaitForAssertion(() => Assert.Single(asked));
        var link = asked[0];
        Assert.Equal(harness.RepositoryAlias, link.Alias);
        Assert.Equal($"{Introduction}#goals", link.Reference);
    }

    [Fact]
    public async Task A_repository_chapter_shows_the_brain_notes_linking_to_it_to_its_owner()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        var backlinks = new FixedBacklinks(harness.RepositoryAlias, Introduction,
            [new BrainBacklink("work/plan.md", "Plan", null), new BrainBacklink("work/standup.md", "Standup", "Yesterday")]);
        harness.Context.Services.AddSingleton<IBrainBacklinks>(backlinks);

        var component = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, harness.RepositoryAlias)
            .Add(pane => pane.OpenReference, new DevbookOpenRequest(Introduction, 1)));

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='brain-backlink']").Count));

        var box = component.Find("[data-testid='brain-backlinks']");
        Assert.Equal("From your Brain", box.QuerySelector(".brain-backlinks__title")!.TextContent.Trim());
        Assert.Equal("Only you see this", component.Find("[data-testid='brain-backlinks-private']").TextContent.Trim());
        Assert.Equal(["work/plan.md", "work/standup.md"], component.FindAll("[data-testid='brain-backlink']").Select(item => item.GetAttribute("data-note")));
        Assert.Contains("Yesterday", component.FindAll("[data-testid='brain-backlink']")[1].TextContent, StringComparison.Ordinal);
        Assert.Contains((harness.RepositoryAlias, Introduction), backlinks.Asked);
    }

    [Fact]
    public async Task A_chapter_nothing_links_to_draws_no_box_and_the_brain_draws_none_either()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nNothing linked.\n");
        harness.Context.Services.AddSingleton<IBrainBacklinks>(new FixedBacklinks("elsewhere", "nowhere.md", [new BrainBacklink("work/plan.md", "Plan", null)]));

        var repository = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, harness.RepositoryAlias)
            .Add(pane => pane.OpenReference, new DevbookOpenRequest(Introduction, 1)));
        repository.WaitForAssertion(() => Assert.Contains("Introduction", repository.Markup, StringComparison.Ordinal));
        Assert.Empty(repository.FindAll("[data-testid='brain-backlinks']"));

        var brain = harness.RenderBrain();
        brain.WaitForAssertion(() => Assert.Contains("Nothing linked.", brain.Markup, StringComparison.Ordinal));
        Assert.Empty(brain.FindAll("[data-testid='brain-backlinks']"));
    }

    /// <summary>Every alias in <paramref name="aliases"/> is a repository with the
    /// default devbook folders; any other has none.</summary>
    private sealed class FixedFolders(params string[] aliases) : IDevbookFolderSource
    {
        public event Action? Changed { add { } remove { } }

        public void NotifyContentChanged()
        {
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
            aliases.Contains(repositoryAlias, StringComparer.OrdinalIgnoreCase) ? DevbookFolderSetting.Defaults() : [];

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            DevbookFolderLocation.Unavailable(key, "Not used here.");
    }

    /// <summary>The backlinks of one chapter, and every question asked.</summary>
    private sealed class FixedBacklinks(string alias, string chapterPath, IReadOnlyList<BrainBacklink> links) : IBrainBacklinks
    {
        public List<(string Alias, string Path)> Asked { get; } = [];

        public Task<IReadOnlyList<BrainBacklink>> LinksToAsync(string repositoryAlias, string path, CancellationToken cancellationToken = default)
        {
            lock (Asked) Asked.Add((repositoryAlias, path));

            return Task.FromResult(
                string.Equals(repositoryAlias, alias, StringComparison.OrdinalIgnoreCase) && string.Equals(path, chapterPath, StringComparison.OrdinalIgnoreCase)
                    ? links
                    : (IReadOnlyList<BrainBacklink>)[]);
        }
    }
}
