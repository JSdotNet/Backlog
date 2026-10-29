using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.SharedKernel;

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
/// are gone (local ADR 0022, amendment of 2026-10-15); a file that still lists
/// them is read once, to migrate it — see <see cref="MigrateSidePanes"/> — and
/// that list is never written again.
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
/// So do the Calendar's Month | Week range and the
/// header's repository scope, and the system tools pane's tab and host filter: each is
/// how the reader last had a screen, kept on this device only. So is show archived
/// (local ADR 0029, §3), whether the archived repositories are offered where they are
/// otherwise hidden. Every key is additive and left out while at its default — the tools
/// pane's two until it has been on a tab or a filter here — so a file
/// written before it keeps its shape and an older build reads a newer file.
/// </para>
/// </summary>
public sealed class ShellNavigationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] Empty = [];

    /// <summary>The main view a file with no <c>lastView</c> reopens on, by its surface
    /// id (local ADR 0042 §2): before the view switch the workspace's main pane was the
    /// task list.</summary>
    public const string DefaultView = "tasks";

    /// <summary>The surface id the roadmap's view is stored under (local ADR 0042 §2).</summary>
    private const string RoadmapViewId = "roadmap";

    /// <summary>
    /// The view names a file from before local ADR 0042 holds — the members of the
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
    /// The surface names a file from before local ADR 0042 holds — the members of the
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
        };

    /// <summary>What the Board's columns are when nothing else was remembered.</summary>
    public const string DefaultBoardColumns = "Status";

    /// <summary>The Calendar's range when nothing else was remembered.</summary>
    public const string DefaultCalendarRange = "Month";

    /// <summary>The surface name the roadmap was stored under while it was a
    /// takeover; it reopens as the Roadmap view.</summary>
    private const string RoadmapName = "Roadmap";

    /// <summary>The surface name the retired enum wrote for no takeover at all: the
    /// workspace. Now nothing is written for it.</summary>
    private const string WorkspaceSurfaceName = "Workspace";

    /// <summary>The Inbox's and the Devbook's surface ids (local ADR 0042 §2).</summary>
    private const string InboxId = "inbox";
    private const string DevbookId = "devbook";

    /// <summary>The pane name the Inbox was stored under while it was a side pane,
    /// and the surface name the retired enum stored it under.</summary>
    private const string InboxName = "Inbox";

    /// <summary>The pane name the Devbook was stored under while it was a side pane,
    /// and the surface name the retired enum stored it under.</summary>
    private const string DevbookName = "Devbook";

    /// <summary>The pane name the Devbook was stored under before the Knowledge
    /// bounded context was renamed to Devbook.</summary>
    private const string LegacyDevbookName = "Knowledge";

    /// <summary>The format <see cref="GoalsTwoWeekStart"/> is stored in.</summary>
    private const string IsoDate = "yyyy-MM-dd";

    /// <summary>The file's name, in the head's own app-data folder.</summary>
    public const string FileName = "shell-navigation.json";

    private readonly string _path;

    /// <summary>The file in <see cref="WorkspaceSettingsStore.DefaultAppDataDirectory"/>:
    /// <c>Backlog.Debug</c> for a Debug build, <c>Backlog</c> for the installed app, so
    /// a head started out of a checkout never writes what the installed app restores.</summary>
    public ShellNavigationStore()
        : this(Path.Combine(WorkspaceSettingsStore.DefaultAppDataDirectory, FileName))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test —
    /// or a session running beside another — a store that does not fight over
    /// the real per-user file.</summary>
    public ShellNavigationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = Read();
        (LastSurface, LastView) = Normalize(dto?.LastSurface, dto?.LastView);
        LastSurface = MigrateSidePanes(dto?.LastSurface, LastSurface, dto?.LastEnabledPanes ?? Empty);
        RoadmapHoursShown = dto?.RoadmapHoursShown ?? true;
        RoadmapCompletedShown = dto?.RoadmapCompletedShown ?? true;
        RoadmapCollapsedGroups = dto?.RoadmapCollapsedGroups ?? Empty;
        BoardColumns = string.IsNullOrWhiteSpace(dto?.BoardColumns) ? DefaultBoardColumns : dto.BoardColumns;
        CalendarPlansShown = dto?.CalendarPlansShown ?? false;
        CalendarRange = string.IsNullOrWhiteSpace(dto?.CalendarRange) ? DefaultCalendarRange : dto.CalendarRange;
        RepositoryScope = MigrateNoRepositoryOnly(dto?.RepositoryScope ?? Empty, dto?.NoRepositoryOnly);
        ToolsTab = string.IsNullOrWhiteSpace(dto?.ToolsTab) ? null : dto.ToolsTab;
        ToolsHostFilter = string.IsNullOrWhiteSpace(dto?.ToolsHostFilter) ? null : dto.ToolsHostFilter;
        ShowArchivedRepositories = dto?.ShowArchivedRepositories ?? false;
        BrainHiddenShown = dto?.BrainHiddenShown ?? false;
        GoalsIterationLength = string.IsNullOrWhiteSpace(dto?.GoalsIterationLength) ? null : dto.GoalsIterationLength;
        GoalsTwoWeekStart = DateOnly.TryParseExact(dto?.GoalsTwoWeekStart, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? start
            : null;
    }

    /// <summary>Raised after anything remembered here changes, so nothing has
    /// to poll the file to notice.</summary>
    public event Action? Changed;

    /// <summary>The surface that was last set, or null when nothing has been
    /// remembered yet — first launch included.</summary>
    public string? LastSurface { get; private set; }

    /// <summary>The main view the workspace was showing when last set — by its surface
    /// id, <c>tasks</c>, <c>board</c>, <c>calendar</c>, <c>roadmap</c> or <c>goals</c>
    /// (local ADR 0042 §2) — and <see cref="DefaultView"/> when nothing has been
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

    /// <summary>Whether the Tasks Calendar draws the roadmap's plans — its "Show plans"
    /// box: off until the reader turns it on on this device. A file that carries no
    /// choice reads as off; one that says <c>false</c>, as a file from when the box
    /// was on by default does once it was turned off, reads as off too.</summary>
    public bool CalendarPlansShown { get; private set; }

    /// <summary>
    /// The Tasks Calendar's range — <c>Month</c> or <c>Week</c> — as the reader last
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

    /// <summary>Whether the archived repositories are shown where they are otherwise
    /// hidden — show archived (local ADR 0029, §3): off until turned on on this device.
    /// Read through the shared kernel's <c>IShowArchivedRepositories</c>.</summary>
    public bool ShowArchivedRepositories { get; private set; }

    /// <summary>Whether the Brain's hidden folders — the ignored ones and the
    /// dot-folders — show as tabs in the Devbook pane, Show hidden (local ADR 0026
    /// §2): off until turned on on this device. Read through the Devbook module's
    /// <c>IBrainShowHidden</c>.</summary>
    public bool BrainHiddenShown { get; private set; }

    /// <summary>The iteration length an earlier build kept per device, as the setting's
    /// value — <c>day</c>, <c>week</c>, <c>two-weeks</c> or <c>month</c>; null until one was
    /// chosen. Legacy: the choice now lives in <c>IterationSettingsStore</c>, which travels
    /// between desktops and reads this only to seed itself once (local ADR 0036, §5); this
    /// build never writes it.</summary>
    public string? GoalsIterationLength { get; private set; }

    /// <summary>The day two-week iterations count from, stored as an ISO date; null until
    /// two weeks was first chosen, and for a stored value that is no date.</summary>
    public DateOnly? GoalsTwoWeekStart { get; private set; }

    /// <summary>Where the choices are written.</summary>
    public string SettingsPath => _path;

    public void SetLastSurface(string? surface)
    {
        var (normalizedSurface, view) = Normalize(surface, LastView);
        if (normalizedSurface == LastSurface && view == LastView) return;

        LastSurface = normalizedSurface;
        LastView = view;
        Save();
    }

    public void SetLastView(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        view = ViewId(view);
        if (view == LastView) return;

        LastView = view;
        Save();
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

    /// <summary>The surface id <paramref name="view"/> stands for: a name the retired
    /// enum stored as its id, anything else as written.</summary>
    private static string ViewId(string view) =>
        RetiredViewNames.TryGetValue(view, out var id) ? id : view;

    /// <summary>
    /// The Inbox and then the Devbook were side panes beside the main view before
    /// each took the whole screen as a surface (local ADR 0022, amendments of
    /// 2026-10-12 and 2026-10-15), so a file from then lists them among the panes —
    /// the Devbook perhaps as <c>Knowledge</c>, its name before the rename. Where a
    /// pane was on screen — no takeover stored over the workspace — it reopens as its
    /// surface; under a takeover it was hidden, so that takeover stays.
    /// <para>
    /// With both listed the Inbox wins: from the day the Inbox took the screen until
    /// the Devbook did, the Inbox surface covered the Devbook pane, and before that
    /// the two sat side by side, where the Inbox — the queue — is the likelier one
    /// to have been read. <c>Tasks</c>, or the older <c>Backlog</c>, names the view
    /// the file reopens on anyway and changes nothing.
    /// </para>
    /// <para>
    /// The list itself is not kept: nothing is a side pane any more, so the next save
    /// writes the file without it. Decided on the stored surface, before the roadmap
    /// takeover is read as a view, because that takeover covered the panes too.
    /// </para>
    /// </summary>
    private static string? MigrateSidePanes(string? storedSurface, string? surface, IReadOnlyList<string> panes)
    {
        var panesWereOnScreen = storedSurface is null || string.Equals(storedSurface, WorkspaceSurfaceName, StringComparison.Ordinal);
        if (!panesWereOnScreen) return surface;

        if (panes.Contains(InboxName, StringComparer.Ordinal)) return InboxId;

        if (panes.Contains(DevbookName, StringComparer.Ordinal) || panes.Contains(LegacyDevbookName, StringComparer.Ordinal))
        {
            return DevbookId;
        }

        return surface;
    }

    public void SetRoadmapHoursShown(bool shown)
    {
        if (shown == RoadmapHoursShown) return;

        RoadmapHoursShown = shown;
        Save();
    }

    public void SetRoadmapCompletedShown(bool shown)
    {
        if (shown == RoadmapCompletedShown) return;

        RoadmapCompletedShown = shown;
        Save();
    }

    public void SetRoadmapCollapsedGroups(IReadOnlyCollection<string> collapsed)
    {
        string[] sorted = [.. collapsed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (sorted.SequenceEqual(RoadmapCollapsedGroups)) return;

        RoadmapCollapsedGroups = sorted;
        Save();
    }

    public void SetBoardColumns(string columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columns);
        if (columns == BoardColumns) return;

        BoardColumns = columns;
        Save();
    }

    public void SetCalendarPlansShown(bool shown)
    {
        if (shown == CalendarPlansShown) return;

        CalendarPlansShown = shown;
        Save();
    }

    public void SetCalendarRange(string range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(range);
        if (range == CalendarRange) return;

        CalendarRange = range;
        Save();
    }

    public void SetToolsTab(string tab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tab);
        if (tab == ToolsTab) return;

        ToolsTab = tab;
        Save();
    }

    public void SetToolsHostFilter(string hostFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFilter);
        if (hostFilter == ToolsHostFilter) return;

        ToolsHostFilter = hostFilter;
        Save();
    }

    /// <summary>Replaces both iteration choices in one save, so a first choice of two weeks
    /// writes its length and its start together. Kept for the files an earlier build wrote
    /// and the tests that write one; the app no longer calls it (local ADR 0036, §5).</summary>
    public void SetGoalsIteration(string length, DateOnly? twoWeekStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(length);
        if (length == GoalsIterationLength && twoWeekStart == GoalsTwoWeekStart) return;

        GoalsIterationLength = length;
        GoalsTwoWeekStart = twoWeekStart;
        Save();
    }

    public void SetShowArchivedRepositories(bool shown)
    {
        if (shown == ShowArchivedRepositories) return;

        ShowArchivedRepositories = shown;
        Save();
    }

    public void SetBrainHiddenShown(bool shown)
    {
        if (shown == BrainHiddenShown) return;

        BrainHiddenShown = shown;
        Save();
    }

    /// <summary>Keeps <paramref name="aliases"/> in the order given — the anchor first —
    /// so the same aliases reordered are a change.</summary>
    public void SetRepositoryScope(IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (aliases.SequenceEqual(RepositoryScope, StringComparer.Ordinal)) return;

        RepositoryScope = [.. aliases];
        Save();
    }

    /// <summary>
    /// The Tasks filter bar's "No repo" chip, which the Brain replaced (local ADR 0027,
    /// §5), was never remembered, so no file written by this store carries it. A file
    /// that does — <c>noRepositoryOnly: true</c>, the name the chip's state went by —
    /// reopens on the Brain alone: the chip narrowed whatever repositories were scoped
    /// to the rows filed against none, and "against none" is the Brain. The key is
    /// never written back, so the next save drops it. A guard, kept so that the old
    /// name is read as what it meant should it ever turn up.
    /// </summary>
    private static IReadOnlyList<string> MigrateNoRepositoryOnly(IReadOnlyList<string> scope, bool? noRepositoryOnly) =>
        noRepositoryOnly is true ? [BrainScope.Value] : scope;

    /// <summary>
    /// Waits until every choice set on this store so far is on disk. The setters
    /// return before the file is written; this is for whoever must read the file
    /// itself — the app closing, or a test — rather than the properties.
    /// </summary>
    public void Flush() => WaitForPendingWrites(_path);

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
    /// </summary>
    private void Save()
    {
        QueuedFileWrites.Enqueue(_path, Serialize());
        Changed?.Invoke();
    }

    private string Serialize() =>
            JsonSerializer.Serialize(new ShellNavigationDto
            {
                LastSurface = LastSurface,
                LastView = LastView,
                // LastEnabledPanes is never set: the side panes are gone, and the
                // list a file from then held was migrated when it was read.
                // Left out while on, so a file from before the switch keeps its shape.
                RoadmapHoursShown = RoadmapHoursShown ? null : false,
                RoadmapCompletedShown = RoadmapCompletedShown ? null : false,
                // Likewise left out while no band is folded.
                RoadmapCollapsedGroups = RoadmapCollapsedGroups.Count == 0 ? null : [.. RoadmapCollapsedGroups],
                // Likewise left out while the columns are the default ones.
                BoardColumns = BoardColumns == DefaultBoardColumns ? null : BoardColumns,
                // Left out while off, the default, so only turning it on writes a key.
                CalendarPlansShown = CalendarPlansShown ? true : null,
                // Each of these is left out while at its default, the same way.
                CalendarRange = CalendarRange == DefaultCalendarRange ? null : CalendarRange,
                RepositoryScope = RepositoryScope.Count == 0 ? null : [.. RepositoryScope],
                // Left out until the tools pane has been on a tab or a filter here.
                ToolsTab = ToolsTab,
                ToolsHostFilter = ToolsHostFilter,
                // Left out while off, the default. The key is a stored name: never
                // rename it without reading the old one.
                ShowArchivedRepositories = ShowArchivedRepositories ? true : null,
                // Likewise left out while off, and likewise a stored name.
                BrainHiddenShown = BrainHiddenShown ? true : null,
                // Left out until chosen. Both keys are stored names: never rename them
                // without reading the old ones.
                GoalsIterationLength = GoalsIterationLength,
                GoalsTwoWeekStart = GoalsTwoWeekStart?.ToString(IsoDate, CultureInfo.InvariantCulture)
            }, JsonOptions);

    private ShellNavigationDto? Read()
    {
        // A store on a path another store in this process has just written
        // reads what that one wrote, not the file as it stood before.
        WaitForPendingWrites(_path);

        try
        {
            if (!File.Exists(_path)) return null;

            return JsonSerializer.Deserialize<ShellNavigationDto>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from
            // opening — fall back to nothing remembered.
            return null;
        }
    }

    /// <summary>A file written before pins left the header also carries a
    /// <c>lastPinnedPanes</c> array. It is not read: the serializer skips a member the
    /// DTO does not declare, and the next save writes the file without it.</summary>
    private sealed class ShellNavigationDto
    {
        public string? LastSurface { get; init; }

        /// <summary>Absent from a file written before the view switch.</summary>
        public string? LastView { get; init; }

        /// <summary>The side panes a file from before the Devbook took the screen
        /// had open, read only to migrate them — see <see cref="MigrateSidePanes"/>.
        /// Never set on a save, so never written.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? LastEnabledPanes { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RoadmapHoursShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RoadmapCompletedShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RoadmapCollapsedGroups { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BoardColumns { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CalendarPlansShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalendarRange { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RepositoryScope { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolsTab { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolsHostFilter { get; init; }

        /// <summary>Written as <c>showArchivedRepositories</c>, and only while on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ShowArchivedRepositories { get; init; }

        /// <summary>Written as <c>brainHiddenShown</c>, and only while on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? BrainHiddenShown { get; init; }

        /// <summary>Written as <c>goalsIterationLength</c>: <c>day</c>, <c>week</c>, <c>two-weeks</c> or <c>month</c>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GoalsIterationLength { get; init; }

        /// <summary>Written as <c>goalsTwoWeekStart</c>, an ISO date. A string rather than a
        /// date, so a hand-edited value that is no date reads as none instead of failing
        /// the whole file.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GoalsTwoWeekStart { get; init; }

        /// <summary>The retired "No repo" chip's state, read only to migrate it — see
        /// <see cref="MigrateNoRepositoryOnly"/>. Never set on a save, so never written.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? NoRepositoryOnly { get; init; }
    }
}
