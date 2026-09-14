using System.Text.Json;

namespace Backlog.Infrastructure.Sync;

/// <summary>What the person told this machine about where the sync service is.</summary>
public sealed class SyncServiceSettings
{
    /// <summary>The service's base URL, or <see langword="null"/> when nothing
    /// has been entered and the head falls back to its environment.</summary>
    public string? ServiceUrl { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceUrl);
}

/// <summary>
/// Per-user, per-installation sync service settings.
/// <para>
/// The same shape as the Azure Foundry store and kept in the same place — beside
/// the device credential under LocalApplicationData, never the backlog folder.
/// The URL is not a secret, but it describes <em>this machine's</em> route to the
/// service, and a workspace root can be pointed at a folder some other product
/// syncs; a second device adopting the first one's URL by way of the workspace
/// is the kind of silent cross-talk ADR 0005 exists to remove.
/// </para>
/// <para>
/// A value that is not an absolute http(s) URL is refused rather than stored:
/// the value goes straight into <see cref="HttpClient.BaseAddress"/>, and a bad
/// one there fails every request on the machine with an exception that names
/// nothing the person typed.
/// </para>
/// </summary>
public sealed class SyncServiceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public SyncServiceSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "sync-service.json"))
    {
    }

    public SyncServiceSettingsStore(string path)
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

    public SyncServiceSettings Current { get; private set; }

    public string SettingsPath => _path;

    /// <summary>
    /// Stores <paramref name="serviceUrl"/>, or clears the setting when it is
    /// blank. Returns a sentence for the person when the value was refused or
    /// could not be written, and <see langword="null"/> when it was saved.
    /// </summary>
    public string? SetServiceUrl(string? serviceUrl)
    {
        var cleaned = Clean(serviceUrl);
        if (cleaned is not null && !IsAbsoluteHttpUrl(cleaned))
        {
            return "The sync service URL has to be a full http:// or https:// address.";
        }

        return Save(new SyncServiceSettings { ServiceUrl = cleaned });
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private string? Save(SyncServiceSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the sync service settings couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private SyncServiceSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new SyncServiceSettings();

            var settings = JsonSerializer.Deserialize<SyncServiceSettings>(File.ReadAllText(_path), JsonOptions);
            return Normalize(settings ?? new SyncServiceSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SyncServiceSettings();
        }
    }

    // A stored value that fails the same check the setter applies is dropped on
    // read, so a hand-edited file cannot put a base address in place that the
    // setter would have refused.
    private static SyncServiceSettings Normalize(SyncServiceSettings settings)
    {
        var url = Clean(settings.ServiceUrl)?.TrimEnd('/');
        return new SyncServiceSettings { ServiceUrl = url is not null && IsAbsoluteHttpUrl(url) ? url : null };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
