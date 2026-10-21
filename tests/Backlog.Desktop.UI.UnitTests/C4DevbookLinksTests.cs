using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The links devbook 1.28.0 writes from a record to the C4 model, as
/// <see cref="C4DevbookStore"/> resolves them into the <see cref="C4Catalog"/>.
/// <para>
/// A record names the elements it is about in <c>c4</c> and the ones its
/// consequences name in <c>c4-touches</c>; a building block names its code folders in
/// <c>code</c>. The model never stores any of it — an element's records and code are
/// read back along those fields — so the catalog is where both directions are
/// answered, and an identifier the workspace does not define is said out loud rather
/// than dropped.
/// </para>
/// </summary>
public sealed class C4DevbookLinksTests : IDisposable
{
    private const string Workspace = """
        workspace "Linked" "A workspace for the link tests" {
            !identifiers hierarchical
            model {
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        devbook = component "Devbook" "The devbook panels"
                    }
                    store = container "Local Task Store" "Canonical" "SQLite"
                }
                github = softwareSystem "GitHub" "Issues"
            }
            views {
                container backlog "containers" "Containers" { include * }
            }
        }
        """;

    private const string Adr = """
        # ADR 0001: Desktop stack

        ```meta
        c4: [backlog.desktop]
        c4-touches: [backlog.store, github]
        ```

        The desktop is MAUI.
        """;

    private const string Tdr = """
        # TDR 0001: Renders vanish on a snapshot

        ```meta
        date: 2026-09-16
        severity: high
        status: planned
        c4: [backlog.desktop.devbook]
        ```

        Prose.
        """;

    private const string BuildingBlock = """
        # Devbook

        ```meta
        c4: [backlog.desktop.devbook]
        code: [src/Modules/Devbook, src/Infrastructure/Backlog.Infrastructure.Devbook]
        ```

        The panels.
        """;

