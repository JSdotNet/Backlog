using System.Globalization;
using System.Text.Json;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The named weekly usage resets as a small JSON file beside the working week, read once
/// and written on every change — <see cref="WorkingHoursSettingsStore"/>'s shape, for the
/// same reason: it is the person's choice, not the workspace's, and it has to be
/// readable before any dashboard part has asked for it.
/// </summary>
/// <remarks>
/// <para>
/// The list and the choice of which reset cuts the dashboard's weeks share the one file,
/// so removing the chosen reset and moving the choice off it is one write, and one store
/// raises one <see cref="Changed"/>.
/// </para>
/// <para>
/// A file written before there could be more than one reset — <c>{ "day", "time" }</c> at
/// the root — reads as one chosen reset with id <c>default</c>, and is left as it was
/// until the first change writes the new shape over it.
/// </para>
/// </remarks>
public sealed class UsageResetSettingsStore : IUsageResetSettings
{
    private const int MaxNameLength = NamedUsageReset.MaxNameLength;

    private const string LegacyId = "default";

    private const string LegacyName = "Weekly reset";

    private const string SaveFailed = "Changed, but the weekly reset couldn't be saved for next time.";

    private const string Missing = "That reset is no longer there.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    private readonly string _path;

    private readonly Lock _gate = new();

    // Replaced whole on every change, so a reader on another thread sees one state or
    // the next, never a list half-edited.
    private volatile State _state;

    public UsageResetSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "usage-reset.json"))
    {
    }

    public UsageResetSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _state = Read();
    }

    public event Action? Changed;

    public IReadOnlyList<NamedUsageReset> Resets => _state.Resets;

    public NamedUsageReset? Selected => _state.Selected;

    /// <summary>What is in force, or null when nothing has been configured.</summary>
    public UsageWeekReset? Current => Selected?.Reset;

    public string SettingsPath => _path;

    public (string? Id, string? Error) Add(string name, DayOfWeek day, TimeOnly time)
    {
        ArgumentNullException.ThrowIfNull(name);

        string? error;
        string id;

        lock (_gate)
        {
            var state = _state;

            if (Refusal(state, name, exceptId: null) is { } refused) return (null, refused);

            id = NewId(state);

            error = Save(state with { Resets = [.. state.Resets, new NamedUsageReset(id, name.Trim(), new UsageWeekReset(day, time))] });
        }

        Changed?.Invoke();

        return (id, error);
    }

    public string? Update(string id, string name, DayOfWeek day, TimeOnly time)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);

        string? error;

        lock (_gate)
        {
            var state = _state;
            var index = IndexOf(state, id);

            if (index < 0) return Missing;
            if (Refusal(state, name, exceptId: id) is { } refused) return refused;

            var updated = new NamedUsageReset(id, name.Trim(), new UsageWeekReset(day, time));

            if (state.Resets[index] == updated) return null;

            var resets = state.Resets.ToArray();
            resets[index] = updated;

            error = Save(state with { Resets = resets });
        }

        Changed?.Invoke();

        return error;
    }

    public string? Remove(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        string? error;

        lock (_gate)
        {
            var state = _state;

            if (IndexOf(state, id) < 0) return null;

            // Removing the chosen reset forgets the choice, which hands the cut to the
            // first one left.
            error = Save(new State(
                [.. state.Resets.Where(reset => reset.Id != id)],
                state.SelectedId == id ? null : state.SelectedId));
        }

        Changed?.Invoke();

        return error;
    }

    public string? Select(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        string? error;

        lock (_gate)
        {
            var state = _state;

            if (IndexOf(state, id) < 0) return Missing;
            if (state.Selected?.Id == id) return null;

            error = Save(state with { SelectedId = id });
        }

        Changed?.Invoke();

        return error;
    }

    private static string? Refusal(State state, string name, string? exceptId)
    {
        var trimmed = name.Trim();

        if (trimmed.Length == 0) return "Give the reset a name.";
        if (trimmed.Length > MaxNameLength) return $"Keep the name to {MaxNameLength} characters.";

        return state.Resets.Any(reset => reset.Id != exceptId && string.Equals(reset.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ? $"There is already a reset called {trimmed}."
            : null;
    }

    private static int IndexOf(State state, string id)
    {
        for (var index = 0; index < state.Resets.Count; index++)
        {
            if (state.Resets[index].Id == id) return index;
        }

        return -1;
    }

    private static string NewId(State state)
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..8];

            if (IndexOf(state, id) < 0) return id;
        }
    }

    /// <summary>Puts the state in force and writes it — the new shape always, and no
    /// file at all once the list is empty. Returns the message for a write that
    /// failed.</summary>
    private string? Save(State state)
    {
        _state = state;

        try
        {
            if (state.Resets.Count == 0)
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            else
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(new ResetsFileDto
                {
                    Resets =
                    [
                        .. state.Resets.Select(reset => new ResetDto
                        {
                            Id = reset.Id,
                            Name = reset.Name,
                            Day = reset.Reset.Day.ToString(),
                            Time = reset.Reset.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
                        })
                    ],
                    Selected = state.Selected?.Id
                }, JsonOptions));
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SaveFailed;
        }
    }

    private State Read()
    {
        try
        {
            if (!File.Exists(_path)) return State.Empty;

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return State.Empty;

            if (Property(root, "resets") is { } list)
            {
                return list.ValueKind == JsonValueKind.Array
                    ? new State(ReadEntries(list), StringOf(root, "selected"))
                    : State.Empty;
            }

            // The shape a single-reset version wrote.
            return ReadReset(root) is { } legacy
                ? new State([new NamedUsageReset(LegacyId, LegacyName, legacy)], LegacyId)
                : State.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return State.Empty;
        }
    }

    /// <summary>The entries that parse, in file order. An entry with no id, no name, an
    /// unreadable day or time, or an id or name an earlier entry already has is skipped,
    /// so one bad hand edit costs that entry and not the list.</summary>
    private static NamedUsageReset[] ReadEntries(JsonElement list)
    {
        var resets = new List<NamedUsageReset>();

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var id = StringOf(entry, "id")?.Trim();
            var name = StringOf(entry, "name")?.Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || name.Length > MaxNameLength) continue;
            if (ReadReset(entry) is not { } reset) continue;
            if (resets.Any(known => known.Id == id || string.Equals(known.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

            resets.Add(new NamedUsageReset(id, name, reset));
        }

        return [.. resets];
    }

    private static UsageWeekReset? ReadReset(JsonElement element)
    {
        var dayText = StringOf(element, "day");
        var timeText = StringOf(element, "time");

        if (dayText is null
            || timeText is null
            || !Enum.TryParse<DayOfWeek>(dayText, ignoreCase: true, out var day)
            || !Enum.IsDefined(day)
            || !TimeOnly.TryParseExact(timeText, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        return new UsageWeekReset(day, time);
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return null;
    }

    private static string? StringOf(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>What is in force: the list, and the id the person chose, which may name
    /// nothing — then the first entry cuts the weeks.</summary>
    private sealed record State(IReadOnlyList<NamedUsageReset> Resets, string? SelectedId)
    {
        public static readonly State Empty = new([], null);

        public NamedUsageReset? Selected =>
            Resets.FirstOrDefault(reset => reset.Id == SelectedId) ?? (Resets.Count > 0 ? Resets[0] : null);
    }

    private sealed class ResetsFileDto
    {
        public List<ResetDto> Resets { get; init; } = [];

        public string? Selected { get; init; }
    }

    private sealed class ResetDto
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Day { get; init; } = string.Empty;

        public string Time { get; init; } = string.Empty;
    }
}
