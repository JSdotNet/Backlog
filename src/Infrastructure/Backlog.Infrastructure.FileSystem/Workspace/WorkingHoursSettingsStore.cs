using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the reader's working week in a JSON file next to the app's other
/// per-user settings.
/// <para>
/// Its own file rather than a section of <c>settings.json</c>, for the reason
/// the refresh and feature choices each have one: that file is the pointer to
/// the workspace, and a pointer that has to be rewritten to move Friday's
/// finishing time is a pointer that gets rewritten far more often than it is
/// moved.
/// </para>
/// <para>
/// Times are written as <c>HH:mm</c>, invariant, rather than as
/// <see cref="TimeOnly"/>'s round-trip form. This file is meant to be read and
/// hand-edited, <c>09:00</c> is what a person writes, and it is the same wire
/// format the <c>time</c> input on the settings screen takes and returns — so
/// the file, the field and the reader all say the same thing. The round-trip
/// form (<c>09:00:00.0000000</c>) would have carried a precision nobody chose
/// and cost a translation at both ends.
/// </para>
/// <para>
/// This file is the device's copy of a week that is one per person (local ADR
/// 0019, §3). The roadmap's pace document carries the same shape under
/// <c>workingWeek</c> (<see cref="ToJson"/>, <see cref="FromJson"/>), and a pace
/// document pulled from another device replaces this copy through
/// <see cref="Replace"/>.
/// </para>
/// </summary>
public sealed class WorkingHoursSettingsStore : IWorkingHoursSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>What is written. Seconds are accepted on the way in because a
    /// browser whose step allows them will send them, and refusing a time for
    /// being more precise than asked would be a rejection nobody could act
    /// on.</summary>
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    private readonly string _path;

    public WorkingHoursSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "working-hours.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test — or
    /// the web harness, which scopes its settings to its content root — a store
    /// that does not fight over the real per-user file.</summary>
    public WorkingHoursSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public WorkingHours Current { get; private set; }

    public string SettingsPath => _path;

    public string? SetDay(DayOfWeek day, bool working, TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            return $"End the day after it starts — {Format(start)} to {Format(end)} covers nothing.";
        }

        var stored = Current.On(day);
        if (stored.Working == working && stored.Start == start && stored.End == end) return null;

        return Save(Current with
        {
            Days = [.. WorkingHours.Week.Select(other =>
                other == day ? new WorkingDay(day, working, start, end) : Current.On(other))]
        });
    }

    /// <summary>
    /// Writes the default week rather than deleting the file.
    /// <para>
    /// Deleting it would read the same on the next start — a missing file is the
    /// default — but it would also silently discard whatever a hand-edit had put
    /// there, and this is the one button on the screen whose whole promise is
    /// that the reader can see what it did. A file saying Monday to Friday is
    /// that promise kept.
    /// </para>
    /// </summary>
    public string? ResetToDefault() => Save(WorkingHours.Default);

    /// <summary>
    /// Replaces the whole week with one that arrived from another device, inside the
    /// pace document (local ADR 0019, §3). Unlike <see cref="SetDay"/> it refuses no day:
    /// the week was set on the other device, and a day ending before it starts already
    /// reads as not worked. Raises <see cref="Changed"/> only when the week differs, so a
    /// pull of the week this device already holds redraws nothing.
    /// </summary>
    public string? Replace(WorkingHours hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var replacing = Normalize(hours);
        if (SameWeek(replacing, Current)) return null;

        return Save(replacing);
    }

    /// <summary>Whether two weeks say the same of every day.</summary>
    public static bool SameWeek(WorkingHours first, WorkingHours second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return WorkingHours.Week.All(day => first.On(day) == second.On(day));
    }

    /// <summary>The week in the shape this file holds — <c>{ "days": [ … ] }</c>, times
    /// as <c>HH:mm</c> — for a document that carries it.</summary>
    public static JsonNode ToJson(WorkingHours hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        return JsonSerializer.SerializeToNode(ToDto(Normalize(hours)), JsonOptions)!;
    }

    /// <summary>A week in this file's shape, or <c>null</c> for anything that is not
    /// one. Read as forgivingly as the file: an unreadable day is filled from the
    /// default.</summary>
    public static WorkingHours? FromJson(JsonNode? node)
    {
        if (node is not JsonObject) return null;

        try
        {
            var dto = node.Deserialize<WorkingHoursDto>(JsonOptions);
            return dto is null ? null : FromDto(dto);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static WorkingHoursDto ToDto(WorkingHours hours) => new()
    {
        Days = [.. hours.Days.Select(day => new WorkingDayDto
        {
            Day = day.Day.ToString(),
            Working = day.Working,
            Start = Format(day.Start),
            End = Format(day.End)
        })]
    };

    private static WorkingHours FromDto(WorkingHoursDto dto) =>
        Normalize(new WorkingHours { Days = [.. (dto.Days ?? []).Select(ReadDay).OfType<WorkingDay>()] });

    private string? Save(WorkingHours hours)
    {
        Current = Normalize(hours);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(ToDto(Current), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the working week couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private WorkingHours Read()
    {
        try
        {
            if (!File.Exists(_path)) return WorkingHours.Default;

            var dto = JsonSerializer.Deserialize<WorkingHoursDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return WorkingHours.Default;

            return FromDto(dto);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable setting must never stop the app from
            // opening — fall back to the default week.
            return WorkingHours.Default;
        }
    }

    /// <summary>A line nobody can read is dropped rather than guessed at.
    /// <see cref="Normalize"/> then fills the day back in from the default,
    /// which is the same answer a missing line gets and for the same reason:
    /// nothing legible was said, so nothing is claimed on the reader's
    /// behalf.</summary>
    private static WorkingDay? ReadDay(WorkingDayDto? dto)
    {
        if (dto is null) return null;
        if (!Enum.TryParse<DayOfWeek>(dto.Day, ignoreCase: true, out var day)) return null;
        if (!TryParse(dto.Start, out var start) || !TryParse(dto.End, out var end)) return null;

        return new WorkingDay(day, dto.Working, start, end);
    }

    /// <summary>
    /// Makes the week whole and puts it in reading order: exactly one entry per
    /// day, Monday first, anything absent taken from the default and any
    /// duplicate resolved by keeping the first.
    /// <para>
    /// A range that ends at or before it starts is deliberately left alone. The
    /// setter refuses one, so it can only arrive by hand-edit, and
    /// <see cref="WorkingHours.Covers"/> already says what such a day means:
    /// nothing is marked. Repairing it here would have to invent an end nobody
    /// chose, and a grid quietly showing hours the file does not claim is worse
    /// than one showing none.
    /// </para>
    /// </summary>
    private static WorkingHours Normalize(WorkingHours hours) =>
        hours with { Days = [.. WorkingHours.Week.Select(hours.On)] };

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private sealed class WorkingHoursDto
    {
        public List<WorkingDayDto> Days { get; init; } = [];
    }

    private sealed class WorkingDayDto
    {
        public string Day { get; init; } = string.Empty;

        public bool Working { get; init; }

        public string Start { get; init; } = string.Empty;

        public string End { get; init; } = string.Empty;
    }
}
