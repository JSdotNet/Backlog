using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Storage;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Remembers what the shell was showing, so it reopens there instead of always
/// defaulting to the workspace panes.
/// <para>
/// Its own file beside the app's other per-user settings, for the same reason
/// <see cref="WorkingHoursSettingsStore"/> has one: what is currently on
/// screen changes far more often than the workspace pointer <c>settings.json</c>
/// holds.
/// </para>
/// <para>
/// Holds the surface and the view names as plain strings rather than the
/// shell's own enums, which are internal to the desktop UI project and not
/// visible from here — the shell is the one that knows what the names mean
/// and is the only reader of them.
/// </para>
/// <para>
/// One file for both, not two, because a fresh shell instance restores them
/// together: which takeover was open and which main view the workspace was
/// showing under it are both "what the reader was looking at", read back in the
/// same <c>OnInitializedAsync</c>. The side panes that once sat beside the view
/// are gone (local ADR 0022, amendment of 2026-10-15).
/// </para>
/// <para>
/// The roadmap was a takeover before it was a view, so a file from then names it
/// as the surface. Reading such a file is where it migrates — see
/// <see cref="Normalize"/> — and nothing here ever writes it as a surface again.
/// </para>
/// <para>
/// The roadmap's Hours switch rides here too (local ADR 0019, §4): it is how the
/// reader last had the roadmap drawn, it belongs to this device, and sync never
/// carries it. Roadmap reads it through its own port, answered over this store. The
/// bands the reader folded to one lane are the same kind of choice and ride beside it.
/// </para>
/// <para>
/// So do the Calendar's Month | Week range, its Show tasks, plans and events boxes, and the
/// header's repository scope, and the system tools pane's tab and host filter: each is
/// how the reader last had a screen, kept on this device only. So is show archived
/// (local ADR 0029, §3), whether the archived repositories are offered where they are
/// otherwise hidden. Every key is additive and left out while at its default — the tools
/// pane's two until it has been on a tab or a filter here, the notification centre's Group by
/// and filters until chosen and its Show settled switch while off — so a file
/// written before it keeps its shape and an older build reads a newer file.
/// </para>
/// <para>
/// <b>A relabelled repository takes what is remembered of it along</b> (local ADR 0060
/// §3): the repository scope, the atlas's scope and the surface in their
/// <c>repo:</c> and <c>ghost:</c> forms, and the roadmap bands folded by alias move to
/// the new alias. The file is this device's own, so every device re-keys its copy
/// from the registry's alias moves.
/// </para>
/// </summary>
// Reads through JsonFileStore<T>; writes stay queued, which that store's synchronous save is not.
public sealed class ShellNavigationStore : IRepositoryAliasKeyedStore
{
    /// <summary>The prefixes of a surface or an atlas scope that name a repository by
    /// its alias: a registered devbook's and a ghost devbook's.</summary>
    private static readonly string[] AliasPrefixes = ["repo:", "ghost:"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] Empty = [];

    /// <summary>The main view a file with no <c>lastView</c> reopens on, by its surface
    /// id (local ADR 0043 §2): before the view switch the workspace's main pane was the
    /// task list.</summary>
    public const string DefaultView = "tasks";

    /// <summary>The surface id the roadmap's view is stored under (local ADR 0043 §2).</summary>
    private const string RoadmapViewId = "roadmap";

