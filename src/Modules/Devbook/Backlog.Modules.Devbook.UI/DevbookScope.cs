using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Answers "what knowledge is there to read for this scope?" from a repository
/// alias alone.
/// <para>
/// The shell needs that answer to decide whether the Devbook option is worth
/// offering at all, and it should not have to know that the answer comes from
/// repository devbook-folder settings. Asking Devbook a question is the
/// boundary; computing it in the shell was the leak.
/// </para>
/// </summary>
public sealed class DevbookScope(GitHubSettingsStore repositories, IDevbookFolderSource folders, IAppFeatureSettings features)
{
    /// <summary>The repository the alias names, or null when nothing is scoped.
    /// An alias that no longer matches a configured repository is the same
    /// answer as no alias at all.</summary>
    public GitHubRepositoryRef? Repository(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositories.Current.Find(repositoryAlias);

    /// <summary>The devbook folders in play: the repository's when one is
    /// scoped, none otherwise — a devbook belongs to a repository. The port
    /// answers, so this context never learns where a snapshot is kept.</summary>
    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        folders.Folders(repositoryAlias);

    /// <summary>The sections that actually have something behind them, or none
    /// at all while Devbook is turned off — or while nothing is
    /// scoped. The second case is asked here rather than left to the catalog,
    /// because the catalog normalises whatever it is handed against the
    /// defaults, and "no folders" would come back as "every folder".</summary>
    public IReadOnlyList<DevbookArea> VisibleAreas(string? repositoryAlias)
    {
        if (!features.IsEnabled(DevbookFeatures.RepositoryDevbook)) return [];

        var folders = Folders(repositoryAlias);
        return folders.Count == 0 ? [] : DevbookAreaCatalog.VisibleAreas(folders);
    }

    /// <summary>
    /// Whether the pane offers to search this devbook.
    /// <para>
    /// Asked here rather than by the pane reading the feature settings itself, for
    /// the reason this whole class exists: the pane's question is "is there
    /// searching to offer", and whether that is answered by a flag, by a folder
    /// setting, or by both is Devbook's business and not the strip's. It is
    /// also only half of what the reader ends up seeing — a repository whose index
    /// has never been generated has the surface and no index, which the surface
    /// itself says in words.
    /// </para>
    /// </summary>
    public bool SearchOffered => features.IsEnabled(DevbookFeatures.Search);

    /// <summary>
    /// Whether the pane offers the Brain (local ADR 0027 §4): whenever Devbook is
    /// on. The Brain is always in the scope, so unlike a repository it needs no
    /// folder behind it to be offered — a pane on a Brain with no folder yet is
    /// where the person is asked for one.
    /// </summary>
    public bool BrainOffered => features.IsEnabled(DevbookFeatures.RepositoryDevbook);

    /// <summary>
    /// Whether the Lobes atlas is offered
    /// (<c>.devbook/domain/devbook/context.md#lobes-atlas</c>): its own switch is
    /// on, and so is Devbook, whose Brain and devbooks it maps. The shell's header
    /// and the pane's Atlas button both ask this one question.
    /// </summary>
    public bool LobesAtlasOffered =>
        features.IsEnabled(DevbookFeatures.RepositoryDevbook) && features.IsEnabled(DevbookFeatures.LobesAtlas);

    /// <summary>Every configured repository that has a devbook to read, in the
    /// registry's order — the devbook scopes the Lobes atlas offers.</summary>
    public IReadOnlyList<string> DevbookRepositoryAliases() =>
        [.. repositories.Current.Repositories
            .Where(repository => VisibleAreas(repository.Alias).Count > 0)
            .Select(repository => repository.Alias)];

    /// <summary>
    /// Whether a repository chapter offers Edit. Asked here, like
    /// <see cref="SearchOffered"/>, so the panels ask Devbook what to offer rather
    /// than reading a flag. The Brain does not ask: a note there is always
    /// editable.
    /// </summary>
    public bool EditOffered => features.IsEnabled(DevbookFeatures.Editing);

    /// <summary>Whether a devbook file and its chapters show the status they
    /// state, and the decision state beside it.</summary>
    public bool StatusShown => features.IsEnabled(DevbookFeatures.Status);

    /// <summary>
    /// Raised when a feature switch changes, so a panel showing a chapter can
    /// redraw what <see cref="EditOffered"/> and <see cref="StatusShown"/> now
    /// answer without the pane being reopened.
    /// </summary>
    public event Action? SwitchesChanged
    {
        add => features.Changed += value;
        remove => features.Changed -= value;
    }

    /// <summary>
    /// <see cref="EditOffered"/> for a panel that resolved the scope optionally.
    /// <para>
    /// No scope means a host that composed none — the storybook, a test rendering
    /// a panel for an unrelated reason — and it gets the viewer as it always was,
    /// so only the real catalog's default changes what a person sees. One rule,
    /// stated here, rather than a <c>?? true</c> each panel has to remember.
    /// </para>
    /// </summary>
    public static bool EditOfferedBy(DevbookScope? scope) => scope?.EditOffered ?? true;

    /// <summary><see cref="StatusShown"/> for a panel that resolved the scope
    /// optionally; no scope shows the status, for the reason
    /// <see cref="EditOfferedBy"/> gives.</summary>
    public static bool StatusShownBy(DevbookScope? scope) => scope?.StatusShown ?? true;

    /// <summary>
    /// The viewer switches as one panel holds them: what they answer now, and a
    /// subscription that tells the panel when either changes.
    /// <para>
    /// <paramref name="scope"/> may be null — a host that composed none — and the
    /// answers are then <see cref="EditOfferedBy"/>'s. <paramref name="dispatch"/>
    /// is the component's <c>InvokeAsync</c>: a switch is flipped on whichever
    /// thread the settings screen runs, and <paramref name="onChanged"/> writes
    /// state the render reads. Nothing is called once the watch is disposed.
    /// </para>
    /// </summary>
    public static DevbookViewerSwitches Watch(DevbookScope? scope, Func<Func<Task>, Task> dispatch, Func<Task> onChanged) =>
        new(scope, dispatch, onChanged);
}

/// <summary>
/// One panel's hold on the viewer switches — see <see cref="DevbookScope.Watch"/>.
/// The panel states what it does when they change; subscribing, marshalling to
/// the renderer and standing down on dispose are written here once.
/// </summary>
public sealed class DevbookViewerSwitches : IDisposable
{
    private readonly DevbookScope? _scope;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly Func<Task> _onChanged;
    private bool _disposed;

    internal DevbookViewerSwitches(DevbookScope? scope, Func<Func<Task>, Task> dispatch, Func<Task> onChanged)
    {
        _scope = scope;
        _dispatch = dispatch;
        _onChanged = onChanged;
        if (_scope is not null) _scope.SwitchesChanged += OnSwitchesChanged;
    }

    /// <summary>Whether Edit is offered on a repository chapter.</summary>
    public bool EditOffered => DevbookScope.EditOfferedBy(_scope);

    /// <summary>Whether a devbook file's and its chapters' status is shown.</summary>
    public bool StatusShown => DevbookScope.StatusShownBy(_scope);

    private void OnSwitchesChanged()
    {
        if (_disposed) return;

        _ = _dispatch(async () =>
        {
            if (!_disposed) await _onChanged();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        if (_scope is not null) _scope.SwitchesChanged -= OnSwitchesChanged;
    }
}
