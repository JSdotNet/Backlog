using System.Text.Json;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the feature choices in a JSON file next to the app's other per-user
/// settings.
/// <para>
/// The catalog is handed in rather than declared here on purpose. What features
/// exist, what they are called and how they are described is product copy that
/// belongs to the screen that renders it; what this class knows is how to read
/// and write a set of keys. Handing the catalog in is also what lets a test
/// state its own two features instead of the eleven the app ships.
/// </para>
/// </summary>
public sealed class AppFeatureSettingsStore : IAppFeatureSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IReadOnlyList<AppFeatureDefinition> _features;
    private readonly string _path;

    public AppFeatureSettingsStore(IReadOnlyList<AppFeatureDefinition> features)
        : this(
            features,
            Path.Combine(WorkspaceSettingsStore.DefaultAppDataDirectory, "features.json"))
    {
    }

    public AppFeatureSettingsStore(IReadOnlyList<AppFeatureDefinition> features, string path)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _features = features;
        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public AppFeatureSettings Current { get; private set; }

    public string SettingsPath => _path;

    public bool IsEnabled(string key)
    {
        var feature = Find(key) ?? throw new ArgumentException($"Unknown feature '{key}'.", nameof(key));

        return feature.EnabledByDefault
            ? !Current.DisabledFeatures.Contains(feature.Key)
            : Current.EnabledFeatures.Contains(feature.Key);
    }

    public string? SetEnabled(string key, bool enabled)
    {
        var feature = Find(key);
        if (feature is null)
        {
            return $"Unknown feature '{key}'.";
        }

        var disabled = new HashSet<string>(Current.DisabledFeatures, StringComparer.OrdinalIgnoreCase);
        var explicitlyEnabled = new HashSet<string>(Current.EnabledFeatures, StringComparer.OrdinalIgnoreCase);

        if (feature.EnabledByDefault)
        {
            if (enabled) disabled.Remove(feature.Key);
            else disabled.Add(feature.Key);
        }
        else
        {
            if (enabled) explicitlyEnabled.Add(feature.Key);
            else explicitlyEnabled.Remove(feature.Key);
        }

        return Save(new AppFeatureSettings
        {
            DisabledFeatures = disabled,
            EnabledFeatures = explicitlyEnabled
        });
    }

    private AppFeatureDefinition? Find(string key) =>
        _features.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    private string? Save(AppFeatureSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            var dto = new FeatureSettingsDto
            {
                DisabledFeatures = [.. Current.DisabledFeatures.Order(StringComparer.OrdinalIgnoreCase)],
                EnabledFeatures = [.. Current.EnabledFeatures.Order(StringComparer.OrdinalIgnoreCase)]
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the feature choice couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private AppFeatureSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new AppFeatureSettings();

            var dto = JsonSerializer.Deserialize<FeatureSettingsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new AppFeatureSettings();

            var disabled = new HashSet<string>(dto.DisabledFeatures, StringComparer.OrdinalIgnoreCase);
            var enabled = new HashSet<string>(dto.EnabledFeatures, StringComparer.OrdinalIgnoreCase);

            return Normalize(new AppFeatureSettings
            {
                DisabledFeatures = disabled,
                EnabledFeatures = enabled
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppFeatureSettings();
        }
    }

    private AppFeatureSettings Normalize(AppFeatureSettings settings)
    {
        // A key the catalog no longer defines — a retired flag — matches neither
        // set and is dropped here, so the next save leaves it out of the file.
        var optOut = _features.Where(f => f.EnabledByDefault).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var optIn = _features.Where(f => !f.EnabledByDefault).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AppFeatureSettings
        {
            DisabledFeatures = [.. settings.DisabledFeatures.Where(optOut.Contains)],
            EnabledFeatures = [.. settings.EnabledFeatures.Where(optIn.Contains)]
        };
    }

    private sealed class FeatureSettingsDto
    {
        public string[] DisabledFeatures { get; init; } = [];

        public string[] EnabledFeatures { get; init; } = [];
    }
}