    private const string DomainChapter = """
        # Devbook context

        ```meta
        type: domain
        ```

        ## Reading

        ```meta
        type: feature
        c4-touches: [backlog.desktop.devbook]
        ```

        A section about one element.
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task An_element_lists_the_records_that_name_it_by_kind()
    {
        var catalog = await LoadAsync(new()
        {
            ["arc42/adr/0001-desktop.md"] = Adr,
            ["arc42/tdr/0001-renders.md"] = Tdr,
        });

        var about = catalog.RecordsOf("backlog.desktop", C4LinkKind.About);
        var link = Assert.Single(about);
        Assert.Equal("ADR 0001: Desktop stack", link.Record.Title);
        Assert.Equal(C4RecordKind.Decision, link.Record.Kind);
        Assert.EndsWith("arc42/adr/0001-desktop.md", link.Record.Path, StringComparison.Ordinal);

        Assert.Empty(catalog.RecordsOf("backlog.desktop", C4LinkKind.Touches));

        var touches = catalog.RecordsOf("github", C4LinkKind.Touches);
        Assert.Equal("ADR 0001: Desktop stack", Assert.Single(touches).Record.Title);

        // With no kind asked, both.
        Assert.Single(catalog.RecordsOf("backlog.store"));
    }

    [Fact]
    public async Task A_record_lists_its_elements_with_the_kind_of_each_link()
    {
        var catalog = await LoadAsync(new() { ["arc42/adr/0001-desktop.md"] = Adr });
        var path = catalog.Links[0].Record.Path;

        var elements = catalog.ElementsOf(path);

        Assert.Equal(
            ["about backlog.desktop", "touches backlog.store", "touches github"],
            elements.Select(link => $"{(link.Kind == C4LinkKind.About ? "about" : "touches")} {link.ElementId}"));
    }

    [Fact]
    public async Task A_record_is_found_by_either_layouts_spelling_of_its_path()
    {
        var catalog = await LoadAsync(new() { ["arc42/adr/0001-desktop.md"] = Adr });

        Assert.Equal(3, catalog.ElementsOf(".arc42/adr/0001-desktop.md").Count);
        Assert.Equal(3, catalog.ElementsOf(".devbook/arc42/adr/0001-desktop.md#status").Count);
    }

    [Fact]
    public async Task An_identifier_is_matched_to_the_element_whatever_its_case()
    {
        var catalog = await LoadAsync(new()
        {
            ["arc42/adr/0001-desktop.md"] = Adr.Replace("c4: [backlog.desktop]", "c4: [Backlog.Desktop]", StringComparison.Ordinal),
        });

        // Stored under the workspace's own spelling, so a lookup by the element's id finds it.
        Assert.Single(catalog.RecordsOf("backlog.desktop", C4LinkKind.About));
        Assert.Single(catalog.RecordsOf("BACKLOG.DESKTOP", C4LinkKind.About));
    }

    [Fact]
    public async Task A_debt_record_carries_its_severity_and_its_place_on_the_ladder()
    {
        var catalog = await LoadAsync(new()
        {
            ["arc42/tdr/0001-renders.md"] = Tdr,
            ["arc42/tdr/0002-resting.md"] = Tdr.Replace("status: planned", string.Empty, StringComparison.Ordinal).Replace("TDR 0001", "TDR 0002", StringComparison.Ordinal),
        });

        var records = catalog.RecordsOf("backlog.desktop.devbook").Select(link => link.Record).OrderBy(record => record.Title, StringComparer.Ordinal).ToList();

        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(C4RecordKind.Debt, record.Kind));
        Assert.All(records, record => Assert.Equal(DevbookDebtSeverity.High, record.Severity));
        Assert.Equal(DevbookDebtLifecycle.Planned, records[0].Lifecycle);
        Assert.Equal(DevbookDebtLifecycle.Identified, records[1].Lifecycle);
    }

    [Fact]
    public async Task A_component_has_the_code_folders_its_building_block_names()
    {
        var catalog = await LoadAsync(new() { ["arc42/building-blocks/devbook.md"] = BuildingBlock });

        Assert.Equal(
            ["src/Modules/Devbook", "src/Infrastructure/Backlog.Infrastructure.Devbook"],
            catalog.CodeOf("backlog.desktop.devbook"));
        Assert.Empty(catalog.CodeOf("backlog.desktop"));
        Assert.Equal(C4RecordKind.BuildingBlock, Assert.Single(catalog.RecordsOf("backlog.desktop.devbook", C4LinkKind.About)).Record.Kind);
    }

    [Fact]
    public async Task Code_named_outside_a_building_block_is_not_read_as_its_elements_code()
    {
        var catalog = await LoadAsync(new() { ["arc42/05-building-block-view.md"] = BuildingBlock });

        Assert.Empty(catalog.CodeOf("backlog.desktop.devbook"));
    }

    [Fact]
    public async Task A_domain_section_links_to_the_model_too()
    {
        var catalog = await LoadAsync(new() { ["domain/devbook/context.md"] = DomainChapter });

        var link = Assert.Single(catalog.RecordsOf("backlog.desktop.devbook", C4LinkKind.Touches));
        Assert.Equal(C4RecordKind.Domain, link.Record.Kind);
        Assert.Equal("Devbook context", link.Record.Title);
        Assert.Equal("Reading", link.Section);
    }

    [Fact]
    public async Task A_link_naming_no_element_is_a_workspace_problem()
    {
        var catalog = await LoadAsync(new()
        {
            ["arc42/adr/0001-desktop.md"] = Adr.Replace("github]", "gitlab]", StringComparison.Ordinal),
        });

        var problem = Assert.Single(catalog.Problems);
        Assert.Equal(C4DevbookStore.WorkspaceFile, problem.WorkspaceFile);
        Assert.Contains("gitlab", problem.Problem.Message, StringComparison.Ordinal);
        Assert.Contains("c4-touches", problem.Problem.Message, StringComparison.Ordinal);
        Assert.EndsWith("arc42/adr/0001-desktop.md", problem.Problem.Construct, StringComparison.Ordinal);

        // The links that do resolve are kept.
        Assert.Equal(2, catalog.Links.Count);
        Assert.Empty(catalog.RecordsOf("gitlab"));
    }

    [Fact]
    public async Task A_field_quoted_inside_an_annotation_fence_is_not_a_link()
    {
        const string annotated = Adr + """