    /// <summary>
    /// The view names a file from before local ADR 0043 holds — the members of the
    /// retired <c>ShellView</c> enum, and <c>InProgress</c>, the Goals view's name before
    /// its rename — each with the surface id it reads as now (§2). Exact names only: the
    /// enum's members were written as they were spelled.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RetiredViewNames { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tasks"] = "tasks",
            ["Roadmap"] = RoadmapViewId,
            ["Calendar"] = "calendar",
            ["Board"] = "board",
            ["Goals"] = "goals",
            ["InProgress"] = "goals",
        };

    /// <summary>
    /// The surface names a file from before local ADR 0043 holds — the members of the
    /// retired <c>WorkspaceSurface</c> enum — each with the takeover's surface id it reads
    /// as now (§2), and <c>Workspace</c>, the main view alone, as no takeover at all.
    /// <c>Roadmap</c> is not here: it was a takeover once and reads as the Roadmap view
    /// over the workspace. Exact names only: the enum's members were written as spelled.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> RetiredSurfaceNames { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [WorkspaceSurfaceName] = null,
            ["Tools"] = "tools",
            ["Dashboard"] = "dashboard",
            ["Sessions"] = "sessions",
            ["PullRequests"] = "pull-requests",
            [InboxName] = InboxId,
            [DevbookName] = DevbookId,
            ["Notifications"] = "notifications",
            ["Atlas"] = "atlas",
        };

    /// <summary>What the Board's columns are when nothing else was remembered.</summary>
    public const string DefaultBoardColumns = "Status";

    /// <summary>How the Tasks list groups when nothing else was remembered: by plan
    /// (local ADR 0062 §1).</summary>
    public const string DefaultTasksGrouping = "Plans";

    /// <summary>The Calendar's range when nothing else was remembered.</summary>
    public const string DefaultCalendarRange = "Month";

    /// <summary>The surface name the roadmap was stored under while it was a
    /// takeover; it reopens as the Roadmap view.</summary>
    private const string RoadmapName = "Roadmap";

    /// <summary>The surface name the retired enum wrote for no takeover at all: the
    /// workspace. Now nothing is written for it.</summary>
    private const string WorkspaceSurfaceName = "Workspace";

    /// <summary>The Inbox's and the Devbook's surface ids (local ADR 0043 §2).</summary>
    private const string InboxId = "inbox";
    private const string DevbookId = "devbook";

    /// <summary>The surface name the retired enum stored the Inbox under.</summary>
    private const string InboxName = "Inbox";

    /// <summary>The surface name the retired enum stored the Devbook under.</summary>
    private const string DevbookName = "Devbook";

    /// <summary>The file's name, in the head's own app-data folder.</summary>
    public const string FileName = "shell-navigation.json";

    private readonly JsonFileStore<ShellNavigationDto> _file;

    /// <summary>Serializes every read-modify-write and its snapshot; see
    /// <see cref="Update"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The file in <see cref="WorkspaceSettingsStore.DefaultAppDataDirectory"/>:
    /// <c>Backlog.Debug</c> for a Debug build, <c>Backlog</c> for the installed app, so
    /// a head started out of a checkout never writes what the installed app restores.</summary>
    public ShellNavigationStore()
        : this(AppDataPaths.Head(FileName))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test —
    /// or a session running beside another — a store that does not fight over
    /// the real per-user file.</summary>
    public ShellNavigationStore(string path)
    {
        _file = new JsonFileStore<ShellNavigationDto>(path, JsonOptions);

        var dto = Read();
        (LastSurface, LastView) = Normalize(dto?.LastSurface, dto?.LastView);
        RoadmapHoursShown = dto?.RoadmapHoursShown ?? true;
        RoadmapCompletedShown = dto?.RoadmapCompletedShown ?? true;
        RoadmapCollapsedGroups = dto?.RoadmapCollapsedGroups ?? Empty;
        BoardColumns = string.IsNullOrWhiteSpace(dto?.BoardColumns) ? DefaultBoardColumns : dto.BoardColumns;
        TasksGrouping = string.IsNullOrWhiteSpace(dto?.TasksGrouping) ? DefaultTasksGrouping : dto.TasksGrouping;
        CalendarTasksShown = dto?.CalendarTasksShown ?? true;
        CalendarPlansShown = dto?.CalendarPlansShown ?? false;
        CalendarEventsShown = dto?.CalendarEventsShown ?? false;
        CalendarRange = string.IsNullOrWhiteSpace(dto?.CalendarRange) ? DefaultCalendarRange : dto.CalendarRange;
        RepositoryScope = dto?.RepositoryScope ?? Empty;
        ToolsTab = string.IsNullOrWhiteSpace(dto?.ToolsTab) ? null : dto.ToolsTab;
        ToolsHostFilter = string.IsNullOrWhiteSpace(dto?.ToolsHostFilter) ? null : dto.ToolsHostFilter;
        NotificationsGroupBy = Blank(dto?.NotificationsGroupBy);
        NotificationsAskedBy = Blank(dto?.NotificationsAskedBy);
        NotificationsKind = Blank(dto?.NotificationsKind);
        NotificationsFrom = Blank(dto?.NotificationsFrom);
        NotificationsShowSettled = dto?.NotificationsShowSettled ?? false;
        AtlasReturnSurface = AtlasReturnId(dto?.AtlasReturnSurface);
        AtlasScope = string.IsNullOrWhiteSpace(dto?.AtlasScope) ? null : dto.AtlasScope;
        ShowArchivedRepositories = dto?.ShowArchivedRepositories ?? false;
        C4ZoomHintDismissed = dto?.C4ZoomHintDismissed ?? false;
        BrainHiddenShown = dto?.BrainHiddenShown ?? false;
        RoadmapArchiveOffered = dto?.RoadmapArchiveOffered ?? Empty;
    }

    /// <summary>Raised after anything remembered here changes, so nothing has
    /// to poll the file to notice.</summary>
    public event Action? Changed;

    /// <summary>The surface that was last set, or null when nothing has been
    /// remembered yet — first launch included.</summary>
    public string? LastSurface { get; private set; }

    /// <summary>The main view the workspace was showing when last set — by its surface
    /// id, <c>tasks</c>, <c>board</c>, <c>calendar</c>, <c>roadmap</c> or <c>goals</c>
    /// (local ADR 0043 §2) — and <see cref="DefaultView"/> when nothing has been
    /// remembered yet. A name the retired enum stored reads as its id
    /// (<see cref="RetiredViewNames"/>); any other value is kept as written, and the
    /// shell reads one it does not offer as Tasks.</summary>
    public string LastView { get; private set; }

    /// <summary>Whether the roadmap's day and week heads carry their hours line: on until
    /// the reader turns the Hours switch off on this device.</summary>
    public bool RoadmapHoursShown { get; private set; }

    /// <summary>Whether the roadmap draws finished plans: on until the reader turns the
    /// Completed switch off on this device.</summary>
    public bool RoadmapCompletedShown { get; private set; }

    /// <summary>The roadmap bands, by group id, the reader folded to one lane on this
    /// device. Empty until one is folded.</summary>
    public IReadOnlyList<string> RoadmapCollapsedGroups { get; private set; }

    /// <summary>
    /// What the Board view's columns were last grouped by — the name of a
    /// <c>TaskBoardGrouping</c> member, kept beside the view it belongs to.
    /// <para>
    /// A name rather than the enum, for the reason <see cref="LastView"/> is one:
    /// this store sits below the module that owns the vocabulary. Left out of the
    /// file while it is the default, so a file from before the Board keeps its
    /// shape; a name the shell no longer knows is read back as Status there.
    /// </para>
    /// </summary>
    public string BoardColumns { get; private set; }

    /// <summary>
    /// How the Tasks list was last grouped — the name of a <c>TaskListGrouping</c>
    /// member, <c>Plans</c> or <c>Flat</c> (local ADR 0062 §1) — kept beside the Board's
    /// columns for <see cref="BoardColumns"/>' reason. <see cref="DefaultTasksGrouping"/>
    /// until changed, and left out of the file while it is.
    /// </summary>
    public string TasksGrouping { get; private set; }

    /// <summary>Whether the Calendar draws the tasks and their planned hours — its "Show
    /// tasks" box (local ADR 0056 §4): on until the reader turns it off on this device. A
    /// file that carries no choice — every file from before the box — reads as on.</summary>
    public bool CalendarTasksShown { get; private set; }

    /// <summary>Whether the Calendar draws the roadmap's plans — its "Show plans"
    /// box: off until the reader turns it on on this device. A file that carries no
    /// choice reads as off; one that says <c>false</c>, as a file from when the box
    /// was on by default does once it was turned off, reads as off too.</summary>
    public bool CalendarPlansShown { get; private set; }

    /// <summary>Whether the Calendar draws the knowledge events — its "Show events"
    /// box (local ADR 0050 §7): off until the reader turns it on on this device, and a
    /// choice apart from <see cref="CalendarPlansShown"/>. A file that carries no choice
    /// reads as off.</summary>
    public bool CalendarEventsShown { get; private set; }

    /// <summary>
    /// The Calendar's range — <c>Month</c> or <c>Week</c> — as the reader last
    /// left it on this device; <see cref="DefaultCalendarRange"/> until changed. A name,
    /// for the reason <see cref="BoardColumns"/> is one: the shell reads a name it does
    /// not know as Month. Only the range is kept, never the date it showed.
    /// </summary>
    public string CalendarRange { get; private set; }

    /// <summary>
    /// The header's repository scope by alias, in the order it was chosen: the first is
    /// the anchor. Empty is every repository, the scope nobody narrowed. Aliases are
    /// kept as written; the shell drops one no longer configured when it restores them.
    /// <para>
    /// The Brain is kept here as the reserved <see cref="BrainScope.Value"/>, the empty
    /// string, in its place (local ADR 0027, §5): no configured alias is ever empty, so
    /// the value cannot be a repository's. Nothing here drops or rewrites it.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> RepositoryScope { get; private set; }

    /// <summary>The system tools pane's tab when it was last open on this device, by
    /// the pane's own tab id; null until one was chosen. Read through Dev PC
    /// Management's <c>IToolsViewPreferences</c>.</summary>
    public string? ToolsTab { get; private set; }

    /// <summary>The system tools pane's host filter, by the pane's own id; null until
    /// one was chosen.</summary>
    public string? ToolsHostFilter { get; private set; }

    /// <summary>Whether the ways-to-move hint of the C4 zoom navigation was dismissed on this
    /// device. Read through Devbook's <c>IDevbookViewPreferences</c>.</summary>
    public bool C4ZoomHintDismissed { get; private set; }

    /// <summary>The notification centre's Group by when it was last chosen on this device, by
    /// the centre's own slug; null until one was chosen. Read, with the three filters below,
    /// through the Notifications UI's <c>INotificationCentrePreferences</c>.</summary>
    public string? NotificationsGroupBy { get; private set; }

    /// <summary>The centre's Asked by filter, by its slug; null for All.</summary>
    public string? NotificationsAskedBy { get; private set; }

    /// <summary>The centre's Kind filter, by its slug; null for All.</summary>
    public string? NotificationsKind { get; private set; }

    /// <summary>The centre's From filter, a system's name; null for anywhere.</summary>
    public string? NotificationsFrom { get; private set; }

    /// <summary>Whether the centre lists the notifications already settled — answered,
    /// acknowledged or closed — beside the open ones: off until the reader turns its Show
    /// settled switch on on this device, and off for a file written before the switch.</summary>
    public bool NotificationsShowSettled { get; private set; }

    /// <summary>The takeover the Lobes atlas was opened over, which its way back
    /// returns to, by its surface id; null for the workspace, and until the atlas was
    /// opened. A name the retired enum stored reads as its id, as
    /// <see cref="LastSurface"/>'s does.
    /// Kept beside <see cref="LastSurface"/> because the Router remounts the shell on
    /// every route change — Register repository's trip to Settings among them — and
    /// a reopened atlas must still know where it came from.</summary>
    public string? AtlasReturnSurface { get; private set; }

    /// <summary>The scope the Lobes atlas was opened on, or the ghost its Register
    /// repository left from — <c>brain</c>, <c>repo:&lt;alias&gt;</c> or
    /// <c>ghost:&lt;alias&gt;</c> — or null for All.</summary>
    public string? AtlasScope { get; private set; }

    /// <summary>Whether the archived repositories are shown where they are otherwise
    /// hidden — show archived (local ADR 0029, §3): off until turned on on this device.
    /// Read through the shared kernel's <c>IShowArchivedRepositories</c>.</summary>
    public bool ShowArchivedRepositories { get; private set; }

    /// <summary>Whether the Brain's hidden folders — the ignored ones and the
    /// dot-folders — show as tabs in the Devbook pane, Show hidden (local ADR 0026
    /// §2): off until turned on on this device. Read through the Devbook module's
    /// <c>IBrainShowHidden</c>.</summary>
    public bool BrainHiddenShown { get; private set; }

    /// <summary>The roadmap items, by id, this device has offered to move a finished
    /// plan's project note to the archive (local ADR 0052 §5): each is offered once,
    /// whatever the person did with the offer. Empty until the first offer.</summary>
    public IReadOnlyList<string> RoadmapArchiveOffered { get; private set; }

    /// <summary>Where the choices are written.</summary>
    public string SettingsPath => _file.Path;

    public void SetLastSurface(string? surface) => Update(() =>
    {
        var (normalizedSurface, view) = Normalize(surface, LastView);
        if (normalizedSurface == LastSurface && view == LastView) return false;

        LastSurface = normalizedSurface;
        LastView = view;
        return true;
    });

    public void SetLastView(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        view = ViewId(view);

        Update(() =>
        {
            if (view == LastView) return false;

            LastView = view;
            return true;
        });
    }

    /// <summary>
    /// The surface and the view as they are kept: the roadmap as a view, never as a
    /// surface. A file from before the view switch names <c>Roadmap</c> as its
    /// surface; it reopens on the Roadmap view over the workspace, and since the
    /// in-memory surface is then the workspace, the next save cannot write the old
    /// name back. A missing view is <see cref="DefaultView"/>, a view the retired
    /// <c>ShellView</c> enum named is its surface id, and a surface the retired
    /// <c>WorkspaceSurface</c> enum named is its takeover's id, or none for the workspace.
    /// </summary>
    private static (string? Surface, string View) Normalize(string? surface, string? view)
    {
        if (string.Equals(surface, RoadmapName, StringComparison.Ordinal))
        {
            return (null, RoadmapViewId);
        }

        return (SurfaceId(surface), string.IsNullOrWhiteSpace(view) ? DefaultView : ViewId(view));
    }

    /// <summary>The takeover's surface id <paramref name="surface"/> stands for: a name
    /// the retired enum stored as its id, the workspace as none, anything else as
    /// written.</summary>
    private static string? SurfaceId(string? surface) =>
        string.IsNullOrWhiteSpace(surface) ? null
        : RetiredSurfaceNames.TryGetValue(surface, out var id) ? id
        : surface;

    /// <summary>The atlas's way back as it is kept: a takeover's surface id, or none for
    /// the workspace — and for <c>Roadmap</c>, which the retired enum could still name
    /// though it was a view by then.</summary>
    private static string? AtlasReturnId(string? surface) =>
        string.Equals(surface, RoadmapName, StringComparison.Ordinal) ? null : SurfaceId(surface);

    /// <summary>The surface id <paramref name="view"/> stands for: a name the retired
    /// enum stored as its id, anything else as written.</summary>
    private static string ViewId(string view) =>
        RetiredViewNames.TryGetValue(view, out var id) ? id : view;

    public void SetRoadmapHoursShown(bool shown) => Update(() =>
    {
        if (shown == RoadmapHoursShown) return false;

        RoadmapHoursShown = shown;
        return true;
    });

    public void SetRoadmapCompletedShown(bool shown) => Update(() =>
    {
        if (shown == RoadmapCompletedShown) return false;

        RoadmapCompletedShown = shown;
        return true;
    });

    public void SetRoadmapCollapsedGroups(IReadOnlyCollection<string> collapsed)
    {
        string[] sorted = [.. collapsed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Update(() =>
        {
            if (sorted.SequenceEqual(RoadmapCollapsedGroups)) return false;

            RoadmapCollapsedGroups = sorted;
            return true;
        });
    }

    public void SetBoardColumns(string columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columns);

        Update(() =>
        {
            if (columns == BoardColumns) return false;

            BoardColumns = columns;
            return true;
        });
    }

    public void SetTasksGrouping(string grouping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grouping);

        Update(() =>
        {
            if (grouping == TasksGrouping) return false;

            TasksGrouping = grouping;
            return true;
        });
    }

    public void SetCalendarTasksShown(bool shown) => Update(() =>
    {
        if (shown == CalendarTasksShown) return false;

        CalendarTasksShown = shown;
        return true;
    });

    public void SetCalendarPlansShown(bool shown) => Update(() =>
    {
        if (shown == CalendarPlansShown) return false;

        CalendarPlansShown = shown;
        return true;
    });

    public void SetCalendarEventsShown(bool shown) => Update(() =>
    {
        if (shown == CalendarEventsShown) return false;

        CalendarEventsShown = shown;
        return true;
    });

    public void SetCalendarRange(string range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        Update(() =>
        {
            if (range == CalendarRange) return false;

            CalendarRange = range;
            return true;
        });
    }

    public void SetToolsTab(string tab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tab);

        Update(() =>
        {
            if (tab == ToolsTab) return false;

            ToolsTab = tab;
            return true;
        });
    }

    /// <summary>Replaces the atlas's place — its way back and its scope — in one save.</summary>
    public void SetAtlasPlace(string? returnSurface, string? scope)
    {
        returnSurface = AtlasReturnId(returnSurface);
        scope = string.IsNullOrWhiteSpace(scope) ? null : scope;

        Update(() =>
        {
            if (returnSurface == AtlasReturnSurface && scope == AtlasScope) return false;

            AtlasReturnSurface = returnSurface;
            AtlasScope = scope;
            return true;
        });
    }

    public void SetToolsHostFilter(string hostFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFilter);

        Update(() =>
        {
            if (hostFilter == ToolsHostFilter) return false;

            ToolsHostFilter = hostFilter;
            return true;
        });
    }

    /// <summary>Remembers that the C4 zoom navigation's hint was dismissed. There is no way to
    /// undo it: the hint is a first-run aid, and the zoom bar says the same things.</summary>
    public void DismissC4ZoomHint() => Update(() =>
    {
        if (C4ZoomHintDismissed) return false;

        C4ZoomHintDismissed = true;
        return true;
    });

    /// <summary>Replaces the notification centre's Group by, its three filters and its Show
    /// settled switch in one save; a blank one is its default.</summary>
    public void SetNotificationsView(string? groupBy, string? askedBy, string? kind, string? from, bool showSettled)
    {
        (groupBy, askedBy, kind, from) = (Blank(groupBy), Blank(askedBy), Blank(kind), Blank(from));

        Update(() =>
        {
            if (groupBy == NotificationsGroupBy && askedBy == NotificationsAskedBy && kind == NotificationsKind && from == NotificationsFrom
                && showSettled == NotificationsShowSettled) return false;

            NotificationsGroupBy = groupBy;
            NotificationsAskedBy = askedBy;
            NotificationsKind = kind;
            NotificationsFrom = from;
            NotificationsShowSettled = showSettled;
            return true;
        });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void SetShowArchivedRepositories(bool shown) => Update(() =>
    {
        if (shown == ShowArchivedRepositories) return false;

        ShowArchivedRepositories = shown;
        return true;
    });

    public void SetBrainHiddenShown(bool shown) => Update(() =>
    {
        if (shown == BrainHiddenShown) return false;

        BrainHiddenShown = shown;
        return true;
    });

    /// <summary>Remembers that the roadmap item <paramref name="itemId"/> was offered
    /// the archive move. One already remembered writes nothing.</summary>
    public void MarkRoadmapArchiveOffered(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        Update(() =>
        {
            if (RoadmapArchiveOffered.Contains(itemId, StringComparer.OrdinalIgnoreCase)) return false;

            RoadmapArchiveOffered = [.. RoadmapArchiveOffered.Append(itemId).Order(StringComparer.Ordinal)];
            return true;
        });
    }

    /// <summary>Keeps <paramref name="aliases"/> in the order given — the anchor first —
    /// so the same aliases reordered are a change.</summary>
    public void SetRepositoryScope(IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        string[] scope = [.. aliases];

        Update(() =>
        {
            if (scope.SequenceEqual(RepositoryScope, StringComparer.Ordinal)) return false;

            RepositoryScope = scope;
            return true;
        });
    }

    // --- A relabelled repository (local ADR 0060 §3) --------------------------

    AliasKeyedStoreKind IRepositoryAliasKeyedStore.Kind => AliasKeyedStoreKind.Local;

    /// <summary>The aliases the scope and the folded bands name, and the one a
    /// <c>repo:</c> or <c>ghost:</c> surface or atlas scope names. A folded band's id is
    /// not always an alias — the roadmap's own groups sit beside them — but no relabel
    /// names those, and the backstop reads replicated stores only.</summary>
    IReadOnlyCollection<string> IRepositoryAliasKeyedStore.HeldAliases()
    {
        lock (_gate)
        {
            var held = new HashSet<string>(StringComparer.Ordinal);

            foreach (var alias in RepositoryScope.Concat(RoadmapCollapsedGroups))
            {
                if (!string.IsNullOrWhiteSpace(alias)) held.Add(GitHubRepositoryRef.NormalizeAlias(alias));
            }

            foreach (var value in (string?[])[LastSurface, AtlasScope])
            {
                if (PrefixedAlias(value) is { } named) held.Add(GitHubRepositoryRef.NormalizeAlias(named.Alias));
            }

            return held;
        }
    }

    /// <summary>
    /// Moves every alias the mapping names to its new one, each value mapped once, so a
    /// swap never runs one move into the other. A list that then names an alias twice
    /// keeps it once, at the first place either held: the old alias's place when it
    /// stood first, the anchor included, since both now name the same repository. The
    /// Brain's empty scope value and a surface or scope that names no repository stay.
    /// Saved once, and only when something moved. Runs on the follower's thread, under
    /// the gate the setters take, so a choice made meanwhile is never lost.
    /// </summary>
    Task<bool> IRepositoryAliasKeyedStore.MoveAliasesAsync(
        IReadOnlyDictionary<string, string> oldToNew,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mapping = RepositoryAliasMapping.Of(oldToNew);

        var changed = Update(() =>
        {
            var moved = false;

            if (RepositoryScope.Any(mapping.Moves))
            {
                RepositoryScope = [.. RepositoryScope.Select(alias => mapping.MovedTo(alias) ?? alias).Distinct(StringComparer.OrdinalIgnoreCase)];
                moved = true;
            }

            if (RoadmapCollapsedGroups.Any(mapping.Moves))
            {
                RoadmapCollapsedGroups =
                [
                    .. RoadmapCollapsedGroups
                        .Select(group => mapping.MovedTo(group) ?? group)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                ];
                moved = true;
            }

            if (Moved(LastSurface, mapping) is { } surface)
            {
                LastSurface = surface;
                moved = true;
            }

            if (Moved(AtlasScope, mapping) is { } scope)
            {
                AtlasScope = scope;
                moved = true;
            }

            return moved;
        });

        return Task.FromResult(changed);
    }

    /// <summary>The prefix and the alias of a <c>repo:</c> or <c>ghost:</c> value, or
    /// null for any other.</summary>
    private static (string Prefix, string Alias)? PrefixedAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var prefix in AliasPrefixes)
        {
            if (value.Length > prefix.Length && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (value[..prefix.Length], value[prefix.Length..]);
            }
        }

        return null;
    }

    /// <summary><paramref name="value"/> with the alias it names moved, or null when it
    /// names none the mapping moves.</summary>
    private static string? Moved(string? value, RepositoryAliasMapping mapping) =>
        PrefixedAlias(value) is { } named && mapping.MovedTo(named.Alias) is { } target
            ? named.Prefix + target
            : null;

    /// <summary>
    /// Waits until every choice set on this store so far is on disk. The setters
    /// return before the file is written; this is for whoever must read the file
    /// itself — the app closing, or a test — rather than the properties.
    /// </summary>
    public void Flush() => WaitForPendingWrites(_file.Path);

    /// <summary>
    /// Waits until every write queued for the file at <paramref name="path"/> —
    /// by any store on it in this process — has landed.
    /// </summary>
    public static void WaitForPendingWrites(string path) => QueuedFileWrites.WaitFor(path);

    /// <summary>
    /// Snapshots the choices on the caller's thread and hands the file write to the
    /// pool (<see cref="QueuedFileWrites"/>).
    /// <para>
    /// The setters run on the click path — a view option, a takeover, a toggle — and
    /// on the desktop that is the renderer's UI thread, where a synchronous write
    /// held the press's own repaint behind the disk, or OneDrive, or a virus scan
    /// (.devbook/design/interaction-guidelines.md#press-feedback, "No synchronous IO
    /// on the UI thread"). The properties are the truth for this process and change
    /// at once, so nothing that reads them waits; only the file lags.
    /// </para>
    /// <para>
    /// The compare, the change, the snapshot and the enqueue all run under
    /// <see cref="_gate"/>: the setters run on the UI thread and a relabel's re-key on the
    /// alias follower's (local ADR 0060 §3), and without it one could drop the other's
    /// change, or enqueue an older snapshot after a newer one. <see cref="Changed"/> is
    /// raised after the gate is left, so no handler runs while holding it.
    /// </para>
    /// </summary>
    /// <param name="change">Applies the change to the properties and says whether
    /// anything changed; it runs under the gate.</param>
    /// <returns>Whether anything changed.</returns>
    private bool Update(Func<bool> change)
    {
        lock (_gate)
        {
            if (!change()) return false;

            QueuedFileWrites.Enqueue(_file.Path, Serialize());
        }

        Changed?.Invoke();
        return true;
    }

    private string Serialize() =>
            JsonSerializer.Serialize(new ShellNavigationDto
            {
                LastSurface = LastSurface,
                LastView = LastView,
                // Left out while on, so a file from before the switch keeps its shape.
                RoadmapHoursShown = RoadmapHoursShown ? null : false,
                RoadmapCompletedShown = RoadmapCompletedShown ? null : false,
                // Likewise left out while no band is folded.
                RoadmapCollapsedGroups = RoadmapCollapsedGroups.Count == 0 ? null : [.. RoadmapCollapsedGroups],
                // Likewise left out while the columns are the default ones.
                BoardColumns = BoardColumns == DefaultBoardColumns ? null : BoardColumns,
                // Likewise left out while the list groups by plan. A stored name: never
                // rename it without reading the old one.
                TasksGrouping = TasksGrouping == DefaultTasksGrouping ? null : TasksGrouping,
                // Left out while on, the default, so only turning it off writes a key. A
                // stored name: never rename it without reading the old one.
                CalendarTasksShown = CalendarTasksShown ? null : false,
                // Left out while off, the default, so only turning it on writes a key.
                CalendarPlansShown = CalendarPlansShown ? true : null,
                // The same for the events box. A stored name: never rename it without
                // reading the old one.
                CalendarEventsShown = CalendarEventsShown ? true : null,
                // Each of these is left out while at its default, the same way.
                CalendarRange = CalendarRange == DefaultCalendarRange ? null : CalendarRange,
                RepositoryScope = RepositoryScope.Count == 0 ? null : [.. RepositoryScope],
                // Left out until the tools pane has been on a tab or a filter here.
                ToolsTab = ToolsTab,
                ToolsHostFilter = ToolsHostFilter,
                // Left out while at their defaults. All four keys are stored names: never
                // rename them without reading the old ones.
                NotificationsGroupBy = NotificationsGroupBy,
                NotificationsAskedBy = NotificationsAskedBy,
                NotificationsKind = NotificationsKind,
                NotificationsFrom = NotificationsFrom,
                // Left out while off, the default. A stored name: never rename it without
                // reading the old one.
                NotificationsShowSettled = NotificationsShowSettled ? true : null,
                // Left out until the atlas has been opened here. Both keys are stored
                // names: never rename them without reading the old ones.
                AtlasReturnSurface = AtlasReturnSurface,
                AtlasScope = AtlasScope,
                // Left out while off, the default. The key is a stored name: never
                // rename it without reading the old one.
                ShowArchivedRepositories = ShowArchivedRepositories ? true : null,
                // Left out until dismissed. The key is a stored name: never rename it
                // without reading the old one.
                C4ZoomHintDismissed = C4ZoomHintDismissed ? true : null,
                // Likewise left out while off, and likewise a stored name.
                BrainHiddenShown = BrainHiddenShown ? true : null,
                // Left out until the first offer. A stored name: never rename it
                // without reading the old one.
                RoadmapArchiveOffered = RoadmapArchiveOffered.Count == 0 ? null : [.. RoadmapArchiveOffered]
            }, JsonOptions);

    private ShellNavigationDto? Read()
    {
        // A store on a path another store in this process has just written
        // reads what that one wrote, not the file as it stood before.
        WaitForPendingWrites(_file.Path);

        // A corrupt or unreachable file reads as nothing remembered.
        return _file.Load();
    }

    /// <summary>A file written before pins left the header also carries a
    /// <c>lastPinnedPanes</c> array. It is not read: the serializer skips a member the
    /// DTO does not declare, and the next save writes the file without it.</summary>
    private sealed class ShellNavigationDto
    {
        public string? LastSurface { get; init; }

        /// <summary>Absent from a file written before the view switch.</summary>
        public string? LastView { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RoadmapHoursShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RoadmapCompletedShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RoadmapCollapsedGroups { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BoardColumns { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TasksGrouping { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CalendarTasksShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CalendarPlansShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CalendarEventsShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalendarRange { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RepositoryScope { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolsTab { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolsHostFilter { get; init; }

        /// <summary>Written as <c>notificationsGroupBy</c>, the centre's slug.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NotificationsGroupBy { get; init; }

        /// <summary>Written as <c>notificationsAskedBy</c>, the centre's slug.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NotificationsAskedBy { get; init; }

        /// <summary>Written as <c>notificationsKind</c>, the centre's slug.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NotificationsKind { get; init; }

        /// <summary>Written as <c>notificationsFrom</c>, a system's name.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NotificationsFrom { get; init; }

        /// <summary>Written as <c>notificationsShowSettled</c>, and only while on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? NotificationsShowSettled { get; init; }

        /// <summary>Written as <c>atlasReturnSurface</c>, a takeover's surface id.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AtlasReturnSurface { get; init; }

        /// <summary>Written as <c>atlasScope</c>, a Lobes scope key.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AtlasScope { get; init; }

        /// <summary>Written as <c>showArchivedRepositories</c>, and only while on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ShowArchivedRepositories { get; init; }

        /// <summary>Written as <c>c4ZoomHintDismissed</c>, and only once dismissed.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? C4ZoomHintDismissed { get; init; }
        /// <summary>Written as <c>brainHiddenShown</c>, and only while on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? BrainHiddenShown { get; init; }

        /// <summary>Written as <c>roadmapArchiveOffered</c>, and only once a plan was offered.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RoadmapArchiveOffered { get; init; }
    }
}
