using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// What Backlog needs to read an organization's Claude usage: an Admin API key,
/// and optionally the workspace to narrow reports to.
/// </summary>
public sealed record ClaudeSettings
{
    /// <summary>The Admin API key (<c>sk-ant-admin…</c>). A regular inference key
    /// cannot read usage reports, however valid it is.</summary>
    public string? AdminApiKey { get; init; }

    /// <summary>Optional workspace filter, so a single workspace's usage can be
    /// reported instead of the whole organization.</summary>
    public string? WorkspaceId { get; init; }

    public string ApiVersion { get; init; } = ClaudeSettingsStore.DefaultApiVersion;

    /// <summary>
    /// True when a key is present. Anthropic only issues admin keys to
    /// organizations, so a configured key is also the practical signal that an
    /// organization exists behind it.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AdminApiKey);

    /// <summary>
    /// True when the key at least looks like an Admin API key. Configuring a
    /// regular <c>sk-ant-api…</c> key is a common mistake that fails with an
    /// opaque 401, so it is worth catching before the request leaves.
    /// </summary>
    [JsonIgnore]
    public bool LooksLikeAdminKey =>
        !string.IsNullOrWhiteSpace(AdminApiKey)
        && AdminApiKey.StartsWith("sk-ant-admin", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads and writes <see cref="ClaudeSettings"/> in the per-user application
/// folder. The admin key deliberately lives outside the backlog folder so synced
/// or committed content never carries credentials.
/// </summary>
public sealed class ClaudeSettingsStore
{
    public const string DefaultApiVersion = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public ClaudeSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "claude.json"))
    {
    }

    public ClaudeSettingsStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    /// <summary>Raised after the configuration changes, so open views and any
    /// cached connection can react.</summary>
    public event Action? Changed;

    public ClaudeSettings Current { get; private set; }

    /// <summary>Where the file lives, shown in Settings so it can be found (and
    /// so it is obvious the key is not in the backlog folder).</summary>
    public string SettingsPath => _path;

    public string? SetAdminApiKey(string? adminApiKey) =>
        Save(Current with { AdminApiKey = adminApiKey });

    public string? SetWorkspaceId(string? workspaceId) =>
        Save(Current with { WorkspaceId = workspaceId });

    public string? ClearAdminApiKey() => SetAdminApiKey(null);

    private string? Save(ClaudeSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the Claude settings couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private ClaudeSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new ClaudeSettings();

            var settings = JsonSerializer.Deserialize<ClaudeSettings>(File.ReadAllText(_path), JsonOptions);
            return Normalize(settings ?? new ClaudeSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file must never stop the app from opening.
            return new ClaudeSettings();
        }
    }

    private static ClaudeSettings Normalize(ClaudeSettings settings) => new()
    {
        AdminApiKey = Clean(settings.AdminApiKey),
        WorkspaceId = Clean(settings.WorkspaceId),
        ApiVersion = Clean(settings.ApiVersion) ?? DefaultApiVersion
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