            ````annotation
            ```meta
            c4: [nowhere]
            ```
            ````
            """;

        var catalog = await LoadAsync(new() { ["arc42/adr/0001-desktop.md"] = annotated });

        Assert.Empty(catalog.Problems);
        Assert.Equal(3, catalog.Links.Count);
    }

    [Fact]
    public async Task With_no_workspace_there_is_nothing_to_link_to_and_nothing_is_reported()
    {
        var catalog = await LoadAsync(new() { ["arc42/adr/0001-desktop.md"] = Adr }, writeWorkspace: false);

        Assert.Empty(catalog.Links);
        Assert.Empty(catalog.Problems);
    }

    [Fact]
    public async Task A_chapter_save_rereads_the_links_over_the_same_parsed_workspace()
    {
        var (store, alias, devbook) = CreateStore(new() { ["arc42/adr/0001-desktop.md"] = Adr });
        using (store)
        {
            var before = await store.LoadAsync(alias);
            Assert.Single(before.RecordsOf("github"));

            File.WriteAllText(
                Path.Combine(devbook, "arc42", "adr", "0001-desktop.md"),
                Adr.Replace(", github]", "]", StringComparison.Ordinal));
            store.ChapterSaved();

            var after = await store.LoadAsync(alias);

            Assert.Empty(after.RecordsOf("github"));
            Assert.Equal(2, after.Links.Count);

            // The model was not parsed again, so an explorer comparing it by reference
            // keeps its layout across the save.
            Assert.Same(Assert.Single(before.Workspaces).Workspace, Assert.Single(after.Workspaces).Workspace);
        }
    }

    [Fact]
    public async Task This_repositorys_own_records_all_resolve_against_its_workspace()
    {
        var catalog = await LoadRepositoryAsync();

        Assert.Empty(catalog.Problems);
        Assert.NotEmpty(catalog.RecordsOf("backlog.desktop.devbook"));
        Assert.Contains(catalog.Links, link => link.Record.Kind == C4RecordKind.Decision && link.Kind == C4LinkKind.Touches);
        Assert.Contains(catalog.Links, link => link.Record is { Kind: C4RecordKind.Debt, Severity: not null, Lifecycle: not null });
    }

    // ---- harness ----------------------------------------------------------------

    /// <summary>A clone whose <c>.devbook/</c> holds the files given, keyed by their
    /// path under it, with the workspace beside the arc42 chapters.</summary>
    private async Task<C4Catalog> LoadAsync(Dictionary<string, string> files, bool writeWorkspace = true)
    {
        var (store, alias, _) = CreateStore(files, writeWorkspace);
        using (store)
        {
            return await store.LoadAsync(alias);
        }
    }

    private (C4DevbookStore Store, string Alias, string Devbook) CreateStore(Dictionary<string, string> files, bool writeWorkspace = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-c4-links-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var devbook = Path.Combine(root, ".devbook");
        Directory.CreateDirectory(Path.Combine(devbook, "arc42"));
        Directory.CreateDirectory(Path.Combine(devbook, "domain"));
        if (writeWorkspace) File.WriteAllText(Path.Combine(devbook, "arc42", C4DevbookStore.WorkspaceFile), Workspace);

        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(devbook, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        var (store, alias) = CreateStoreOver(root);
        return (store, alias, devbook);
    }

    private async Task<C4Catalog> LoadRepositoryAsync()
    {
        var (store, alias) = CreateStoreOver(RepositoryRoot());
        using (store)
        {
            return await store.LoadAsync(alias);
        }
    }

    private (C4DevbookStore Store, string Alias) CreateStoreOver(string clone)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "backlog-c4-links-settings-" + Guid.NewGuid().ToString("N"));
        _roots.Add(scratch);

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(scratch, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.C4Diagrams, true));

        var gitHub = new GitHubSettingsStore(Path.Combine(scratch, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        var repository = Assert.Single(repositories) with { CloneDirectory = clone };
        Assert.Null(gitHub.SetRepositories([repository]));

        var folders = new DevbookFolderSource(gitHub, new WorkspaceSettingsStore(Path.Combine(scratch, "store")));
        return (new C4DevbookStore(features, folders), repository.Alias);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Backlog.sln"))) return directory.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder that will not delete is not a test failure.
            }
        }
    }
}
