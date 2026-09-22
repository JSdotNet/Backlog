using System.Globalization;
using System.Text.Json;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The weekly usage reset as a small JSON file beside the working week, read once and
/// written on every change — <see cref="WorkingHoursSettingsStore"/>'s shape, for the
/// same reason: it is the person's choice, not the workspace's, and it has to be
/// readable before any dashboard part has asked for it.
/// </summary>
public sealed class UsageResetSettingsStore : IUsageResetSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    private readonly string _path;

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

        Current = Read();
    }

    public event Action? Changed;

    public UsageWeekReset? Current { get; private set; }

    public string SettingsPath => _path;

    public string? Set(DayOfWeek day, TimeOnly time)
    {
        if (Current is { } stored && stored.Day == day && stored.Time == time) return null;

        return Save(new UsageWeekReset(day, time));
    }

    public string? Clear() => Current is null ? null : Save(null);

    private string? Save(UsageWeekReset? reset)
    {
        Current = reset;

        string? error = null;

        try
        {
            if (reset is null)
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            else
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(new UsageResetDto
                {
                    Day = reset.Day.ToString(),
                    Time = reset.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
                }, JsonOptions));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the weekly reset couldn't be saved for next time.";
        }

        Changed?.Invoke();

        return error;
    }

    private UsageWeekReset? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var dto = JsonSerializer.Deserialize<UsageResetDto>(File.ReadAllText(_path), JsonOptions);

            if (dto is null
                || !Enum.TryParse<DayOfWeek>(dto.Day, ignoreCase: true, out var day)
                || !TimeOnly.TryParseExact(dto.Time, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return null;
            }

            return new UsageWeekReset(day, time);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed class UsageResetDto
    {
        public string Day { get; init; } = string.Empty;

        public string Time { get; init; } = string.Empty;
    }
}
