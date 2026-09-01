using System.Text.Json;

using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the refresh choices in a JSON file next to the app's other per-user
/// settings.
/// <para>
/// Its own file rather than a third section of <c>settings.json</c>, for the same
/// reason the feature choices have one: that file is the pointer to the
/// workspace, and a pointer that has to be rewritten to change a poll interval is
/// a pointer that gets rewritten far more often than it is moved.
/// </para>
/// </summary>
public sealed class BacklogRefreshSettingsStore : IBacklogRefreshSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public BacklogRefreshSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "refresh.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test — or
    /// the web harness, which scopes its settings to its content root — a store
    /// that does not fight over the real per-user file.</summary>
    public BacklogRefreshSettingsStore(string path)
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

    public BacklogRefreshSettings Current { get; private set; }

    public string SettingsPath => _path;

    public string? SetPollingEnabled(bool enabled)
    {
        if (enabled == Current.PollingEnabled) return null;

        return Save(Current with { PollingEnabled = enabled });
    }

    public string? SetPollingIntervalSeconds(int seconds)
    {
        if (seconds < BacklogRefreshSettings.MinimumPollingIntervalSeconds)
        {
            return $"Check at least {BacklogRefreshSettings.MinimumPollingIntervalSeconds} second apart.";
        }

        if (seconds == Current.PollingIntervalSeconds) return null;

        return Save(Current with { PollingIntervalSeconds = seconds });
    }

    private string? Save(BacklogRefreshSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new RefreshSettingsDto
            {
                PollingEnabled = Current.PollingEnabled,
                PollingIntervalSeconds = Current.PollingIntervalSeconds
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the refresh choice couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private BacklogRefreshSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new BacklogRefreshSettings();

            var dto = JsonSerializer.Deserialize<RefreshSettingsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new BacklogRefreshSettings();

            return Normalize(new BacklogRefreshSettings
            {
                PollingEnabled = dto.PollingEnabled,
                PollingIntervalSeconds = dto.PollingIntervalSeconds
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable setting must never stop the app from
            // opening — fall back to the defaults.
            return new BacklogRefreshSettings();
        }
    }

    /// <summary>The floor applies to whatever the file says as well as to what
    /// the screen asks for: a hand-edited zero is a value nothing should be asked
    /// to run at, not a choice to honour.</summary>
    private static BacklogRefreshSettings Normalize(BacklogRefreshSettings settings) =>
        settings.PollingIntervalSeconds >= BacklogRefreshSettings.MinimumPollingIntervalSeconds
            ? settings
            : settings with { PollingIntervalSeconds = BacklogRefreshSettings.MinimumPollingIntervalSeconds };

    private sealed class RefreshSettingsDto
    {
        public bool PollingEnabled { get; init; } = true;

        public int PollingIntervalSeconds { get; init; } = BacklogRefreshSettings.DefaultPollingIntervalSeconds;
    }
}
