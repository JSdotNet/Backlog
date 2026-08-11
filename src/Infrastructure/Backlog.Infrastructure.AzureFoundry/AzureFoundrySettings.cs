using System.Text.Json;

namespace Backlog.Infrastructure.AzureFoundry;

public sealed class AzureFoundrySettings
{
    public string? Endpoint { get; init; }

    public string? Deployment { get; init; }

    public string? ApiKey { get; init; }

    public string ApiVersion { get; init; } = AzureFoundrySettingsStore.DefaultApiVersion;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Deployment)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Per-user Azure Foundry settings. The API key deliberately lives outside the
/// backlog folder so synced or committed content never carries credentials.
/// </summary>
public sealed class AzureFoundrySettingsStore
{
    public const string DefaultApiVersion = "2024-10-21";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public AzureFoundrySettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "azure-foundry.json"))
    {
    }

    public AzureFoundrySettingsStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public AzureFoundrySettings Current { get; private set; }

    public string SettingsPath => _path;

    public string? SetConnection(string? endpoint, string? deployment, string? apiKey, string? apiVersion) =>
        Save(new AzureFoundrySettings
        {
            Endpoint = Clean(endpoint),
            Deployment = Clean(deployment),
            ApiKey = Clean(apiKey) ?? Current.ApiKey,
            ApiVersion = Clean(apiVersion) ?? DefaultApiVersion
        });

    public string? SetApiKey(string? apiKey) =>
        Save(new AzureFoundrySettings
        {
            Endpoint = Current.Endpoint,
            Deployment = Current.Deployment,
            ApiKey = Clean(apiKey),
            ApiVersion = Current.ApiVersion
        });

    public string? ClearApiKey() => SetApiKey(null);

    private string? Save(AzureFoundrySettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the Azure Foundry settings couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private AzureFoundrySettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new AzureFoundrySettings();

            var settings = JsonSerializer.Deserialize<AzureFoundrySettings>(File.ReadAllText(_path), JsonOptions);
            return Normalize(settings ?? new AzureFoundrySettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AzureFoundrySettings();
        }
    }

    private static AzureFoundrySettings Normalize(AzureFoundrySettings settings) => new()
    {
        Endpoint = Clean(settings.Endpoint)?.TrimEnd('/'),
        Deployment = Clean(settings.Deployment),
        ApiKey = Clean(settings.ApiKey),
        ApiVersion = Clean(settings.ApiVersion) ?? DefaultApiVersion
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
