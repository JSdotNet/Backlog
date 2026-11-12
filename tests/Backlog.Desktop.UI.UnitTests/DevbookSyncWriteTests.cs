using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The sync selector, from the panes' side, now that a direction is Backlog's and
/// not the chapter's (devbook's branch-true decision): a pick lands in the
/// repository's direction map by the block's address and no file changes, so it
/// is offered on a read-only branch snapshot too — and refused on an archived
/// repository (local ADR 0029 §4), which shows the direction and offers nothing.
/// Another writer — a tool call over MCP — moving the map redraws the pane.
/// </summary>
public sealed class DevbookSyncWriteTests : IDisposable
{
    private const string Alias = "backlog";
    private const string ContextMap = "domain/context-map.md";
    private const string Pad = ".file-view__header [role='group'][aria-label='Sync direction']";

    private readonly List<string> _roots = [];

    [Theory]
    [InlineData(DevbookSourceKind.LocalFolder)]
    [InlineData(DevbookSourceKind.Branch)]
    public async Task A_pick_in_the_domain_panel_states_the_direction_in_the_map_and_writes_no_file(DevbookSourceKind source)
    {
        using var harness = DomainHarness(source, out var directions, out var file);
        var before = File.ReadAllBytes(file);

        var component = Render(harness);
        component.WaitForAssertion(() => Assert.Single(component.FindAll(Pad)));

        // The pad's left key states pull.
        await component.ClickAsync($"{Pad} .sync-pad__key--left");

        component.WaitForAssertion(() =>
        {
            var statement = Assert.Single(directions.List(Alias));
            Assert.Equal(new DevbookSyncStatement(ContextMap, "pull", DevbookLocalAuthor.Name, statement.At), statement);
        });
        component.WaitForAssertion(() => Assert.Contains("badge--sync-pull", component.Find($"{Pad} .sync-pad__value").ClassName, StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Inheriting_again_clears_the_entry()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder, out var directions, out _);
        directions.Set(Alias, ContextMap, "push", "someone");

        var component = Render(harness);
        component.WaitForAssertion(() => Assert.Contains("badge--sync-push", component.Find($"{Pad} .sync-pad__value").ClassName, StringComparison.Ordinal));

        // The pad's down key states nothing, so the block inherits.
        await component.ClickAsync($"{Pad} .sync-pad__key--down");

        component.WaitForAssertion(() => Assert.Empty(directions.List(Alias)));
    }

    [Fact]
    public void An_archived_repository_shows_the_direction_and_offers_no_pick()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder, out var directions, out _, archived: true);
        directions.Set(Alias, ContextMap, "push", "someone");

        var component = Render(harness);

        component.WaitForAssertion(() => Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".file-view__header [data-testid='devbook-sync']")));
        Assert.Empty(component.FindAll(Pad));
        Assert.Contains("push", component.Find(".file-view__header [data-testid='devbook-sync']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_writer_moving_the_map_redraws_the_open_chapter()
    {
        using var harness = DomainHarness(DevbookSourceKind.LocalFolder, out var directions, out _);

        var component = Render(harness);
        component.WaitForAssertion(() => Assert.Contains("badge--sync-report", component.Find($"{Pad} .sync-pad__value").ClassName, StringComparison.Ordinal));

        // What a tool call over MCP, or another pane, does: write the store directly.
        directions.Set(Alias, ".devbook/domain/context-map.md", "off", "someone");

        component.WaitForAssertion(() => Assert.Contains("badge--sync-off", component.Find($"{Pad} .sync-pad__value").ClassName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pick_in_the_design_view_states_the_direction_in_the_map_on_a_branch_and_writes_no_file()
    {
        var root = TempDir();
        var design = Path.Combine(root, ".design");
        Directory.CreateDirectory(design);
        var file = Path.Combine(design, "component-libraries.md");
        File.WriteAllText(file, "# Component Libraries\n\n```meta\nstatus: draft\n```\n\nThe libraries.\n");
        var before = File.ReadAllBytes(file);

        var (context, _) = Context(root, DevbookSourceKind.Branch);
        var directions = new SessionDevbookSubjectStateStore();
        context.Services.AddSingleton<IDevbookSyncDirectionStore>(directions);
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();
        using var harness = new Harness(context);

        var component = context.Render<DesignDevbookView>(parameters => parameters
            .Add(view => view.RepositoryAlias, Alias)
            .Add(view => view.SelectedPath, "component-libraries.md"));
        component.WaitForAssertion(() => Assert.Single(component.FindAll(Pad)));

        // The pad's right key states push.
        await component.ClickAsync($"{Pad} .sync-pad__key--right");

        component.WaitForAssertion(() => Assert.Equal("push", directions.Get(Alias, "design/component-libraries.md")));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    // --- Fixtures --------------------------------------------------------------

    private static IRenderedComponent<DomainDevbookPanel> Render(Harness harness) =>
        harness.Context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, Alias)
            .Add(panel => panel.SelectedPath, ".domain/context-map.md"));

    /// <summary>A domain folder holding the context map, whose own block is the
    /// folder's sync level, with a session direction map the panel finds.</summary>
    private Harness DomainHarness(DevbookSourceKind source, out IDevbookSyncDirectionStore directions, out string file, bool archived = false)
    {
        var root = TempDir();
        var domain = Path.Combine(root, ".domain");
        Directory.CreateDirectory(domain);
        file = Path.Combine(domain, "context-map.md");
        File.WriteAllText(file, "# Context Map\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");

        var (context, folders) = Context(root, source);
        folders.Archived = archived;

        var store = new SessionDevbookSubjectStateStore();
        directions = store;
        context.Services.AddSingleton<IDevbookSyncDirectionStore>(store);
        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json")));
        context.Services.AddSingleton(new DomainDevbookStore(folders));
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context);
    }

    /// <summary>The services every pane here asks for, over one root resolved as
    /// the clone or as a branch snapshot.</summary>
    private static (BunitContext Context, StubFolderSource Folders) Context(string root, DevbookSourceKind source)
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

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-sync-write-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _roots.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private sealed record Harness(BunitContext Context) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    /// <summary>Resolves every area against one root in the legacy layout and
    /// stamps the source and the archive flag on it, as
    /// <see cref="DevbookPanelsReadOnlyBranchTests"/>' stub does.</summary>
    private sealed class StubFolderSource(string root, DevbookSourceKind source) : IDevbookFolderSource
    {
        public bool Archived { get; set; }

        public event Action? Changed;

        public void NotifyContentChanged() => Changed?.Invoke();

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => DevbookFolderSetting.Defaults();

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var folder = DevbookFolderSetting.Defaults()
                .First(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
            folder = folder with { ResolvedPath = folder.LegacyRelativePath };

            var full = Path.Combine(root, folder.EffectivePath);

            return Directory.Exists(full)
                ? new DevbookFolderLocation(key, true, null, "JSdotNet/Backlog", folder, full, root, "JSdotNet/Backlog", Alias, source, RepositoryArchived: Archived)
                : DevbookFolderLocation.Unavailable(key, $"No {key}.", source: source, repositoryArchived: Archived);
        }
    }
}
