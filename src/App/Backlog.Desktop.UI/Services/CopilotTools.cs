using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Backlog.Desktop.UI.Services;

public enum CopilotToolKind
{
    Plugin,
    McpServer
}

public enum CopilotToolAction
{
    Update,
    Enable,
    Disable
}

public sealed record CopilotToolInfo(
    string Key,
    CopilotToolKind Kind,
    string Name,
    string? Source,
    bool ConfiguredEnabled,
    bool Installed,
    string InstalledVersion,
    string AvailableVersion,
    string Status)
{
    public bool UpdateAvailable => VersionDiffers(InstalledVersion, AvailableVersion);

    public bool CanUpdate => ConfiguredEnabled && Installed && UpdateAvailable;

    public static bool VersionDiffers(string installedVersion, string availableVersion)
    {
        var installed = NormalizeVersion(installedVersion);
        var available = NormalizeVersion(availableVersion);

        return installed is not null
            && available is not null
            && !string.Equals(installed, available, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("not installed", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.StartsWith('v') ? trimmed[1..] : trimmed;
    }
}

public sealed record CopilotToolCatalog(IReadOnlyList<CopilotToolInfo> Tools, string Message);

public sealed record CopilotToolActionResult(bool Succeeded, string Message)
{
    public static CopilotToolActionResult Ok(string message) => new(true, message);

    public static CopilotToolActionResult Failed(string message) => new(false, message);
}

public sealed record CopilotToolConfigurationPaths(string CatalogPath, string PcConfigPath)
{
    private const string DefaultRepositoryRoot = "%USERPROFILE%\\.copilot\\repos\\Backlog";
    private const string ToolFolderName = ".tools";
    private const string CatalogFileName = "copilot-tools.json";

    public static CopilotToolConfigurationPaths CreateDefault(string? machineName = null, string? startPath = null, string? storageRootDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(storageRootDirectory))
        {
            return FromStorageRoot(storageRootDirectory, machineName);
        }

        var localCatalogRoot = FindCatalogRoot(startPath ?? AppContext.BaseDirectory)
            ?? FindCatalogRoot(Environment.CurrentDirectory);

        return localCatalogRoot is null
            ? FromStorageRoot(DefaultRepositoryRoot, machineName)
            : FromStorageRoot(localCatalogRoot, machineName);
    }

    public static CopilotToolConfigurationPaths FromRepositoryRoot(string repositoryRoot, string? machineName = null)
        => FromStorageRoot(repositoryRoot, machineName);

    public static CopilotToolConfigurationPaths FromStorageRoot(string storageRoot, string? machineName = null)
    {
        var expandedRoot = Environment.ExpandEnvironmentVariables(storageRoot);
        var pcName = NormalizeMachineName(machineName ?? Environment.MachineName);

        return new CopilotToolConfigurationPaths(
            Path.Combine(expandedRoot, ToolFolderName, CatalogFileName),
            Path.Combine(expandedRoot, ToolFolderName, pcName, CatalogFileName));
    }

    public static CopilotToolConfigurationPaths FromCatalogPath(string catalogPath, string? machineName = null)
    {
        var expandedCatalog = Environment.ExpandEnvironmentVariables(catalogPath);
        var toolRoot = Path.GetDirectoryName(expandedCatalog) ?? Environment.CurrentDirectory;
        var pcName = NormalizeMachineName(machineName ?? Environment.MachineName);

        return new CopilotToolConfigurationPaths(
            expandedCatalog,
            Path.Combine(toolRoot, pcName, CatalogFileName));
    }

    private static string? FindCatalogRoot(string startPath)
    {
        var directory = Directory.Exists(startPath)
            ? new DirectoryInfo(startPath)
            : new FileInfo(startPath).Directory;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ToolFolderName, CatalogFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string NormalizeMachineName(string machineName)
    {
        var normalized = new string(machineName
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "unknown-pc" : normalized;
    }
}

public sealed record CopilotToolConfigurationDocument(JsonNode Root, bool PcConfigExists, string CatalogPath, string PcConfigPath);

public static class CopilotToolConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<CopilotToolConfigurationDocument> ReadAsync(CopilotToolConfigurationPaths paths, CancellationToken ct = default)
    {
        await using var catalogStream = File.OpenRead(paths.CatalogPath);
        var root = await JsonNode.ParseAsync(catalogStream, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tool catalog is empty.");

        if (!File.Exists(paths.PcConfigPath))
        {
            return new CopilotToolConfigurationDocument(root, false, paths.CatalogPath, paths.PcConfigPath);
        }

        await using var pcStream = File.OpenRead(paths.PcConfigPath);
        var pcRoot = await JsonNode.ParseAsync(pcStream, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PC tool config is empty.");

        MergeArray(root, pcRoot, "plugins", "name");
        MergeArray(root, pcRoot, "mcpServers", "packageId");

        return new CopilotToolConfigurationDocument(root, true, paths.CatalogPath, paths.PcConfigPath);
    }

    public static async Task WriteEnabledOverrideAsync(CopilotToolConfigurationPaths paths, string key, bool enabled, CancellationToken ct = default)
    {
        var root = await ReadPcConfigOrEmptyAsync(paths.PcConfigPath, ct).ConfigureAwait(false);
        var (arrayName, idName, idValue) = ParseKey(key);
        var array = GetOrCreateArray(root, arrayName);
        var tool = FindObject(array, idName, idValue);

        if (tool is null)
        {
            tool = new JsonObject { [idName] = idValue };
            array.Add(tool);
        }

        tool["enabled"] = enabled;

        Directory.CreateDirectory(Path.GetDirectoryName(paths.PcConfigPath) ?? Environment.CurrentDirectory);
        await using var stream = File.Create(paths.PcConfigPath);
        await JsonSerializer.SerializeAsync(stream, root, JsonOptions, ct).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ReadPcConfigOrEmptyAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))?.AsObject()
            ?? throw new InvalidOperationException("PC tool config is empty.");
    }

    private static void MergeArray(JsonNode root, JsonNode pcRoot, string arrayName, string idName)
    {
        var catalogArray = root[arrayName]?.AsArray();
        var pcArray = pcRoot[arrayName]?.AsArray();
        if (catalogArray is null || pcArray is null)
        {
            return;
        }

        foreach (var pcNode in pcArray)
        {
            if (pcNode is not JsonObject pcObject || GetString(pcObject, idName) is not { Length: > 0 } idValue)
            {
                continue;
            }

            var catalogObject = FindObject(catalogArray, idName, idValue);
            if (catalogObject is null)
            {
                continue;
            }

            foreach (var property in pcObject)
            {
                catalogObject[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static JsonArray GetOrCreateArray(JsonObject root, string arrayName)
    {
        if (root[arrayName] is JsonArray existing)
        {
            return existing;
        }

        var array = new JsonArray();
        root[arrayName] = array;
        return array;
    }

    private static JsonObject? FindObject(JsonArray array, string idName, string idValue) =>
        array.OfType<JsonObject>().FirstOrDefault(node => GetString(node, idName).Equals(idValue, StringComparison.OrdinalIgnoreCase));

    private static string GetString(JsonObject node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static (string ArrayName, string IdName, string IdValue) ParseKey(string key)
    {
        if (key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase))
        {
            return ("plugins", "name", key["plugin:".Length..]);
        }

        if (key.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            return ("mcpServers", "packageId", key["mcp:".Length..]);
        }

        throw new ArgumentException("Unknown tool key.", nameof(key));
    }
}

public interface ICopilotToolService
{
    Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default);
}

public sealed class UnsupportedCopilotToolService : ICopilotToolService
{
    private const string Message = "Copilot tool management is only available in the desktop app.";

    public Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(new CopilotToolCatalog([], Message));

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));
}
