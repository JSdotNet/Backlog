using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The sync selector, from the panes' side, now that a direction is Backlog's and
/// not the chapter's (devbook's branch-true decision): a pick lands in the
/// repository's direction map by the block's address and no file changes, so it
/// is offered on a read-only branch snapshot too.
/// </summary>
public sealed class DevbookSyncWriteTests : IDisposable
{
    private const string Alias = "backlog";
    private const string Pad = ".file-view__header [role='group'][aria-label='Sync direction']";

    private readonly List<string> _roots = [];

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
