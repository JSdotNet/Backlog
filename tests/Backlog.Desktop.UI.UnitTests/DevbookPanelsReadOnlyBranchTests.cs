using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The panels that offer a chapter edit, rendered against a branch snapshot.
/// <para>
/// <see cref="DevbookReadOnlyBranchTests"/> proves the stores and the writer
/// refuse; this proves the panels never ask. The three panels here load their
/// chapter against a root their store already holds rather than against the
/// located folder, and that path used to lose the source on the way — so the
/// store said read-only, the status selector was hidden, and the edit control
/// beside it was offered anyway. A save from it landed in the fetched copy of
/// the branch, which is exactly the outcome the writer's own guard exists to
/// prevent and could not, because the ref it was handed said editable.
/// </para>
/// <para>
/// Each panel is rendered twice, once per source, so a missing button proves the
/// gate and not a chapter that failed to render at all.
/// </para>
/// </summary>
public sealed class DevbookPanelsReadOnlyBranchTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // --- Domain ----------------------------------------------------------------

    [Fact]
    public void The_domain_panel_offers_no_edit_on_a_branch()
    {
        using var harness = DomainHarness(DevbookSourceKind.Branch);

        var component = harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

        component.WaitForAssertion(() => Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-edit']"));
    }

    [Fact]
    public void The_domain_panel_offers_the_edit_on_a_clone()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder);

        var component = harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));
    }

    // --- An archived repository (local ADR 0029 §4) ----------------------------

    /// <summary>"A clone of an archived repository": the chapter reads, and
    /// neither an edit nor a remark is offered.</summary>
    [Fact]
    public void The_domain_panel_offers_no_edit_and_no_remark_on_an_archived_repositorys_clone()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder, archived: true);

        var component = harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

        component.WaitForAssertion(() => Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-edit']"));
        Assert.Empty(component.FindAll("[data-testid^='markdown-comment-']"));
    }

    /// <summary>A remark kept from before the archive reads, with no resolve,
    /// edit or delete control: each would write to the replicated store.</summary>
    [Fact]
    public void The_domain_panel_shows_a_kept_remark_read_only_on_an_archived_repository()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder, archived: true);
        var store = new SessionDevbookAnnotationStore();
        var kept = store.Add("backlog", ".domain/context-map.md", 0, "someone");
        store.Edit(kept.Id, "Kept from before the archive.");
        harness.Context.Services.AddSingleton<IDevbookAnnotationStore>(store);

        var component = harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

        component.WaitForAssertion(() => Assert.Contains("Kept from before the archive.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid^='markdown-comment-resolve-']"));
        Assert.Empty(component.FindAll("[data-testid^='markdown-comment-edit-']"));
        Assert.Empty(component.FindAll("[data-testid^='markdown-comment-delete-']"));
    }

    /// <summary>The remark is refused on the repository being archived, not on
    /// the folder being read-only: a branch snapshot of an active repository keeps
    /// the remarks it always had, because a remark is the app's, not the
    /// file's.</summary>
    [Fact]
    public void The_domain_panel_still_takes_remarks_on_an_active_repositorys_branch()
    {
        using var harness = DomainHarness(DevbookSourceKind.Branch);

        var component = harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));
    }

    // --- A debt record's state (the subject store) --------------------------------

    /// <summary>
    /// The flip the status writer's retirement makes: on a branch snapshot, where
    /// the chapter cannot be edited, a debt record's state is still offered — it
    /// is the subject store's, so no file changes and nothing is left for the next
    /// fetch to discard. The pick lands in the store, by this host, and is drawn.
    /// </summary>
    [Fact]
    public void The_arc42_panel_sets_a_debt_records_state_on_a_branch_and_writes_no_file()
    {
        using var harness = Arc42Harness(DevbookSourceKind.Branch, out var subjects, out var record);
        var before = File.ReadAllBytes(record);

        var component = harness.Context.Render<Arc42DevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, DebtPath));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-subject-state-select']")));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-edit']"));

        component.Find("[data-testid='devbook-subject-state-select'] select").Change("planned");

        // The pick lands through the panel's async handler, so the store and the
        // re-render are awaited rather than read straight after Change().
        component.WaitForAssertion(() =>
        {
            var stored = subjects.Get("backlog", DevbookSubject.Debt(DebtPath));
            Assert.NotNull(stored);
            Assert.Equal("planned", stored.State);
            Assert.Equal(DevbookLocalAuthor.Name, stored.By);
        });
        component.WaitForAssertion(() =>
        {
            Assert.StartsWith(
                "debt state set by You on ",
                component.Find("[data-testid='devbook-state-subject']").TextContent.Trim(),
                StringComparison.Ordinal);
            Assert.Equal("planned", component.Find("[data-testid='devbook-subject-state-select'] select").GetAttribute("value"));
        });
        Assert.Equal(before, File.ReadAllBytes(record));
    }

    /// <summary>An archived repository is read-only all the way through (local
    /// ADR 0029 §4): a state kept from before the archive reads, and none is
    /// offered.</summary>
    [Fact]
    public void The_arc42_panel_shows_a_debt_records_state_read_only_on_an_archived_repository()
    {
        using var harness = Arc42Harness(DevbookSourceKind.LocalFolder, out var subjects, out _, archived: true);
        var kept = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var debt = DevbookSubject.Debt(DebtPath);
        subjects.Apply(new DevbookSubjectState(DevbookSubject.IdFor("backlog", debt), "backlog", debt, "identified", "someone", kept, null, null, kept));

        var component = harness.Context.Render<Arc42DevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, DebtPath));

        component.WaitForAssertion(() => Assert.Equal(
            "identified",
            component.Find("[data-testid='devbook-subject-state']").TextContent.Trim()));
        Assert.Equal(
            "debt state set by someone on 2026-10-30",
            component.Find("[data-testid='devbook-state-subject']").TextContent.Trim());
        Assert.Empty(component.FindAll("[data-testid='devbook-subject-state-select']"));
    }

    // --- Design ----------------------------------------------------------------

    [Fact]
    public void The_design_view_offers_no_edit_on_a_branch()
    {
        using var harness = DesignHarness(DevbookSourceKind.Branch);

        var component = harness.Context.Render<DesignDevbookView>(parameters => parameters
            .Add(view => view.RepositoryAlias, "backlog")
            .Add(view => view.SelectedPath, "colors.md"));

        component.WaitForAssertion(() => Assert.Contains("Palette prose.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='design-chapter-file-edit']"));
    }

    [Fact]
    public void The_design_view_offers_the_edit_on_a_clone()
    {
        using var harness = DesignHarness(DevbookSourceKind.LocalFolder);

        var component = harness.Context.Render<DesignDevbookView>(parameters => parameters
            .Add(view => view.RepositoryAlias, "backlog")
            .Add(view => view.SelectedPath, "colors.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
    }

    // --- Instructions ----------------------------------------------------------

    [Fact]
    public void The_instructions_panel_offers_no_edit_on_a_branch()
    {
        using var harness = InstructionsHarness(DevbookSourceKind.Branch);

        var component = harness.Context.Render<InstructionsDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, "CLAUDE.md"));

        component.WaitForAssertion(() => Assert.Contains("Project instructions.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='instructions-document-edit']"));
    }

    [Fact]
    public void The_instructions_panel_offers_the_edit_on_a_clone()
    {
        using var harness = InstructionsHarness(DevbookSourceKind.LocalFolder);

        var component = harness.Context.Render<InstructionsDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, "CLAUDE.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
    }

    /// <summary>The source flipping under an open editor. The other panels gate
    /// the editing state on the chapter still being writable and Instructions
    /// did not: a clone switched to a branch left <c>_editing</c> set, and the
    /// file view draws its edit body on that alone — an editor over a chapter it
    /// may not save, with the way out gone along with the offer.</summary>
    [Fact]
    public void The_instructions_editor_closes_when_the_source_becomes_a_branch()
    {
        using var harness = InstructionsHarness(DevbookSourceKind.LocalFolder);

        var component = harness.Context.Render<InstructionsDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, "backlog")
            .Add(panel => panel.SelectedPath, "CLAUDE.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
        component.Find("[data-testid='instructions-document-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-chapter-surface']")));

        harness.Folders.Source = DevbookSourceKind.Branch;
        harness.Folders.RaiseChanged();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-chapter-surface']")));
        Assert.Empty(component.FindAll("[data-testid='instructions-document-edit']"));
        Assert.Empty(component.FindAll("[data-testid='instructions-document-done']"));
        Assert.Contains("Project instructions.", component.Markup, StringComparison.Ordinal);
    }

    // --- The loader the three panels share --------------------------------------

    /// <summary>The root-path overload is the one the panels reach for, and it is
    /// where the source was dropped: a caller that holds the flag has to be able
    /// to hand it over.</summary>
    [Fact]
    public async Task Loading_against_a_root_carries_the_edit_flag_through()
    {
        var root = WithDomain();

        var readOnly = await DevbookChapterContent.LoadAsync("domain", Path.Combine(root, ".domain"), ".domain/context-map.md", canEdit: false, TestContext.Current.CancellationToken);
        var editable = await DevbookChapterContent.LoadAsync("domain", Path.Combine(root, ".domain"), ".domain/context-map.md", canEdit: true, TestContext.Current.CancellationToken);

        Assert.NotNull(readOnly.Chapter);
        Assert.False(readOnly.Chapter.CanEdit);
        Assert.NotNull(editable.Chapter);
        Assert.True(editable.Chapter.CanEdit);
    }

    // --- Fixtures --------------------------------------------------------------

    private Harness DomainHarness(DevbookSourceKind source, bool archived = false)
    {
        var root = WithDomain();
        var (context, folders) = Context(root, source);
        folders.Archived = archived;

        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json")));
        context.Services.AddSingleton(new DomainDevbookStore(folders));
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, folders);
    }

    private const string DebtPath = ".arc42/tdr/0001-sync-lag.md";

    /// <summary>An architecture folder holding one debt record, listed in the
    /// folder's index — the reader walks only the top of the folder on its own —
    /// with a session subject store the panel finds.</summary>
    private Harness Arc42Harness(DevbookSourceKind source, out SessionDevbookSubjectStateStore subjects, out string record, bool archived = false)
    {
        var root = TempDir();
        var arc42 = Path.Combine(root, ".arc42");
        Directory.CreateDirectory(Path.Combine(arc42, "tdr"));
        Directory.CreateDirectory(Path.Combine(arc42, "_meta"));
        record = Path.Combine(arc42, "tdr", "0001-sync-lag.md");
        File.WriteAllText(record, "# TDR 0001: Sync lag\n\n```meta\nstatus: draft\n```\n\nThe deployed service lags.\n");
        File.WriteAllText(Path.Combine(arc42, "_meta", "index.json"), $$"""
            {
              "schemaVersion": 1,
              "entries": [
                { "type": "file", "path": "{{DebtPath}}" }
              ]
            }
            """);

        var (context, folders) = Context(root, source);
        folders.Archived = archived;

        subjects = new SessionDevbookSubjectStateStore();
        context.Services.AddSingleton<IDevbookSubjectStateStore>(subjects);
        context.Services.AddSingleton(new Arc42DevbookStore(folders));

        return new Harness(context, folders);
    }

    private Harness DesignHarness(DevbookSourceKind source)
    {
        var root = TempDir();
        var design = Path.Combine(root, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "colors.md"), "# Colors\n\n```meta\nstatus: draft\n```\n\nPalette prose.\n");

        var (context, folders) = Context(root, source);
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();

        return new Harness(context, folders);
    }

    private Harness InstructionsHarness(DevbookSourceKind source)
    {
        var root = TempDir();
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# Claude\n\nProject instructions.\n");

        var (context, folders) = Context(root, source);
        context.Services.AddSingleton(new InstructionSourceDiscovery());

        return new Harness(context, folders);
    }

    /// <summary>The services every one of the three panels asks for, over a
    /// repository whose clone is <paramref name="root"/> when the source is the
    /// clone and absent when it is a branch — the same pair of shapes
    /// <see cref="DevbookReadOnlyBranchTests"/> hands its stores.</summary>
    private (BunitContext Context, StubFolderSource Folders) Context(string root, DevbookSourceKind source)
    {
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var branch = source is DevbookSourceKind.Branch;
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = branch ? null : root,
            UseLocalDevbookFolder = !branch,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var folders = new StubFolderSource(root, source);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new WorkspaceSettingsStore(Path.Combine(root, "store")));
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<DevbookChapterWriter>();

        return (context, folders);
    }

    private string WithDomain()
    {
        var root = TempDir();
        var domain = Path.Combine(root, ".domain");
        Directory.CreateDirectory(domain);
        File.WriteAllText(
            Path.Combine(domain, "context-map.md"),
            "# Context Map\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n\n## Boundaries\n\nWhat divides the contexts.\n");

        return root;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-panels-read-only-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed record Harness(BunitContext Context, StubFolderSource Folders) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    /// <summary>Resolves every area against one root and stamps the source on
    /// it — the stub <see cref="DevbookReadOnlyBranchTests"/> uses, for the reason
    /// given there: the panels are under test, not the resolution. The source can
    /// be changed and the change announced, which is what a switch in the pane's
    /// selector comes down to once the settings store has republished it.</summary>
    private sealed class StubFolderSource(string root, DevbookSourceKind source) : IDevbookFolderSource
    {
        public DevbookSourceKind Source { get; set; } = source;

        /// <summary>Whether the repository is archived, stamped on every location
        /// the way the real source stamps it.</summary>
        public bool Archived { get; set; }

        public event Action? Changed;

        public void RaiseChanged() => Changed?.Invoke();

        public void NotifyContentChanged() => Changed?.Invoke();

        public string StorageDirectory => root;

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => DevbookFolderSetting.Defaults();

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var folder = DevbookFolderSetting.Defaults()
                .First(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
            // The fixtures sit in the root layout, which the real source reaches
            // through its fallback to the legacy folder.
            folder = folder with { ResolvedPath = folder.LegacyRelativePath };

            var full = Path.Combine(root, folder.EffectivePath);

            return Directory.Exists(full)
                ? new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", folder, full, root, "JSdotNet/Backlog", "backlog", Source, RepositoryArchived: Archived)
                : DevbookFolderLocation.Unavailable(key, $"No {key}.", source: Source, repositoryArchived: Archived);
        }
    }
}
