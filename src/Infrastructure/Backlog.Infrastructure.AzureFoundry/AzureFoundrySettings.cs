using System.Security.Cryptography;
using System.Text.Json;

namespace Backlog.Infrastructure.AzureFoundry;

public sealed class AzureFoundrySettings
{
    public string? Endpoint { get; init; }

    public string? Deployment { get; init; }

    public string? ApiKey { get; init; }

    public string ApiVersion { get; init; } = AzureFoundrySettingsStore.DefaultApiVersion;

    /// <summary>
    /// The Azure Resource Manager scope whose spend the dashboard reads — the
    /// resource ID of the Foundry account, or a resource group or subscription
    /// around it. Separate from the chat endpoint because the two are different
    /// addresses of the same thing: the endpoint is where completions go, the
    /// scope is what the bill is written against, and nothing derives one from
    /// the other.
    /// </summary>
    public string? CostScope { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Deployment)
        && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Whether there is a scope to read spend for. Independent of
    /// <see cref="IsConfigured"/>: reading the bill needs no chat deployment,
    /// and asking a deployment needs no bill.</summary>
    public bool IsCostConfigured => !string.IsNullOrWhiteSpace(CostScope);
}

/// <summary>
/// Per-user Azure Foundry settings. The API key deliberately lives outside the
/// backlog folder so synced or committed content never carries credentials.
/// <para>
/// The key is a bearer secret that works from anywhere, so it is not written in
/// the clear either (local ADR 0030): <c>apiKey</c> holds a DPAPI envelope sealed
/// under the signed-in Windows user, and every other field stays legible. A key an
/// earlier release wrote in plaintext is read as it is and sealed straight away;
/// one this user cannot open reads as no key, which the Settings page shows as an
/// empty key field to fill in again.
/// </para>
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
            ApiVersion = Clean(apiVersion) ?? DefaultApiVersion,
            CostScope = Current.CostScope
        });

    public string? SetApiKey(string? apiKey) =>
        Save(new AzureFoundrySettings
        {
            Endpoint = Current.Endpoint,
            Deployment = Current.Deployment,
            ApiKey = Clean(apiKey),
            ApiVersion = Current.ApiVersion,
            CostScope = Current.CostScope
        });

    public string? SetCostScope(string? costScope) =>
        Save(new AzureFoundrySettings
        {
            Endpoint = Current.Endpoint,
            Deployment = Current.Deployment,
            ApiKey = Current.ApiKey,
            ApiVersion = Current.ApiVersion,
            CostScope = Clean(costScope)
        });

    public string? ClearApiKey() => SetApiKey(null);

    private string? Save(AzureFoundrySettings settings)
    {
        Current = Normalize(settings);

        var error = Write(Current) ? null : "Changed, but the Azure Foundry settings couldn't be saved for next time.";

        Changed?.Invoke();
        return error;
    }

    /// <summary>Writes the settings with the key sealed. False when the file could
    /// not be written.</summary>
    private bool Write(AzureFoundrySettings settings)
    {
        try
        {
            var stored = new AzureFoundrySettings
            {
                Endpoint = settings.Endpoint,
                Deployment = settings.Deployment,
                ApiKey = SecretEnvelope.Seal(settings.ApiKey),
                ApiVersion = settings.ApiVersion,
                CostScope = settings.CostScope
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(stored, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    private AzureFoundrySettings Read()
    {
        AzureFoundrySettings settings;
        bool wasPlaintext;

        try
        {
            if (!File.Exists(_path)) return new AzureFoundrySettings();

            var stored = JsonSerializer.Deserialize<AzureFoundrySettings>(File.ReadAllText(_path), JsonOptions)
                ?? new AzureFoundrySettings();

            settings = Normalize(new AzureFoundrySettings
            {
                Endpoint = stored.Endpoint,
                Deployment = stored.Deployment,
                ApiKey = SecretEnvelope.Open(stored.ApiKey, out wasPlaintext),
                ApiVersion = stored.ApiVersion,
                CostScope = stored.CostScope
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AzureFoundrySettings();
        }

        // A key an earlier release left in the clear is sealed now rather than at
        // the next save, which may be months away. A write that fails leaves it as
        // it was, to be tried again on the next start.
        if (wasPlaintext) _ = Write(settings);

        return settings;
    }

    private static AzureFoundrySettings Normalize(AzureFoundrySettings settings) => new()
    {
        Endpoint = Clean(settings.Endpoint)?.TrimEnd('/'),
        Deployment = Clean(settings.Deployment),
        ApiKey = Clean(settings.ApiKey),
        ApiVersion = Clean(settings.ApiVersion) ?? DefaultApiVersion,
        // Trimmed of slashes at both ends so the request path is built from one
        // shape whether the ID was pasted from the portal or from a Bicep output.
        CostScope = Clean(settings.CostScope)?.Trim('/') is { Length: > 0 } scope ? "/" + scope : null
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
