using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// <see cref="IKnowledgeFolderSource"/> publishes <c>Changed</c> so that open
/// panels can reload when a folder, a repository or the storage root moves.
/// Two of the four knowledge stores used to swallow that signal, so the domain
/// and design panels kept rendering documents read from the folder configured
/// before the change — the arc42 and technology panels, behind the same
/// interface, refreshed correctly.
/// <para>
/// The chain breaks in two places per panel and both have to hold, so each is
/// asserted separately: the store must re-publish the event, and the panel must
/// subscribe and re-read. The panel tests repoint the repository's clone
/// directory rather than editing files in place, because a moved folder is the
/// case the cached read got wrong: it changes neither the component parameters
/// nor the repository alias, so the load guard short-circuits and only the
/// event can invalidate it.
/// </para>
/// </summary>
public sealed class KnowledgeFolderChangeRefreshTests
{
    [Fact]
    public void Domain_knowledge_store_publishes_the_folder_source_change()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var store = new DomainKnowledgeStore(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        store.Changed += Handler;
        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Domain_knowledge_store_stops_publishing_once_unsubscribed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var store = new DomainKnowledgeStore(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        store.Changed += Handler;
        store.Changed -= Handler;
        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Design_knowledge_provider_publishes_the_folder_source_change()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var provider = new DesignKnowledgeProvider(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        provider.Changed += Handler;
        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Design_knowledge_provider_stops_publishing_once_unsubscribed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var provider = new DesignKnowledgeProvider(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        provider.Changed += Handler;
        provider.Changed -= Handler;
        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Domain_knowledge_panel_reloads_when_the_knowledge_folder_moves()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<DomainKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Contains("Context Map: Alpha", component.Markup, StringComparison.Ordinal);

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Context Map: Beta", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Context Map: Alpha", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Design_knowledge_view_reloads_when_the_knowledge_folder_moves()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<DesignKnowledgeView>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Contains("Design: Alpha", component.Markup, StringComparison.Ordinal);

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Design: Beta", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Design: Alpha", component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The folder source outlives any one panel — it is registered as a
    /// singleton — so a panel that subscribes without unsubscribing leaves a
    /// disposed component attached to it, and every later folder change reaches
    /// a disposed renderer. That failure surfaces as an unobserved exception
    /// from the <c>async void</c> handler rather than as a failing render, so
    /// the subscription is counted directly instead of being inferred from a
    /// raised event.
    /// </summary>
    /// <summary>
    /// The pull case, which is the folder staying exactly where it is while
    /// everything in it is replaced. Nothing about the component's parameters or
    /// the repository alias changes, so the panel's own load guard short-circuits
    /// and the announcement is the only thing that can invalidate it — the same
    /// event a move raises, which is why it sits on the same port.
    /// </summary>
    [Fact]
    public void Domain_knowledge_panel_rereads_when_the_folder_content_is_announced_replaced()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<DomainKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Contains("Context Map: Alpha", component.Markup, StringComparison.Ordinal);

        workspace.RewriteContextMap(workspace.RepositoryPath, "Pulled");
        workspace.Folders.NotifyContentChanged();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Context Map: Pulled", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Context Map: Alpha", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Knowledge_panels_detach_from_the_folder_source_when_disposed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var source = new CountingKnowledgeFolderSource(workspace.Folders);
        var context = workspace.CreateBunitContext(source);

        context.Render<DomainKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));
        context.Render<DesignKnowledgeView>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Equal(2, source.SubscriberCount);

        context.Dispose();

        Assert.Equal(0, source.SubscriberCount);
    }

    /// <summary>
    /// A panel handed its view by the parent does not own the load, so the
    /// folder-change signal must not make it re-read the store behind the
    /// parent's back.
    /// </summary>
    [Fact]
    public void Domain_knowledge_panel_keeps_a_parent_supplied_view_across_a_folder_move()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();
        var supplied = DomainKnowledgeView.Unavailable("Supplied by the parent.");

        var component = context.Render<DomainKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias)
            .Add(parameter => parameter.View, supplied));

        Assert.Contains("Supplied by the parent.", component.Markup, StringComparison.Ordinal);

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Contains("Supplied by the parent.", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Context Map:", component.Markup, StringComparison.Ordinal);
    }
}

/// <summary>
/// Two repository folders with distinguishable knowledge content and the
/// settings that decide which of them the source resolves against, so a test
/// can move the folder the way a user would: by changing the setting.
/// </summary>
file sealed class KnowledgeWorkspace : IDisposable
{
    public const string Alias = "backlog";

    private readonly string _root;

    private KnowledgeWorkspace(string root)
    {
        _root = root;
        RepositoryPath = WriteRepository(Path.Combine(root, "alpha"), "Alpha");
        SecondRepositoryPath = WriteRepository(Path.Combine(root, "beta"), "Beta");

        RepositorySettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = RepositoryPath,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(RepositorySettings.SetRepositories([repository]));

        WorkspaceSettings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        Folders = new KnowledgeFolderSource(RepositorySettings, WorkspaceSettings);
    }

    public string RepositoryPath { get; }

    public string SecondRepositoryPath { get; }

    public GitHubSettingsStore RepositorySettings { get; }

    public WorkspaceSettingsStore WorkspaceSettings { get; }

    public KnowledgeFolderSource Folders { get; }

    public static KnowledgeWorkspace Create() =>
        new(Path.Combine(Path.GetTempPath(), "backlog-knowledge-refresh-tests", Guid.NewGuid().ToString("n")));

    /// <summary>Moves the configured knowledge folders, which is what a user
    /// changing the clone directory in Settings does.</summary>
    public void PointRepositoryAt(string cloneDirectory) =>
        Assert.Null(RepositorySettings.SetCloneDirectory(Alias, cloneDirectory));

    /// <summary>Replaces a chapter where it stands, which is what pulling the
    /// latest version into the clone does to it.</summary>
    public void RewriteContextMap(string repositoryPath, string name) =>
        WriteRepository(repositoryPath, name);

    public BunitContext CreateBunitContext(IKnowledgeFolderSource? folders = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(WorkspaceSettings);
        context.Services.AddSingleton(RepositorySettings);
        context.Services.AddSingleton(folders ?? Folders);
        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(_root, "features", "features.json")));
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        return context;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string WriteRepository(string path, string name)
    {
        var domain = Path.Combine(path, ".domain");
        Directory.CreateDirectory(domain);
        File.WriteAllText(Path.Combine(domain, "context-map.md"), $"""
# Context Map: {name}

```meta
status: draft
```

> The {name} context map.
""");

        var design = Path.Combine(path, ".design");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "README.md"), $"""
# Design: {name}

```meta
status: approved
```

> The {name} design knowledge.
""");

        return path;
    }
}

/// <summary>
/// A folder source that answers every question by delegating, and additionally
/// counts how many handlers are attached to <c>Changed</c> — the one thing a
/// leaked subscription can be observed by from outside the panel.
/// </summary>
file sealed class CountingKnowledgeFolderSource(IKnowledgeFolderSource inner) : IKnowledgeFolderSource
{
    public event Action? Changed
    {
        add
        {
            inner.Changed += value;
            SubscriberCount++;
        }
        remove
        {
            inner.Changed -= value;
            SubscriberCount--;
        }
    }

    public int SubscriberCount { get; private set; }

    public string StorageDirectory => inner.StorageDirectory;

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null) => inner.Resolve(key, repositoryAlias);

    public void NotifyContentChanged() => inner.NotifyContentChanged();
}
