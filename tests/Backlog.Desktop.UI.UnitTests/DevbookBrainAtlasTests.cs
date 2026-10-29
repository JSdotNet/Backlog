using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Brain's atlas (local ADR 0027 §6,
/// <c>.devbook/domain/brain/features.md#atlas-edges</c>): the Brain's notes and
/// the links between them, with every <c>repo:</c> link drawn as an edge to a node
/// for the repository chapter it names — labelled with the repository alias, and
/// dashed where this machine cannot follow it. Read from the Brain's own database;
/// the repository is never read.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookBrainAtlasTests : IDisposable
{
    private const string BuildingBlocks = ".devbook/arc42/05-building-block-view.md";

    private const string TwoAreas = "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n| Work | work |\n";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-atlas-tests", Guid.NewGuid().ToString("N"));

    // ----- Reading -------------------------------------------------------------

    [Fact]
    public void A_resolved_repo_link_is_a_solid_edge_labelled_with_the_alias_to_a_node_for_the_chapter()
    {
        var graph = ReadBrain(
            [Note("projects/plan.md", "Plan", "projects")],
            [new DevbookAtlasSourceEdge("related", "projects/plan.md", $"repo:backlog/{BuildingBlocks}#devbook")],
            Resolves("backlog"));

        Assert.True(graph.Available);

        var repository = Assert.Single(graph.Nodes, node => node.RepositoryLink is not null);
        Assert.Equal("repo:backlog/.arc42/05-building-block-view.md#devbook", repository.Id);
        Assert.Equal("backlog", repository.Group);
        Assert.Equal("backlog", repository.Folder);
        Assert.Equal(DevbookAtlasReader.RepositoryChapterKind, repository.Kind);
        Assert.True(repository.RepositoryLink!.Resolved);
        Assert.Null(repository.RepositoryLink.Problem);
        Assert.Equal($"repo:backlog/{BuildingBlocks}#devbook", repository.RepositoryLink.Raw);
        Assert.Equal("active", repository.ToneSlug);
        Assert.Equal(1, repository.InDegree);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal("projects/plan.md", edge.Source);
        Assert.Equal(repository.Id, edge.Target);
        Assert.Equal(DevbookAtlasReader.RepoLinkEdgeKind, edge.Kind);
        Assert.Equal("backlog", edge.Label);
        Assert.False(edge.Dashed);
    }

    [Fact]
    public void An_unresolved_repo_link_is_a_dashed_edge_to_a_hollow_node_that_says_why()
    {
        var graph = ReadBrain(
            [Note("work/standup.md", "Standup", "work")],
            [new DevbookAtlasSourceEdge("repo-link", "work/standup.md", "repo:specmanager/.devbook/domain/specs/domain.md")],
            Resolves("backlog"));

        var repository = Assert.Single(graph.Nodes, node => node.RepositoryLink is not null);
        Assert.False(repository.RepositoryLink!.Resolved);
        Assert.Equal(BrainRepoLinkResolver.NotOnThisMachine, repository.RepositoryLink.Problem);
        Assert.Equal(string.Empty, repository.ToneSlug);
        Assert.Equal("specmanager", repository.Group);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal("specmanager", edge.Label);
        Assert.True(edge.Dashed);
    }

    /// <summary>A note may write one chapter in either devbook layout and the alias
    /// in any case, from <c>related</c> and from prose: one node, and one edge per
    /// note, as every other pair in the atlas is one line.</summary>
    [Fact]
    public void Two_spellings_of_one_chapter_are_one_node_and_one_edge_per_note()
    {
        var graph = ReadBrain(
            [Note("projects/plan.md", "Plan", "projects"), Note("work/standup.md", "Standup", "work")],
            [
                new DevbookAtlasSourceEdge("related", "projects/plan.md", $"repo:backlog/{BuildingBlocks}"),
                new DevbookAtlasSourceEdge("repo-link", "projects/plan.md", "repo:Backlog/.arc42/05-building-block-view.md"),
                new DevbookAtlasSourceEdge("repo-link", "work/standup.md", "repo:BACKLOG/./.devbook/arc42/05-building-block-view.md")
            ],
            Resolves("backlog"));

        var repository = Assert.Single(graph.Nodes, node => node.RepositoryLink is not null);
        Assert.Equal(2, repository.InDegree);
        Assert.Equal(2, graph.Edges.Count);
        Assert.All(graph.Edges, edge => Assert.Equal(repository.Id, edge.Target));
    }

    /// <summary>The renderer joins an edge to its nodes by exact id, so every edge
    /// carries the nodes' own spelling, and one repository is one group whichever
    /// case each link wrote its alias in.</summary>
    [Fact]
    public void Edges_carry_the_nodes_own_ids_and_one_repository_is_one_group()
    {
        var graph = ReadBrain(
            [Note("projects/plan.md", "Plan", "projects"), Note("work/standup.md", "Standup", "work")],
            [
                new DevbookAtlasSourceEdge("related", "projects/plan.md", "Work/Standup.md"),
                new DevbookAtlasSourceEdge("repo-link", "projects/plan.md", "repo:backlog/.arc42/01-introduction-and-goals.md"),
                new DevbookAtlasSourceEdge("repo-link", "work/standup.md", "repo:Backlog/.arc42/05-building-block-view.md")
            ],
            Resolves("backlog"));

        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(graph.Edges, edge =>
        {
            Assert.Contains(edge.Source, ids);
            Assert.Contains(edge.Target, ids);
        });

        var groups = graph.Nodes.Where(node => node.RepositoryLink is not null).Select(node => node.Group).Distinct().ToList();
        Assert.Equal(["backlog"], groups);
    }

    [Fact]
    public void Links_between_notes_are_still_drawn_and_a_malformed_repo_link_is_dropped()
    {
        var graph = ReadBrain(
            [Note("projects/plan.md", "Plan", "projects"), Note("work/standup.md", "Standup", "work")],
            [
                new DevbookAtlasSourceEdge("related", "projects/plan.md", "work/standup.md"),
                new DevbookAtlasSourceEdge("related", "projects/plan.md", "repo:no-path-here")
            ],
            Resolves("backlog"));

        Assert.DoesNotContain(graph.Nodes, node => node.RepositoryLink is not null);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("related", edge.Kind);
        Assert.Null(edge.Label);
        Assert.False(edge.Dashed);
        Assert.Equal(["Projects", "Work"], graph.Nodes.Select(node => node.Group).Order());
    }

    /// <summary>The repository atlas never sees a <c>repo-link</c> edge — a
    /// repository's database stores none — so what it draws is unchanged: no label
    /// and no dash on any edge.</summary>
    [Fact]
    public void The_repository_atlas_draws_no_label_and_no_dash()
    {
        var graph = DevbookAtlasReader.Read(
            DevbookAtlasScope.All,
            [
                new DevbookAtlasSourceNode(".domain/a.md", "A", "domain", "file", "active", ".domain/a.md", false),
                new DevbookAtlasSourceNode(".domain/b.md", "B", "domain", "file", "active", ".domain/b.md", false)
            ],
            [new DevbookAtlasSourceEdge("related", ".domain/a.md", ".domain/b.md")]);

        var edge = Assert.Single(graph.Edges);
        Assert.Null(edge.Label);
        Assert.False(edge.Dashed);
        Assert.All(graph.Nodes, node => Assert.Null(node.RepositoryLink));
    }

    // ----- The service, over a Brain database the builder wrote -------------------

    [Fact]
    public async Task The_service_reads_the_brains_own_database_and_resolves_against_the_repositories_configured_here()
    {
        var brain = WriteBrain();
        BuildDatabase(brain);
        var service = new DevbookAtlasService(new FixedFolders("backlog")) { BrainDatabaseFor = DatabaseFor };

        var graph = await service.ReadBrainAsync(new FixedBrain(brain), TestContext.Current.CancellationToken);

        Assert.True(graph.Available);
        Assert.True(graph.Scope.IsBrain);

        var solid = Assert.Single(graph.Edges, edge => edge.Label is "backlog" or "Backlog");
        Assert.Equal("projects/plan.md", solid.Source);
        Assert.False(solid.Dashed);

        var dashed = Assert.Single(graph.Edges, edge => edge.Label == "specmanager");
        Assert.Equal("work/standup.md", dashed.Source);
        Assert.True(dashed.Dashed);

        Assert.Contains(graph.Edges, edge => edge.Kind == "related" && edge.Target == "work/standup.md");
        Assert.Equal(2, graph.Nodes.Count(node => node.RepositoryLink is not null));
    }

    [Fact]
    public async Task With_no_brain_folder_or_no_index_the_atlas_says_so()
    {
        var service = new DevbookAtlasService(new FixedFolders()) { BrainDatabaseFor = DatabaseFor };

        var unset = await service.ReadBrainAsync(new FixedBrain(null), TestContext.Current.CancellationToken);
        Assert.False(unset.Available);
        Assert.Equal(DevbookAtlasService.NoBrainFolder, unset.Message);

        var unbuilt = await service.ReadBrainAsync(new FixedBrain(WriteBrain()), TestContext.Current.CancellationToken);
        Assert.False(unbuilt.Available);
        Assert.Equal(DevbookAtlasService.BrainIndexMissing, unbuilt.Message);
    }

    // ----- The pane ------------------------------------------------------------------

    /// <summary>The Brain offers the atlas; opened, it draws the Brain's map, and a
    /// repository chapter in it opens that repository's devbook the way a chip in
    /// a note does.</summary>
    [Fact]
    public async Task The_brain_pane_offers_the_atlas_and_a_repository_chapter_in_it_opens_that_repository()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, TwoAreas);
        harness.WriteNote("projects/plan.md", $"# Plan\n\nSee [the blocks](repo:{harness.RepositoryAlias}/.arc42/01-introduction-and-goals.md).\n");
        harness.WriteNote("work/standup.md", "# Standup\n\nRead [a spec](repo:specmanager/.devbook/domain/specs/domain.md).\n");
        BuildDatabase(harness.BrainFolder);
        harness.Context.Services.AddSingleton(sp => new DevbookAtlasService(sp.GetRequiredService<IDevbookFolderSource>()) { BrainDatabaseFor = DatabaseFor });

        BrainRepoLink? opened = null;
        var component = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, string.Empty)
            .Add(pane => pane.OnOpenRepositoryChapter, (BrainRepoLink link) => opened = link));

        component.WaitForAssertion(() => Assert.Contains("Projects", DevbookPaneBrainHarness.Tabs(component)));

        component.Find("[data-testid='devbook-atlas-toggle']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Brain atlas", component.Find("[data-testid='devbook-atlas']").GetAttribute("aria-label"));
            Assert.Equal(4, component.FindAll("[data-testid='graph-atlas-index-option']").Count);
        });

        var unresolved = component.Find("[data-testid='graph-atlas-index-option'][data-node-id^='repo:specmanager/']");
        unresolved.Click();
        component.WaitForAssertion(() =>
            Assert.Contains(BrainRepoLinkResolver.NotOnThisMachine, component.Find("[data-testid='devbook-atlas-repo-link']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='devbook-atlas-repo-link'] button"));

        var resolved = component.Find($"[data-testid='graph-atlas-index-option'][data-node-id^='repo:{harness.RepositoryAlias}/']");
        resolved.Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-atlas-repo-link'] button")));

        component.Find("[data-testid='devbook-atlas-repo-link'] button").Click();

        Assert.NotNull(opened);
        Assert.Equal(harness.RepositoryAlias, opened.Alias);
        Assert.Equal(".arc42/01-introduction-and-goals.md", opened.Path);
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-atlas']")));
    }

    /// <summary>A Brain note followed out of the map opens in the pane, on its own
    /// area's tab.</summary>
    [Fact]
    public async Task A_note_opened_from_the_brain_atlas_opens_in_the_pane()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, TwoAreas);
        harness.WriteNote("projects/plan.md", "# Plan\n\nThe plan.\n");
        harness.WriteNote("work/standup.md", "# Standup\n\n```meta\nrelated: [projects/plan.md]\n```\n\nToday.\n");
        BuildDatabase(harness.BrainFolder);
        harness.Context.Services.AddSingleton(sp => new DevbookAtlasService(sp.GetRequiredService<IDevbookFolderSource>()) { BrainDatabaseFor = DatabaseFor });

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.Contains("Work", DevbookPaneBrainHarness.Tabs(component)));

        component.Find("[data-testid='devbook-atlas-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option'][data-node-id='work/standup.md']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='work/standup.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='devbook-atlas-open-note']").Click());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-atlas']"));
            Assert.Contains("Today.", component.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
        });
    }

    // ----- Fixtures --------------------------------------------------------------

    private static DevbookAtlasGraph ReadBrain(
        IReadOnlyList<DevbookAtlasSourceNode> nodes,
        IReadOnlyList<DevbookAtlasSourceEdge> edges,
        Func<BrainRepoLink, BrainRepoLinkResolution> resolve) =>
        DevbookAtlasReader.ReadBrain(DevbookAtlasScope.Brain, nodes, edges, resolve);

    private static DevbookAtlasSourceNode Note(string path, string label, string folder) =>
        new(path, label, folder, "file", string.Empty, path, false);

    private static Func<BrainRepoLink, BrainRepoLinkResolution> Resolves(params string[] aliases) =>
        new BrainRepoLinkResolver(new FixedFolders(aliases)).Resolve;

    private string DatabaseFor(string brainRoot) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), brainRoot)!;

    private void BuildDatabase(string brainRoot)
    {
        var target = DatabaseFor(brainRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Assert.True(DevbookDatabaseBuilder.Build(brainRoot, target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));
    }

    private string WriteBrain()
    {
        var root = Path.Combine(_scratch, "Brain" + Guid.NewGuid().ToString("N")[..6]);
        Write(root, BrainConventions.FileName, TwoAreas);
        Write(root, "projects/plan.md", $$"""
            # Plan

            ```meta
            related: [repo:backlog/{{BuildingBlocks}}#devbook, work/standup.md]
            ```

            See [the building blocks](repo:Backlog/.arc42/05-building-block-view.md#devbook).
            """);
        Write(root, "work/standup.md", """
            # Standup

            Read [a spec](repo:specmanager/.devbook/domain/specs/domain.md).
            """);
        return root;
    }

    private static void Write(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The repositories configured on this machine, by alias.</summary>
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

    /// <summary>A Brain at a fixed folder, or none.</summary>
    private sealed class FixedBrain(string? root) : IBrainFolderSource
    {
        public string? BrainRoot => root;

        public bool BrainFolderPresent() => root is not null;

        public event Action? Changed { add { } remove { } }

        public void NotifyContentChanged()
        {
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => [];

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            DevbookFolderLocation.Unavailable(key, "Not used here.");

        public IReadOnlyList<BrainArea> Areas() => [];

        public IReadOnlyList<BrainArea> HiddenAreas() => [];

        public IReadOnlyList<BrainConflictCopy> ConflictCopies() => [];
    }
}
