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

    private Harness DomainHarness(DevbookSourceKind source)
    {
        var root = WithDomain();
        var (context, folders) = Context(root, source);

        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json")));
        context.Services.AddSingleton(new DomainDevbookStore(folders));
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

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
                ? new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", folder, full, root, "JSdotNet/Backlog", "backlog", Source)
                : DevbookFolderLocation.Unavailable(key, $"No {key}.", source: Source);
        }
    }
}
