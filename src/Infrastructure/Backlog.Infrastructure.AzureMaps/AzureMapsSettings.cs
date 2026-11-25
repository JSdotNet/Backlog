using System.Security.Cryptography;
using System.Text.Json;
using Backlog.SharedKernel.Storage;

namespace Backlog.Infrastructure.AzureMaps;

/// <summary>
/// The Azure Maps account the Events map uses (local ADR 0061 §1, §4): its key, and where and
/// how it is called. The endpoint, the api-versions and the tileset are settings rather than
/// code, so a newer version is a setting change, not a decision.
/// </summary>
public sealed class AzureMapsSettings
{
    /// <summary>The EU geography's endpoint, which keeps requests and their input in the EU.</summary>
    public const string DefaultEndpoint = "https://eu.atlas.microsoft.com";

    /// <summary>Search's Get Geocoding, with the structured address parameters.</summary>
    public const string DefaultGeocodingApiVersion = "2026-01-01";

    /// <summary>Render's Get Map Tile.</summary>
    public const string DefaultTileApiVersion = "2024-04-01";

    /// <summary>Raster road tiles in the main style.</summary>
    public const string DefaultTilesetId = "microsoft.base.road";

    /// <summary>The account's shared key. Null while none is set, which is the person not
    /// having consented: nothing is sent and the map shows its missing-key note.</summary>
    public string? ApiKey { get; init; }

    public string Endpoint { get; init; } = DefaultEndpoint;

    public string GeocodingApiVersion { get; init; } = DefaultGeocodingApiVersion;

    public string TileApiVersion { get; init; } = DefaultTileApiVersion;

    public string TilesetId { get; init; } = DefaultTilesetId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Per-user Azure Maps settings, beside the Foundry ones and kept the same way (local ADR 0030):
/// <c>apiKey</c> holds a DPAPI envelope sealed under the signed-in Windows user and every other
/// field stays legible. A key written in plaintext is read as it is and sealed straight away; one
/// this user cannot open reads as no key, which Settings shows as an empty key field to fill in
/// again.
/// </summary>
// Not on JsonFileStore<T>: the key is sealed with DPAPI on write and opened on read, and a
// CryptographicException is a refused write too — as AzureFoundrySettingsStore.
public sealed class AzureMapsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public AzureMapsSettingsStore()
        : this(AppDataPaths.Shared("azure-maps.json"))
    {
    }

    public AzureMapsSettingsStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    /// <summary>Raised after every save, the key's included.</summary>
    public event Action? Changed;

    public AzureMapsSettings Current { get; private set; }

    public string SettingsPath => _path;

    /// <summary>Sets the key; blank clears it. Null when it was saved, else the message to show:
    /// the key is still in use for this session, but not kept for the next.</summary>
    public string? SetApiKey(string? apiKey) =>
        Save(new AzureMapsSettings
        {
            ApiKey = Clean(apiKey),
            Endpoint = Current.Endpoint,
            GeocodingApiVersion = Current.GeocodingApiVersion,
            TileApiVersion = Current.TileApiVersion,
            TilesetId = Current.TilesetId
        });

    public string? ClearApiKey() => SetApiKey(null);

    private string? Save(AzureMapsSettings settings)
    {
        Current = Normalize(settings);

        var error = Write(Current) ? null : "Changed, but the Azure Maps settings couldn't be saved for next time.";

        Changed?.Invoke();
        return error;
    }

    /// <summary>Writes the settings with the key sealed. False when the file could not be
    /// written.</summary>
    private bool Write(AzureMapsSettings settings)
    {
        try
        {
            var stored = new AzureMapsSettings
            {
                ApiKey = SecretEnvelope.Seal(settings.ApiKey),
                Endpoint = settings.Endpoint,
                GeocodingApiVersion = settings.GeocodingApiVersion,
                TileApiVersion = settings.TileApiVersion,
                TilesetId = settings.TilesetId
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(stored, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    private AzureMapsSettings Read()
    {
        AzureMapsSettings settings;
        bool wasPlaintext;

        try
        {
            if (!File.Exists(_path)) return new AzureMapsSettings();

            var stored = JsonSerializer.Deserialize<AzureMapsSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new AzureMapsSettings();

            settings = Normalize(new AzureMapsSettings
            {
                ApiKey = SecretEnvelope.Open(stored.ApiKey, out wasPlaintext),
                Endpoint = stored.Endpoint,
                GeocodingApiVersion = stored.GeocodingApiVersion,
                TileApiVersion = stored.TileApiVersion,
                TilesetId = stored.TilesetId
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AzureMapsSettings();
        }

        // A key left in the clear is sealed now rather than at the next save. A write that fails
        // leaves it as it was, to be tried again on the next start.
        if (wasPlaintext) _ = Write(settings);

        return settings;
    }

    private static AzureMapsSettings Normalize(AzureMapsSettings settings) => new()
    {
        ApiKey = Clean(settings.ApiKey),
        Endpoint = Clean(settings.Endpoint?.Trim().TrimEnd('/')) ?? AzureMapsSettings.DefaultEndpoint,
        GeocodingApiVersion = Clean(settings.GeocodingApiVersion) ?? AzureMapsSettings.DefaultGeocodingApiVersion,
        TileApiVersion = Clean(settings.TileApiVersion) ?? AzureMapsSettings.DefaultTileApiVersion,
        TilesetId = Clean(settings.TilesetId) ?? AzureMapsSettings.DefaultTilesetId
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
