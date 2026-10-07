using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>The surface name the roadmap was stored under while it was a
    /// takeover, and the view name it is stored under now.</summary>
    private const string RoadmapName = "Roadmap";

    /// <summary>The surface that is no takeover at all: the workspace.</summary>
    private const string WorkspaceSurfaceName = "Workspace";

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
        LastEnabledPanes = dto?.LastEnabledPanes ?? Empty;
        RoadmapHoursShown = dto?.RoadmapHoursShown ?? true;
        RoadmapCollapsedGroups = dto?.RoadmapCollapsedGroups ?? Empty;
        CalendarPlansShown = dto?.CalendarPlansShown ?? true;
    }

    /// <summary>Raised after anything remembered here changes, so nothing has
    /// to poll the file to notice.</summary>
    public event Action? Changed;

    /// <summary>The surface that was last set, or null when nothing has been
    /// remembered yet — first launch included.</summary>
    public string? LastSurface { get; private set; }

    /// <summary>The main view the workspace was showing when last set — by name,
    /// <c>Tasks</c> or <c>Roadmap</c> — and <see cref="DefaultView"/> when nothing
    /// has been remembered yet.</summary>
    public string LastView { get; private set; }

    /// <summary>The side panes that were open beside the main view when last set.
    /// Empty is both "nothing remembered yet" and "no side pane open", which the
    /// shell reopens the same way. A file from before the view switch may still
    /// list <c>Tasks</c> here; the shell ignores it, since the task list is a view
    /// now and no longer a pane.</summary>
    public IReadOnlyList<string> LastEnabledPanes { get; private set; }

    /// <summary>Whether the roadmap's day and week heads carry their hours line: on until
    /// the reader turns the Hours switch off on this device.</summary>
    public bool RoadmapHoursShown { get; private set; }

    /// <summary>The roadmap bands, by group id, the reader folded to one lane on this
    /// device. Empty until one is folded.</summary>
    public IReadOnlyList<string> RoadmapCollapsedGroups { get; private set; }

    /// <summary>Whether the Tasks Calendar draws the roadmap's plans — its "Show plans"
    /// box: on until the reader turns it off on this device.</summary>
    public bool CalendarPlansShown { get; private set; }

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

    public void SetCalendarPlansShown(bool shown)
    {
        if (shown == CalendarPlansShown) return;

        CalendarPlansShown = shown;
        Save();
    }

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
                // Left out while on, as the Hours switch is.
                CalendarPlansShown = CalendarPlansShown ? null : false
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
        public bool? CalendarPlansShown { get; init; }
    }
}
