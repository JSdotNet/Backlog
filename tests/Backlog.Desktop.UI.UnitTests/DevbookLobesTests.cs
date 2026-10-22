using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Lobes atlas's read model (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>):
/// the Brain and every registered devbook in one graph, read from their own
/// databases, each node carrying its scope, its region and its kind.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookLobesTests : IDisposable
{
    private const string Brain = DevbookLobesGraph.BrainScopeKey;
    private const string BacklogScope = "repo:backlog";
    private const string Homelab = "ghost:homelab";

    private const string Conventions = """
        # Conventions

        ## Areas

        | Area | Folder |
        |---|---|
        | Inbox | _inbox |
        | Projects | projects |
        | Areas | areas |
        | Resources | resources |
        | People | people |
        | Journal | journal |
        | Archive | archive |
        | Recipes | recipes |
        """;

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-lobes-tests", Guid.NewGuid().ToString("N"));

    // ----- Brain regions -----------------------------------------------------------

    [Fact]
    public async Task A_brain_note_is_placed_by_its_para_area()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Frontal, Note(graph, "projects/plan.md").Lobe);
        Assert.Equal(DevbookLobe.Parietal, Note(graph, "areas/health.md").Lobe);
        Assert.Equal(DevbookLobe.Temporal, Note(graph, "people/ann.md").Lobe);
        Assert.Equal(DevbookLobe.Cerebellum, Note(graph, "journal/2026-10-22.md").Lobe);
        Assert.Equal(DevbookLobe.Cerebellum, Note(graph, "_inbox/idea.md").Lobe);
    }

    [Fact]
    public async Task The_para_field_wins_over_the_folder()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Frontal, Note(graph, "resources/ref.md").Lobe);
    }

    [Fact]
    public async Task An_area_declared_beyond_para_is_parietal()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Parietal, Note(graph, "recipes/pie.md").Lobe);
    }

    [Fact]
    public async Task Archive_is_the_dust_and_no_region()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Dust, Note(graph, "archive/old.md").Lobe);
        Assert.Equal(1, graph.Scope(Brain)!.LobeCounts[DevbookLobe.Dust]);
    }

    [Fact]
    public async Task The_brain_stem_holds_conventions_and_the_area_roots_and_occipital_the_repository_hubs()
    {
        var graph = await ReadAsync();
        var brain = graph.Nodes.Where(node => node.ScopeKey == Brain).ToList();

        var stem = brain.Where(node => node.Lobe == DevbookLobe.Stem).ToList();
        Assert.All(stem, node => Assert.Equal(DevbookLobesNodeKind.IndexHub, node.Kind));
        Assert.Contains(stem, node => node.Label == BrainConventions.FileName);
        Assert.Contains(stem, node => node.Label == "Recipes");
        Assert.Equal(1 + 8, stem.Count);

        var hubs = brain.Where(node => node.Lobe == DevbookLobe.Occipital).ToList();
        Assert.All(hubs, node => Assert.Equal(DevbookLobesNodeKind.RepositoryHub, node.Kind));
        Assert.Equal(["backlog", "homelab"], hubs.Select(node => node.Label).Order());
        Assert.False(hubs.Single(node => node.Label == "backlog").Dashed);
        Assert.True(hubs.Single(node => node.Label == "homelab").Dashed);
    }

    /// <summary>The para field read straight off the rows, without a database: the
    /// attribute wins over the folder for a note, and a chapter of a note takes
    /// its file's.</summary>
    [Fact]
    public void The_para_attribute_wins_over_the_folder_for_a_note_and_its_chapters()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [
                    new("resources/ref.md", "Ref", "resources", "file", string.Empty, "resources/ref.md", false),
                    new("resources/ref.md#detail", "Detail", "resources", "chapter", string.Empty, "resources/ref.md", false),
                    new("resources/other.md", "Other", "resources", "file", string.Empty, "resources/other.md", false)
                ],
                [],
                new Dictionary<string, string> { ["resources/ref.md"] = "Projects" },
                ["resources"]),
            [],
            Unresolved);

        Assert.Equal(DevbookLobe.Frontal, Note(graph, "resources/ref.md").Lobe);
        Assert.Equal(DevbookLobe.Frontal, Note(graph, "resources/ref.md#detail").Lobe);
        Assert.Equal(DevbookLobe.Parietal, Note(graph, "resources/other.md").Lobe);
    }

    /// <summary>A PARA area kept in a folder of another name is still known by its
    /// name.</summary>
    [Fact]
    public void A_para_area_in_a_folder_of_another_name_keeps_its_region()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [
                    new("work/plan.md", "Plan", "work", "file", string.Empty, "work/plan.md", false),
                    new("contacts/ann.md", "Ann", "contacts", "file", string.Empty, "contacts/ann.md", false)
                ],
                [],
                new Dictionary<string, string> { ["work/plan.md"] = "work", ["contacts/ann.md"] = "contacts" },
                ["work", "contacts"],
                [new BrainArea("Projects", "work", 1), new BrainArea("People", "contacts", 2)]),
            [],
            Unresolved);

        Assert.Equal(DevbookLobe.Frontal, Note(graph, "work/plan.md").Lobe);
        Assert.Equal(DevbookLobe.Temporal, Note(graph, "contacts/ann.md").Lobe);
        Assert.Contains(graph.Nodes, node => node.Kind == DevbookLobesNodeKind.IndexHub && node.Label == "Projects");
    }

    // ----- Devbook regions ---------------------------------------------------------

    [Fact]
    public async Task A_devbook_chapter_is_placed_by_its_folder()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Frontal, Chapter(graph, ".devbook/arc42/adr/0001-use-sqlite.md").Lobe);
        Assert.Equal(DevbookLobe.Parietal, Chapter(graph, ".devbook/tech/docker.md").Lobe);
        Assert.Equal(DevbookLobe.Temporal, Chapter(graph, ".devbook/domain/inbox/domain.md").Lobe);
        Assert.Equal(DevbookLobe.Occipital, Chapter(graph, ".devbook/design/tokens.md").Lobe);
        Assert.Equal(DevbookLobe.Cerebellum, Chapter(graph, ".devbook/ai/prompting.md").Lobe);
        Assert.All(graph.Nodes.Where(node => node.ScopeKey == BacklogScope), node => Assert.Equal(DevbookLobesNodeKind.Chapter, node.Kind));
    }

    [Fact]
    public async Task A_convention_root_is_in_the_stem_not_its_folders_region()
    {
        var graph = await ReadAsync();

        Assert.Equal(DevbookLobe.Stem, Chapter(graph, ".devbook/domain/context-map.md").Lobe);
    }

    [Fact]
    public void The_folder_mapping_covers_every_devbook_folder_in_either_layout()
    {
        Assert.Equal(DevbookLobe.Frontal, DevbookLobesReader.FolderLobe("arc42"));
        Assert.Equal(DevbookLobe.Frontal, DevbookLobesReader.FolderLobe(".arc42"));
        Assert.Equal(DevbookLobe.Parietal, DevbookLobesReader.FolderLobe("tech"));
        Assert.Equal(DevbookLobe.Temporal, DevbookLobesReader.FolderLobe("domain"));
        Assert.Equal(DevbookLobe.Occipital, DevbookLobesReader.FolderLobe("design"));
        Assert.Equal(DevbookLobe.Cerebellum, DevbookLobesReader.FolderLobe("ai"));
        Assert.Equal(DevbookLobe.Stem, DevbookLobesReader.FolderLobe(string.Empty));
    }

    // ----- Cross-devbook resolution ------------------------------------------------

    /// <summary>One note writes the decision record in the devbook layout in
    /// <c>related</c> and in the root layout with the alias capitalised in prose;
    /// another links a chapter of a file. Each lands on the devbook's own node.</summary>
    [Fact]
    public async Task A_repo_link_resolves_to_the_devbooks_own_chapter_node_whichever_spelling()
    {
        var graph = await ReadAsync();
        var record = Chapter(graph, ".devbook/arc42/adr/0001-use-sqlite.md");
        var inbox = Chapter(graph, ".devbook/domain/inbox/domain.md#inbox");

        var toRecord = graph.Edges.Where(edge => edge.Kind == DevbookLobesReader.RepoLinkEdgeKind && edge.Target == record.Id).ToList();
        var link = Assert.Single(toRecord);
        Assert.Equal("brain|projects/plan.md", link.Source);
        Assert.False(link.Dashed);

        Assert.True(record.RepositoryLink!.Resolved);
        Assert.Single(graph.Edges, edge => edge.Kind == DevbookLobesReader.RepoLinkEdgeKind && edge.Target == inbox.Id && !edge.Dashed);
        Assert.DoesNotContain(graph.Nodes, node => node.ScopeKey == BacklogScope && node.Kind == DevbookLobesNodeKind.GhostChapter);

        var hub = graph.Nodes.Single(node => node.Kind == DevbookLobesNodeKind.RepositoryHub && node.Label == "backlog");
        Assert.Equal(2, graph.Edges.Count(edge => edge.Kind == DevbookLobesReader.BundleEdgeKind && edge.Source == hub.Id && !edge.Dashed));
    }

    [Fact]
    public async Task An_unregistered_alias_is_a_ghost_scope_holding_only_the_named_chapters_in_the_links_words()
    {
        var graph = await ReadAsync();

        var scope = graph.Scope(Homelab)!;
        Assert.Equal(DevbookLobesScopeKind.Ghost, scope.Kind);
        Assert.Equal("homelab", scope.Label);
        Assert.False(scope.Available);

        var ghosts = graph.Nodes.Where(node => node.ScopeKey == Homelab).ToList();
        Assert.Equal(2, ghosts.Count);
        Assert.All(ghosts, node =>
        {
            Assert.Equal(DevbookLobesNodeKind.GhostChapter, node.Kind);
            Assert.True(node.Dashed);
            Assert.Equal(BrainRepoLinkResolver.NotOnThisMachine, node.RepositoryLink!.Problem);
        });
        Assert.Equal(["docker", "y"], ghosts.Select(node => node.Label).Order(StringComparer.Ordinal));
        Assert.Equal(DevbookLobe.Parietal, ghosts.Single(node => node.Label == "docker").Lobe);

        Assert.All(graph.Edges.Where(edge => ghosts.Any(node => node.Id == edge.Target)), edge => Assert.True(edge.Dashed));
    }

    /// <summary>A registered repository whose index is not built yet is still its
    /// own scope; the chapters the notes name sit in it dashed.</summary>
    [Fact]
    public void A_registered_devbook_without_an_index_holds_the_named_chapters_dashed()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [new("related", "projects/plan.md", "repo:backlog/.devbook/arc42/05-building-block-view.md")],
                new Dictionary<string, string>(),
                ["projects"]),
            [DevbookLobesDevbookSource.Missing("backlog", "No index yet.")],
            new BrainRepoLinkResolver(new Registered("backlog")).Resolve);

        var scope = graph.Scope(BacklogScope)!;
        Assert.Equal(DevbookLobesScopeKind.Devbook, scope.Kind);
        Assert.False(scope.Available);
        Assert.Equal("No index yet.", scope.Message);

        var node = Assert.Single(graph.Nodes, node => node.ScopeKey == BacklogScope);
        Assert.Equal(DevbookLobesNodeKind.GhostChapter, node.Kind);
        Assert.Equal(DevbookLobe.Frontal, node.Lobe);
        Assert.True(Assert.Single(graph.Edges, edge => edge.Kind == DevbookLobesReader.RepoLinkEdgeKind).Dashed);
    }

    /// <summary>A chapter named in a devbook not indexed yet is not a broken link:
    /// its chip says the index does not exist yet, whatever the resolver made of
    /// the path, and points at the scope card that says the same.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_chapter_of_a_devbook_not_indexed_yet_says_so_rather_than_that_it_is_no_chapter(bool resolves)
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [new("related", "projects/plan.md", "repo:backlog/.devbook/arc42/05-building-block-view.md")],
                new Dictionary<string, string>(),
                ["projects"]),
            [DevbookLobesDevbookSource.Missing("backlog", "No index yet.")],
            link => resolves
                ? new BrainRepoLinkResolution(link, true, null)
                : new BrainRepoLinkResolution(link, false, BrainRepoLinkResolver.NotADevbookChapter));

        var node = Assert.Single(graph.Nodes, node => node.ScopeKey == BacklogScope);
        Assert.True(node.Dashed);
        Assert.False(node.RepositoryLink!.Resolved);
        Assert.Equal(DevbookLobesReader.NotIndexedYet, node.RepositoryLink.Problem);
        Assert.Contains("scope card", DevbookLobesReader.NotIndexedYet, StringComparison.Ordinal);
    }

    /// <summary>A chapter an indexed devbook does not hold still reads as no
    /// chapter of it.</summary>
    [Fact]
    public void A_chapter_an_indexed_devbook_does_not_hold_is_still_no_chapter_in_its_devbook()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [new("related", "projects/plan.md", "repo:backlog/.devbook/arc42/99-missing.md")],
                new Dictionary<string, string>(),
                ["projects"]),
            [
                new DevbookLobesDevbookSource(
                    "backlog",
                    true,
                    null,
                    [new(".arc42/05-building-block-view.md", "Building blocks", "arc42", "file", string.Empty, ".arc42/05-building-block-view.md", false)],
                    [],
                    new HashSet<string>())
            ],
            link => new BrainRepoLinkResolution(link, true, null));

        var ghost = Assert.Single(graph.Nodes, node => node.Kind == DevbookLobesNodeKind.GhostChapter);
        Assert.Equal(BacklogScope, ghost.ScopeKey);
        Assert.Equal(BrainRepoLinkResolver.NotADevbookChapter, ghost.RepositoryLink!.Problem);
    }

    /// <summary>A chapter the devbook's index holds is always its own node — in a
    /// root-layout repository too, whose ids are the folded spelling — and a link
    /// the resolver cannot follow is dashed to it rather than to a ghost beside
    /// it.</summary>
    [Fact]
    public void A_link_that_does_not_resolve_to_an_indexed_chapter_is_dashed_to_that_chapter()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [new("related", "projects/plan.md", "repo:backlog/.devbook/arc42/05-building-block-view.md")],
                new Dictionary<string, string>(),
                ["projects"]),
            [
                new DevbookLobesDevbookSource(
                    "backlog",
                    true,
                    null,
                    [new(".arc42/05-building-block-view.md", "Building blocks", "arc42", "file", string.Empty, ".arc42/05-building-block-view.md", false)],
                    [],
                    new HashSet<string>())
            ],
            link => new BrainRepoLinkResolution(link, false, BrainRepoLinkResolver.NotADevbookChapter));

        var node = Assert.Single(graph.Nodes, node => node.ScopeKey == BacklogScope);
        Assert.Equal(DevbookLobesNodeKind.Chapter, node.Kind);
        Assert.False(node.RepositoryLink!.Resolved);
        Assert.Equal(BrainRepoLinkResolver.NotADevbookChapter, node.RepositoryLink.Problem);
        Assert.True(Assert.Single(graph.Edges, edge => edge.Kind == DevbookLobesReader.RepoLinkEdgeKind).Dashed);
    }

    [Fact]
    public void Two_cases_of_one_ghost_path_are_one_ghost_chapter()
    {
        var graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [
                    new("related", "projects/plan.md", "repo:homelab/.arc42/X.md"),
                    new("repo-link", "projects/plan.md", "repo:HOMELAB/.devbook/arc42/x.md")
                ],
                new Dictionary<string, string>(),
                ["projects"]),
            [],
            Unresolved);

        Assert.Single(graph.Nodes, node => node.ScopeKey == Homelab);
        Assert.Single(graph.Scopes, scope => scope.Kind == DevbookLobesScopeKind.Ghost);
    }

    // ----- Counts --------------------------------------------------------------------

    [Fact]
    public async Task Each_scope_counts_its_regions_its_items_and_the_notes_linking_in()
    {
        var graph = await ReadAsync();

        Assert.Equal([Brain, BacklogScope, Homelab], graph.Scopes.Select(scope => scope.Key));

        var brain = graph.Scope(Brain)!;
        Assert.Equal(8, brain.ItemCount);
        Assert.Equal(2, brain.LobeCounts[DevbookLobe.Frontal]);
        Assert.Equal(2, brain.LobeCounts[DevbookLobe.Parietal]);
        Assert.Equal(1, brain.LobeCounts[DevbookLobe.Temporal]);
        Assert.Equal(2, brain.LobeCounts[DevbookLobe.Occipital]);
        Assert.Equal(2, brain.LobeCounts[DevbookLobe.Cerebellum]);
        Assert.Equal(9, brain.LobeCounts[DevbookLobe.Stem]);
        Assert.Equal(3, brain.LinkingNotes);

        var backlog = graph.Scope(BacklogScope)!;
        Assert.True(backlog.Available);
        Assert.Equal(graph.Nodes.Count(node => node.ScopeKey == BacklogScope), backlog.ItemCount);
        Assert.Equal(backlog.ItemCount, backlog.LobeCounts.Values.Sum());
        Assert.Equal(0, backlog.LobeCounts[DevbookLobe.Dust]);
        Assert.Equal(2, backlog.LinkingNotes);

        var homelab = graph.Scope(Homelab)!;
        Assert.Equal(2, homelab.ItemCount);
        Assert.Equal(1, homelab.LinkingNotes);
    }

    // ----- The service ---------------------------------------------------------------

    [Fact]
    public async Task With_no_brain_folder_or_no_brain_index_the_lobes_read_says_so()
    {
        var service = new DevbookAtlasService(new Registered(Array.Empty<string>())) { BrainDatabaseFor = DatabaseFor };

        var unset = await service.ReadLobesAsync(new FixedBrain(null), [], TestContext.Current.CancellationToken);
        Assert.False(unset.Available);
        Assert.Equal(DevbookAtlasService.NoBrainFolder, unset.Message);

        var unbuilt = await service.ReadLobesAsync(new FixedBrain(Path.Combine(_scratch, "never-built")), [], TestContext.Current.CancellationToken);
        Assert.False(unbuilt.Available);
        Assert.Equal(DevbookAtlasService.BrainIndexMissing, unbuilt.Message);

        // Only the missing index is the absence the atlas offers to mend.
        Assert.True(unbuilt.BrainIndexMissing);
        Assert.False(unset.BrainIndexMissing);
    }

    [Fact]
    public async Task Each_indexed_scope_says_when_its_database_was_built()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var graph = await ReadAsync();

        var brain = graph.Scope(Brain)!.IndexedAt;
        var backlog = graph.Scope(BacklogScope)!.IndexedAt;
        Assert.NotNull(brain);
        Assert.NotNull(backlog);
        Assert.InRange(brain.Value, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.InRange(backlog.Value, before, DateTimeOffset.UtcNow.AddMinutes(1));

        // A ghost has no database of its own here.
        Assert.Null(graph.Scope(Homelab)!.IndexedAt);
    }

    [Fact]
    public async Task The_lobes_source_builds_the_brains_index_through_the_refresher()
    {
        var brain = WriteBrain();
        var cache = Path.Combine(_scratch, "devbook-cache");
        using var refresher = new DevbookDatabaseRefresher(() => cache, watchFolder: (_, _) => null);
        using var services = new ServiceCollection()
            .AddSingleton<IBrainFolderSource>(new FixedBrain(brain))
            .AddSingleton(refresher)
            .BuildServiceProvider();
        var atlas = new DevbookAtlasService(new Registered(Array.Empty<string>())) { BrainDatabaseFor = DatabaseFor };
        var source = new DevbookLobesSource(atlas, new DevbookScope(null!, null!, null!), services);

        Assert.True(source.CanBuildBrainIndex);
        Assert.False(File.Exists(DatabaseFor(brain)));

        Assert.Null(await source.BuildBrainIndexAsync(TestContext.Current.CancellationToken));

        Assert.True(File.Exists(DatabaseFor(brain)));
        var graph = await atlas.ReadLobesAsync(new FixedBrain(brain), [], TestContext.Current.CancellationToken);
        Assert.True(graph.Available, graph.Message);
    }

    [Fact]
    public async Task A_host_without_the_refresher_or_a_brain_cannot_build_and_says_why()
    {
        var atlas = new DevbookAtlasService(new Registered(Array.Empty<string>())) { BrainDatabaseFor = DatabaseFor };

        using (var noRefresher = new ServiceCollection().AddSingleton<IBrainFolderSource>(new FixedBrain(WriteBrain())).BuildServiceProvider())
        {
            var source = new DevbookLobesSource(atlas, new DevbookScope(null!, null!, null!), noRefresher);
            Assert.False(source.CanBuildBrainIndex);
            Assert.Equal(DevbookLobesSource.BuildUnavailable, await source.BuildBrainIndexAsync(TestContext.Current.CancellationToken));
        }

        using var refresher = new DevbookDatabaseRefresher(() => Path.Combine(_scratch, "devbook-cache"), watchFolder: (_, _) => null);
        using var noBrain = new ServiceCollection().AddSingleton<IBrainFolderSource>(new FixedBrain(null)).AddSingleton(refresher).BuildServiceProvider();
        var unset = new DevbookLobesSource(atlas, new DevbookScope(null!, null!, null!), noBrain);
        Assert.False(unset.CanBuildBrainIndex);
        Assert.Equal(DevbookAtlasService.NoBrainFolder, await unset.BuildBrainIndexAsync(TestContext.Current.CancellationToken));
    }

    // ----- Fixtures ------------------------------------------------------------------

    private static readonly Func<BrainRepoLink, BrainRepoLinkResolution> Unresolved =
        new BrainRepoLinkResolver(new Registered(Array.Empty<string>())).Resolve;

    private static DevbookLobesNode Note(DevbookLobesGraph graph, string id) =>
        Assert.Single(graph.Nodes, node => node.Id == $"{Brain}|{id}");

    private static DevbookLobesNode Chapter(DevbookLobesGraph graph, string id) =>
        Assert.Single(graph.Nodes, node => node.Id == $"{BacklogScope}|{id}");

    private async Task<DevbookLobesGraph> ReadAsync()
    {
        var brain = WriteBrain();
        var target = DatabaseFor(brain);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Assert.True(DevbookDatabaseBuilder.Build(brain, target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));

        var repository = WriteRepository();
        var repositoryDatabase = DevbookDatabaseLocation.ForRepositoryRoot(repository)!;
        Directory.CreateDirectory(Path.GetDirectoryName(repositoryDatabase)!);
        Assert.True(DevbookDatabaseBuilder.Build(repository, repositoryDatabase, TestContext.Current.CancellationToken));

        var service = new DevbookAtlasService(new Registered(("backlog", repository))) { BrainDatabaseFor = DatabaseFor };
        var graph = await service.ReadLobesAsync(new FixedBrain(brain), ["backlog"], TestContext.Current.CancellationToken);

        Assert.True(graph.Available, graph.Message);
        return graph;
    }

    private string WriteBrain()
    {
        var root = Path.Combine(_scratch, "Brain");
        Write(root, BrainConventions.FileName, Conventions);
        Write(root, "projects/plan.md", """
            # Plan

            ```meta
            related: [repo:backlog/.devbook/arc42/adr/0001-use-sqlite.md]
            ```

            We chose [SQLite](repo:Backlog/.arc42/adr/0001-use-sqlite.md).
            """);
        Write(root, "resources/ref.md", """
            # Ref

            ```meta
            para: projects
            ```

            Filed under resources, but a project.
            """);
        Write(root, "areas/health.md", "# Health\n\nSee [the inbox](repo:backlog/.devbook/domain/inbox/domain.md#inbox).\n");
        Write(root, "recipes/pie.md", "# Pie\n\nApples.\n");
        Write(root, "archive/old.md", "# Old\n\nGone.\n");
        Write(root, "people/ann.md", "# Ann\n\nA colleague.\n");
        Write(root, "journal/2026-10-22.md", "# 22 October\n\nToday.\n");
        Write(root, "_inbox/idea.md", """
            # Idea

            Run [docker](repo:homelab/.devbook/tech/docker.md), as [Y](repo:homelab/.devbook/arc42/x.md#y)
            and [again](repo:HomeLab/.arc42/x.md#y) say.
            """);
        return root;
    }

    private string WriteRepository()
    {
        var root = Path.Combine(_scratch, "backlog");
        Write(root, ".devbook/arc42/01-introduction-and-goals.md", "# Introduction\n\nGoals.\n");
        Write(root, ".devbook/arc42/adr/README.md", "# Decisions\n\nThe records.\n");
        Write(root, ".devbook/arc42/adr/0001-use-sqlite.md", "# Use SQLite\n\nDecided.\n");
        Write(root, ".devbook/domain/context-map.md", "# Context map\n\nContexts.\n");
        Write(root, ".devbook/domain/inbox/context.md", "# Inbox\n\nThe context.\n");
        Write(root, ".devbook/domain/inbox/domain.md", """
            # Inbox domain

            ## Inbox

            ```meta
            type: aggregate
            ```

            Captures.
            """);
        Write(root, ".devbook/tech/technology-graph.md", "# Technology graph\n\nThe graph.\n");
        Write(root, ".devbook/tech/docker.md", "# Docker\n\nContainers.\n");
        Write(root, ".devbook/design/README.md", "# Design\n\nPrinciples.\n");
        Write(root, ".devbook/design/tokens.md", "# Tokens\n\nColours.\n");
        Write(root, ".devbook/ai/adoption-map.md", "# Adoption map\n\nStages.\n");
        Write(root, ".devbook/ai/prompting.md", "# Prompting\n\nHow.\n");
        return root;
    }

    private string DatabaseFor(string brainRoot) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), brainRoot)!;

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
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The repositories registered on this machine: an alias, and where
    /// its clone is when it has one.</summary>
    private sealed class Registered(params (string Alias, string? Root)[] repositories) : IDevbookFolderSource
    {
        public Registered(params string[] aliases)
            : this([.. aliases.Select(alias => (alias, (string?)null))])
        {
        }

        public event Action? Changed { add { } remove { } }

        public void NotifyContentChanged()
        {
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
            repositories.Any(repository => string.Equals(repository.Alias, repositoryAlias, StringComparison.OrdinalIgnoreCase))
                ? DevbookFolderSetting.Defaults()
                : [];

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var repository = repositories.FirstOrDefault(repository => string.Equals(repository.Alias, repositoryAlias, StringComparison.OrdinalIgnoreCase));
            var setting = Folders(repositoryAlias).FirstOrDefault(folder => folder.Key == key);

            return repository.Root is null || setting is null || setting.DefaultRelativePath.Length == 0
                ? DevbookFolderLocation.Unavailable(key, "Not on disk.")
                : new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", setting, Path.Combine(repository.Root, setting.DefaultRelativePath), repository.Root);
        }
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

        public IReadOnlyList<BrainArea> Areas() => BrainConventions.Read(root);

        public IReadOnlyList<BrainConflictCopy> ConflictCopies() => [];
    }
}
