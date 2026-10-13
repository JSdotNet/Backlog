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
/// Holds the surface and the pane names as plain strings rather than the
/// shell's own enums, which are internal to the desktop UI project and not
/// visible from here — the shell is the one that knows what the names mean
/// and is the only reader of them.
/// </para>
/// <para>
/// One file for all three, not three, because a fresh shell instance restores them
/// together: which takeover was open, which main view the workspace was showing
/// under it and which side panes were open beside that view are all "what the
/// reader was looking at", read back in the same <c>OnInitializedAsync</c>.
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
/// So do the Calendar's Month | Week range, the In progress view's two toggles and the
/// header's repository scope: each is how the reader last had a screen, kept on this
/// device only. Every key is additive and left out while at its default, so a file
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

    /// <summary>The main view a file with no <c>lastView</c> reopens on: before the
    /// view switch the workspace's main pane was the task list.</summary>
    public const string DefaultView = "Tasks";

    /// <summary>What the Board's columns are when nothing else was remembered.</summary>
    public const string DefaultBoardColumns = "Status";

    /// <summary>The Calendar's range when nothing else was remembered.</summary>
    public const string DefaultCalendarRange = "Month";

    /// <summary>The surface name the roadmap was stored under while it was a
    /// takeover, and the view name it is stored under now.</summary>
    private const string RoadmapName = "Roadmap";

    /// <summary>The surface that is no takeover at all: the workspace.</summary>
    private const string WorkspaceSurfaceName = "Workspace";

    /// <summary>The pane name the Inbox was stored under while it was a side pane,
    /// and the surface name it is stored under now.</summary>
    private const string InboxName = "Inbox";

    private readonly string _path;

    public ShellNavigationStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "shell-navigation.json"))
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
        (LastSurface, LastEnabledPanes) = MigrateInboxPane(dto?.LastSurface, LastSurface, dto?.LastEnabledPanes ?? Empty);
        RoadmapHoursShown = dto?.RoadmapHoursShown ?? true;
        RoadmapCollapsedGroups = dto?.RoadmapCollapsedGroups ?? Empty;
        BoardColumns = string.IsNullOrWhiteSpace(dto?.BoardColumns) ? DefaultBoardColumns : dto.BoardColumns;
        CalendarPlansShown = dto?.CalendarPlansShown ?? false;
        CalendarRange = string.IsNullOrWhiteSpace(dto?.CalendarRange) ? DefaultCalendarRange : dto.CalendarRange;
        InProgressNeedsYouFirst = dto?.InProgressNeedsYouFirst ?? true;
        InProgressIncludeFinishedToday = dto?.InProgressIncludeFinishedToday ?? false;
        RepositoryScope = MigrateNoRepositoryOnly(dto?.RepositoryScope ?? Empty, dto?.NoRepositoryOnly);
    }

    /// <summary>Raised after anything remembered here changes, so nothing has
    /// to poll the file to notice.</summary>
    public event Action? Changed;

    /// <summary>The surface that was last set, or null when nothing has been
    /// remembered yet — first launch included.</summary>
    public string? LastSurface { get; private set; }

    /// <summary>The main view the workspace was showing when last set — by name,
    /// <c>Tasks</c>, <c>Board</c>, <c>Calendar</c>, <c>Roadmap</c> or <c>InProgress</c>
    /// — and <see cref="DefaultView"/> when nothing has been remembered yet.</summary>
    public string LastView { get; private set; }

    /// <summary>The side panes that were open beside the main view when last set.
    /// Empty is both "nothing remembered yet" and "no side pane open", which the
    /// shell reopens the same way. A file from before the view switch may still
    /// list <c>Tasks</c> here; the shell ignores it, since the task list is a view
    /// now and no longer a pane. An <c>Inbox</c> here is read away on load — see
    /// <see cref="MigrateInboxPane"/> — since the Inbox is a surface now.</summary>
    public IReadOnlyList<string> LastEnabledPanes { get; private set; }

    /// <summary>Whether the roadmap's day and week heads carry their hours line: on until
    /// the reader turns the Hours switch off on this device.</summary>
    public bool RoadmapHoursShown { get; private set; }

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

    /// <summary>Whether the In progress view puts the sessions waiting on the reader
    /// first — its "Needs you first" toggle: on until turned off on this device.</summary>
    public bool InProgressNeedsYouFirst { get; private set; }

    /// <summary>Whether the In progress view also lists what finished today — its
    /// "Include finished today" toggle: off until turned on on this device.</summary>
    public bool InProgressIncludeFinishedToday { get; private set; }

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
        if (view == LastView) return;

        LastView = view;
        Save();
    }

    /// <summary>
    /// The surface and the view as they are kept: the roadmap as a view, never as a
    /// surface. A file from before the view switch names <c>Roadmap</c> as its
    /// surface; it reopens on the Roadmap view over the workspace, and since the
    /// in-memory surface is then the workspace, the next save cannot write the old
    /// name back. A missing view is <see cref="DefaultView"/>.
    /// </summary>
    private static (string? Surface, string View) Normalize(string? surface, string? view)
    {
        if (string.Equals(surface, RoadmapName, StringComparison.Ordinal))
        {
            return (WorkspaceSurfaceName, RoadmapName);
        }

        return (surface, string.IsNullOrWhiteSpace(view) ? DefaultView : view);
    }

    /// <summary>
    /// The Inbox was a side pane beside the main view before it took the whole screen
    /// as a surface (local ADR 0022, amendment of 2026-10-12), so a file from then
    /// lists it among the panes. Where it was on screen — no takeover stored over the
    /// workspace — it reopens as the Inbox surface; under a takeover it was hidden, so
    /// that takeover stays. Either way the name leaves the pane list, and the next save
    /// cannot write it back there. Decided on the stored surface, before the roadmap
    /// takeover is read as a view, because that takeover covered the pane too.
    /// </summary>
    private static (string? Surface, IReadOnlyList<string> Panes) MigrateInboxPane(
        string? storedSurface, string? surface, IReadOnlyList<string> panes)
    {
        if (!panes.Contains(InboxName, StringComparer.Ordinal)) return (surface, panes);

        string[] remaining = [.. panes.Where(pane => !string.Equals(pane, InboxName, StringComparison.Ordinal))];
        var inboxWasOnScreen = storedSurface is null || string.Equals(storedSurface, WorkspaceSurfaceName, StringComparison.Ordinal);

        return (inboxWasOnScreen ? InboxName : surface, remaining);
    }

    public void SetLastPanes(IReadOnlyList<string> enabled)
    {
        if (enabled.SequenceEqual(LastEnabledPanes)) return;

        LastEnabledPanes = [.. enabled];
        Save();
    }

    public void SetRoadmapHoursShown(bool shown)
    {
        if (shown == RoadmapHoursShown) return;

        RoadmapHoursShown = shown;
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

    public void SetInProgressNeedsYouFirst(bool needsYouFirst)
    {
        if (needsYouFirst == InProgressNeedsYouFirst) return;

        InProgressNeedsYouFirst = needsYouFirst;
        Save();
    }

    public void SetInProgressIncludeFinishedToday(bool include)
    {
        if (include == InProgressIncludeFinishedToday) return;

        InProgressIncludeFinishedToday = include;
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

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new ShellNavigationDto
            {
                LastSurface = LastSurface,
                LastView = LastView,
                LastEnabledPanes = [.. LastEnabledPanes],
                // Left out while on, so a file from before the switch keeps its shape.
                RoadmapHoursShown = RoadmapHoursShown ? null : false,
                // Likewise left out while no band is folded.
                RoadmapCollapsedGroups = RoadmapCollapsedGroups.Count == 0 ? null : [.. RoadmapCollapsedGroups],
                // Likewise left out while the columns are the default ones.
                BoardColumns = BoardColumns == DefaultBoardColumns ? null : BoardColumns,
                // Left out while off, the default, so only turning it on writes a key.
                CalendarPlansShown = CalendarPlansShown ? true : null,
                // Each of these is left out while at its default, the same way.
                CalendarRange = CalendarRange == DefaultCalendarRange ? null : CalendarRange,
                InProgressNeedsYouFirst = InProgressNeedsYouFirst ? null : false,
                InProgressIncludeFinishedToday = InProgressIncludeFinishedToday ? true : null,
                RepositoryScope = RepositoryScope.Count == 0 ? null : [.. RepositoryScope]
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing this write only costs the next launch its restore; what
            // is on screen right now is unaffected.
        }

        Changed?.Invoke();
    }

    private ShellNavigationDto? Read()
    {
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

        public string[]? LastEnabledPanes { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? RoadmapHoursShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RoadmapCollapsedGroups { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BoardColumns { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CalendarPlansShown { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalendarRange { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? InProgressNeedsYouFirst { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? InProgressIncludeFinishedToday { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? RepositoryScope { get; init; }

        /// <summary>The retired "No repo" chip's state, read only to migrate it — see
        /// <see cref="MigrateNoRepositoryOnly"/>. Never set on a save, so never written.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? NoRepositoryOnly { get; init; }
    }
}
