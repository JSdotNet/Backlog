using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Devbook pane composed the way the application hosts compose it, with a real
/// <see cref="BrainFolderSource"/> over a Brain folder this harness builds on disk,
/// beside a real repository devbook — so a test can put the pane on the Brain, on the
/// repository, or on either side of the switch, and read what it drew.
/// <para>
/// Every repository-side service reads its folders through
/// <see cref="RepositoryFolders"/>, which counts what it is asked. On the Brain the
/// pane must ask it nothing: no branch picker, no version check, no scenario load.
/// </para>
/// </summary>
internal sealed class DevbookPaneBrainHarness : IAsyncDisposable
{
    private DevbookPaneBrainHarness(
        string root,
        BunitContext context,
        TestBrainSettings settings,
        BrainFolderSource brain,
        RecordingRepositoryFolders repositoryFolders,
        string repositoryAlias)
    {
        Root = root;
        Context = context;
        Settings = settings;
        Brain = brain;
        RepositoryFolders = repositoryFolders;
        RepositoryAlias = repositoryAlias;
    }

    public string Root { get; }

    public BunitContext Context { get; }

    public TestBrainSettings Settings { get; }

    public BrainFolderSource Brain { get; }

    public RecordingRepositoryFolders RepositoryFolders { get; }

    public string RepositoryAlias { get; }

    /// <summary>The folder the Brain setting points at by default, created empty.</summary>
    public string BrainFolder => Path.Combine(Root, "Brain");

    /// <summary>
    /// A pane host. <paramref name="brainFolder"/> is the Brain setting: the
    /// harness's own folder by default, null for none set, or any path a test
    /// wants — one that does not exist, or one that is not a path at all.
    /// </summary>
    public static DevbookPaneBrainHarness Create(string? brainFolder = DefaultFolder)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-brain-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        WriteFile(clone, ".github/instructions/already-here.instructions.md", "---\napplyTo: \"**\"\n---\n\n# Already here\n");
        WriteFile(clone, ".arc42/01-introduction-and-goals.md", "# Introduction\n");

        var brainRoot = Path.Combine(root, "Brain");
        Directory.CreateDirectory(brainRoot);

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        // Search on as well, so its absence on the Brain is the pane's choice and
        // not the flag's.
        _ = featureSettings.SetEnabled(DevbookFeatures.Search, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));

        var settings = new TestBrainSettings(ReferenceEquals(brainFolder, DefaultFolder) ? brainRoot : brainFolder);
        var brain = new BrainFolderSource(() => settings.BrainFolder);
        var repositoryFolders = new RecordingRepositoryFolders(new DevbookFolderSource(gitHubSettings, store));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(repositoryFolders);
        context.Services.AddSingleton<IBrainFolderSource>(brain);
        context.Services.AddSingleton<IBrainSettings>(settings);
        context.Services.AddSingleton<ILocalGitRepositoryService>(new ScriptedGitRepositoryService());
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton(new DevbookScenarioStore(repositoryFolders));
        context.Services.AddSingleton<Arc42DevbookStore>();

        // The repository's two whole-devbook readings, so a test can open them on the
        // repository side and watch what a flip to the Brain does with them.
        context.Services.AddSingleton<DevbookAtlasService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        context.Services.AddSingleton<IDevbookSearch>(sp => new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));

        return new DevbookPaneBrainHarness(root, context, settings, brain, repositoryFolders, repository.Alias);
    }

    /// <summary>Stands for "the harness's own Brain folder" in <see cref="Create"/>,
    /// so that null can mean no folder set.</summary>
    private const string DefaultFolder = "<harness>";

    /// <summary>Writes a file into the Brain folder.</summary>
    public string WriteNote(string relativePath, string content) => WriteFile(BrainFolder, relativePath, content);

    /// <summary>The pane with nothing pressed, or the Brain as the anchor: both reach
    /// it as the empty alias.</summary>
    public IRenderedComponent<DevbookPane> RenderBrain(string? switchRepositoryAlias = null, Action<bool>? onShowsBrain = null) =>
        Render(string.Empty, switchRepositoryAlias, onShowsBrain);

    public IRenderedComponent<DevbookPane> Render(string? repositoryAlias, string? switchRepositoryAlias = null, Action<bool>? onShowsBrain = null) =>
        Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, repositoryAlias)
            .Add(pane => pane.SwitchRepositoryAlias, switchRepositoryAlias)
            .Add(pane => pane.ShowsBrainChanged, (bool showsBrain) => onShowsBrain?.Invoke(showsBrain)));

    /// <summary>The section tabs, by their labels, in strip order.</summary>
    public static IReadOnlyList<string> Tabs(IRenderedComponent<DevbookPane> component) =>
        [.. component.FindAll(".devbook-stack__nav-item").Select(tab => tab.TextContent.Trim())];

    private static string WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();

        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>The Brain setting, held in memory and announced the way the real store
/// announces it.</summary>
internal sealed class TestBrainSettings(string? folder) : IBrainSettings
{
    public string? BrainFolder { get; private set; } = folder;

    public string? TrySetBrainFolder(string? path)
    {
        BrainFolder = string.IsNullOrWhiteSpace(path) ? null : path;
        Changed?.Invoke();
        return null;
    }

    public event Action? Changed;
}

/// <summary>The repository devbook source, counting every question it is asked.</summary>
internal sealed class RecordingRepositoryFolders(IDevbookFolderSource inner) : IDevbookFolderSource
{
    private int _calls;

    /// <summary>How many times anything asked for a folder.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public void Reset() => Interlocked.Exchange(ref _calls, 0);

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias)
    {
        Interlocked.Increment(ref _calls);
        return inner.Folders(repositoryAlias);
    }

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        Interlocked.Increment(ref _calls);
        return inner.Resolve(key, repositoryAlias);
    }

    public void NotifyContentChanged() => inner.NotifyContentChanged();
}
