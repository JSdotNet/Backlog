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
/// interface, refreshed correctly. The instructions panel and the pane holding
/// all five swallowed it too, and were the remainder of the same defect: half a
/// pane that reacts and half that does not is the more visible version of it.
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
    public void Knowledge_menu_publishes_the_folder_source_change()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var menu = new KnowledgeMenu(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        menu.Changed += Handler;
        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Knowledge_menu_stops_publishing_once_unsubscribed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var menu = new KnowledgeMenu(workspace.Folders);
        var raised = 0;
        void Handler() => raised++;

        menu.Changed += Handler;
        menu.Changed -= Handler;
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

    [Fact]
    public void Instructions_knowledge_panel_reloads_when_the_knowledge_folder_moves()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Contains("Instructions: Alpha", component.Markup, StringComparison.Ordinal);

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Instructions: Beta", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Instructions: Alpha", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Instructions_knowledge_panel_rereads_when_the_folder_content_is_announced_replaced()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Contains("Instructions: Alpha", component.Markup, StringComparison.Ordinal);

        workspace.RewriteContextMap(workspace.RepositoryPath, "Pulled");
        workspace.Folders.NotifyContentChanged();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Instructions: Pulled", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Instructions: Alpha", component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A refresh, not a reset. The panel re-reads the file behind an open editing
    /// surface, and the surface keeps a buffer nobody saved yet — the same
    /// contract <see cref="Arc42KnowledgePanel"/> states for its own handler,
    /// because a background event does not get to throw away what someone is
    /// still typing.
    /// </summary>
    [Fact]
    public void An_open_instructions_editor_keeps_its_buffer_across_a_folder_content_change()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias)
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
        component.Find("[data-testid='instructions-document-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("textarea")));
        component.Find("textarea").Input("# Instructions: Typed\n\nNot saved yet.\n");

        workspace.RewriteContextMap(workspace.RepositoryPath, "Pulled");
        workspace.Folders.NotifyContentChanged();

        component.WaitForAssertion(() => Assert.Contains("Instructions: Typed", component.Find("textarea").TextContent, StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of the same contract: a folder that <em>moves</em> rather
    /// than one replaced where it stands. The distinction matters because the two
    /// reach <see cref="KnowledgeChapterEditor"/> differently — a move changes the
    /// chapter's <c>FullPath</c>, which is the identity the editor decides on — so
    /// the buffer surviving one does not imply it survives the other. It does, and
    /// this is what says so: the panel re-reads on both, and the editor declines
    /// the re-read while a save is pending either way.
    /// </summary>
    [Fact]
    public void An_open_instructions_editor_keeps_its_buffer_across_a_folder_move()
    {
        using var workspace = KnowledgeWorkspace.Create();
        using var context = workspace.CreateBunitContext();

        var component = context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias)
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='instructions-document-edit']")));
        component.Find("[data-testid='instructions-document-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("textarea")));
        component.Find("textarea").Input("# Instructions: Typed\n\nNot saved yet.\n");

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Instructions: Typed", component.Find("textarea").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Instructions: Beta", component.Find("textarea").TextContent, StringComparison.Ordinal);
        });

    }

    [Fact]
    public void Knowledge_pane_menu_reloads_when_the_knowledge_folder_moves()
    {
        using var workspace = KnowledgeWorkspace.Create();
        workspace.SetFolderEnabled("instructions", false);
        workspace.SetFolderEnabled(".arc42", false);
        workspace.SetFolderEnabled(".tech", false);
        using var context = workspace.CreateBunitContext();

        var component = context.Render<KnowledgePane>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        component.WaitForAssertion(() => Assert.Contains("Alpha Notes", MenuMarkup(component), StringComparison.Ordinal));

        workspace.PointRepositoryAt(workspace.SecondRepositoryPath);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Beta Notes", MenuMarkup(component), StringComparison.Ordinal);
            Assert.DoesNotContain("Alpha Notes", MenuMarkup(component), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The other half of the pane the menu reload does not cover: which sections
    /// exist at all. Nothing moves on disk here — a folder is turned off in
    /// Settings — so the alias and the parameters are the same as they were, which
    /// is exactly the case the pane's load guard is written to skip.
    /// </summary>
    [Fact]
    public void Knowledge_pane_sections_follow_a_knowledge_folder_being_turned_off()
    {
        using var workspace = KnowledgeWorkspace.Create();
        workspace.SetFolderEnabled("instructions", false);
        workspace.SetFolderEnabled(".arc42", false);
        workspace.SetFolderEnabled(".tech", false);
        using var context = workspace.CreateBunitContext();

        var component = context.Render<KnowledgePane>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("#tab-design")));
        Assert.Single(component.FindAll("#tab-domain"));

        workspace.SetFolderEnabled(".design", false);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("#tab-design"));
            Assert.Single(component.FindAll("#tab-domain"));
        });
    }

    /// <summary>
    /// One handler and no more. The pane has two collaborators standing between it
    /// and the folder source, and subscribing to a forwarder on each of them would
    /// reload the menu twice for one folder change. Counted with the sections
    /// turned off, because a pane with sections in it holds the subscriptions of
    /// the panels it renders as well as its own.
    /// </summary>
    [Fact]
    public void Knowledge_pane_attaches_one_handler_to_the_folder_source()
    {
        using var workspace = KnowledgeWorkspace.Create();
        Assert.Null(workspace.Features.SetEnabled(KnowledgeFeatures.KnowledgeSections, false));
        var source = new CountingKnowledgeFolderSource(workspace.Folders);
        using var context = workspace.CreateBunitContext(source);

        context.Render<KnowledgePane>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Equal(1, source.SubscriberCount);
    }

    /// <summary>Awaited disposal, because the pane renders the editing surface and
    /// that writes its last pending save on the way out — the same reason the
    /// other pane suites dispose their harness asynchronously.</summary>
    [Fact]
    public async Task Knowledge_pane_detaches_from_the_folder_source_when_disposed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        workspace.SetFolderEnabled("instructions", false);
        workspace.SetFolderEnabled(".arc42", false);
        workspace.SetFolderEnabled(".tech", false);
        var source = new CountingKnowledgeFolderSource(workspace.Folders);
        var context = workspace.CreateBunitContext(source);

        context.Render<KnowledgePane>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.True(source.SubscriberCount > 0);

        await context.DisposeAsync();

        Assert.Equal(0, source.SubscriberCount);
    }

    private static string MenuMarkup(IRenderedComponent<KnowledgePane> component) =>
        component.Find(".knowledge-stack__menu").InnerHtml;

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

    /// <summary>
    /// Torn down through <c>DisposeAsync</c>, which is not a style choice. The
    /// instructions panel's subtree contains <see cref="KnowledgeChapterEditor"/>,
    /// an <see cref="IAsyncDisposable"/>, and bUnit's synchronous <c>Dispose</c>
    /// does not reliably finish that path — the assertion below then reads a count
    /// taken before the components let go, and fails intermittently rather than
    /// honestly. The Blazor hosts tear the renderer down asynchronously too, so
    /// this is also the closer of the two to what production does.
    /// </summary>
    [Fact]
    public async Task Knowledge_panels_detach_from_the_folder_source_when_disposed()
    {
        using var workspace = KnowledgeWorkspace.Create();
        var source = new CountingKnowledgeFolderSource(workspace.Folders);
        var context = workspace.CreateBunitContext(source);

        context.Render<DomainKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));
        context.Render<DesignKnowledgeView>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));
        context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, KnowledgeWorkspace.Alias));

        Assert.Equal(3, source.SubscriberCount);

        await context.DisposeAsync();

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

        Features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        Assert.Null(Features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true));
        Assert.Null(Features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true));
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

    /// <summary>Turns one knowledge folder on or off for the repository, which is
    /// the Settings gesture that changes which sections the pane has without
    /// moving anything on disk.</summary>
    public void SetFolderEnabled(string key, bool enabled) =>
        Assert.Null(RepositorySettings.SetKnowledgeFolder(Alias, key, enabled, null));

    /// <summary>The feature settings the pane's scope is gated on, so a test can
    /// take the knowledge sections away and leave the pane with no panels of its
    /// own inside it.</summary>
    public AppFeatureSettingsStore Features { get; }

    public BunitContext CreateBunitContext(IKnowledgeFolderSource? folders = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(WorkspaceSettings);
        context.Services.AddSingleton(RepositorySettings);
        context.Services.AddSingleton(folders ?? Folders);
        context.Services.AddSingleton<IAppFeatureSettings>(Features);
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
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

        // A chapter named after the folder it is in, so the knowledge menu built
        // from one repository is told apart from the menu built from the other by
        // the node labels alone — the context map is in both and would look the
        // same in a tree either way.
        File.WriteAllText(Path.Combine(domain, $"{name.ToLowerInvariant()}-notes.md"), $"""
# {name} Notes

```meta
status: draft
```

> The {name} notes.
""");

        // The instruction roots discovery walks. Nothing else in the fixture
        // writes them, and the Instructions panel reads no other folder.
        var instructions = Path.Combine(path, ".github");
        Directory.CreateDirectory(instructions);
        File.WriteAllText(Path.Combine(instructions, "copilot-instructions.md"), $"""
# Instructions: {name}

Repository-wide guidance for {name}.
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
