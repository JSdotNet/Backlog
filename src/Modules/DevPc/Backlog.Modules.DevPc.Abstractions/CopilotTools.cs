using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Backlog.Modules.DevPc.Abstractions;

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

/// <summary>A tool a caller wants written into the catalog, before the catalog
/// has anything to say about it.
/// <para>
/// <paramref name="Id"/> is whichever property identifies the entry — a plugin's
/// <c>name</c>, an MCP server's <c>packageId</c> — so one draft covers both
/// kinds without the caller choosing a property name.
/// </para>
/// <para>
/// <paramref name="PluginKind"/> is the catalog's own <c>kind</c> string,
/// <c>repository-skills</c> and its siblings, and is deliberately not the
/// <see cref="CopilotToolKind"/> enum beside it. The enum says which array an
/// entry lives in; this says what the host does with it once it is there, and
/// the host reads it as free text it may not recognise.
/// </para></summary>
public sealed record CopilotToolDraft(
    CopilotToolKind Kind,
    string Id,
    string? Source = null,
    string? DisplayName = null,
    string? PluginKind = null);

/// <summary>What the tools surface has to draw.
/// <para>
/// <paramref name="CatalogExists"/> is what separates "there is no catalog file"
/// from "the catalog is empty". Both used to arrive as an empty
/// <paramref name="Tools"/> list, so the pane drew the same dead end for a
/// machine that needs a catalog created and one that needs a tool added.
/// </para>
/// <para>
/// <paramref name="CanEditCatalog"/> is one coarse flag rather than four: a host
/// that can write the catalog can do all of creating, adding, removing and
/// importing, and one that cannot can do none of them.
/// </para>
/// <para>
/// The three carry defaults so the positional construction every existing caller
/// uses still compiles. A host that really answers this port sets them anyway —
/// the defaults describe the host that cannot.
/// </para></summary>
public sealed record CopilotToolCatalog(
    IReadOnlyList<CopilotToolInfo> Tools,
    string Message,
    bool CatalogExists = false,
    string CatalogPath = "",
    bool CanEditCatalog = false);

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

    /// <summary>
    /// Serialises every catalog write, because two of them interleaved would
    /// read the same file and then write two different whole documents over each
    /// other — the second one silently losing the first one's entry.
    ///
    /// <para>It guards this process only. The catalog is a file two machines can
    /// have open through a sync folder, and nothing here pretends otherwise; the
    /// temp-file swap below is what keeps the loser of that race a valid catalog
    /// rather than half of one.</para>
    /// </summary>
    private static readonly SemaphoreSlim CatalogWriteLock = new(1, 1);

    /// <summary>Whether there is a catalog to read at all. Both hosts ask here
    /// rather than calling <see cref="File.Exists(string)"/> themselves, so the
    /// answer the pane branches on has one definition.</summary>
    public static bool CatalogExists(CopilotToolConfigurationPaths paths) => File.Exists(paths.CatalogPath);

    /// <summary>The key a tool is addressed by, minted in one place so the
    /// prefixes <see cref="ParseKey"/> reads are the prefixes callers write.</summary>
    public static string KeyFor(CopilotToolKind kind, string id) =>
        kind is CopilotToolKind.Plugin ? $"plugin:{id}" : $"mcp:{id}";

    /// <summary>Writes the empty catalog a machine starts from: the two arrays
    /// every reader here expects, indented the way the rest of the file is, so
    /// the first hand edit after this lands in a document that already looks
    /// hand-written.</summary>
    /// <exception cref="InvalidOperationException">A catalog is already there.
    /// Creating over it would discard every entry in it.</exception>
    public static async Task CreateCatalogAsync(CopilotToolConfigurationPaths paths, CancellationToken ct = default)
    {
        await CatalogWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(paths.CatalogPath))
            {
                throw new InvalidOperationException($"A tool catalog already exists at {paths.CatalogPath}.");
            }

            await WriteCatalogAsync(paths.CatalogPath, EmptyCatalog(), ct).ConfigureAwait(false);
        }
        finally
        {
            CatalogWriteLock.Release();
        }
    }

    /// <summary>
    /// Appends one entry to the catalog — never to the per-PC file. The merge in
    /// <see cref="ReadAsync"/> drops a PC entry with no catalog match, so a tool
    /// added to the PC file would be a tool that never appears.
    ///
    /// <para>New entries arrive enabled. Adding a tool is the act of asking for
    /// it; the per-PC override is where "not on this machine" is said.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no catalog yet, the
    /// draft is missing something the entry cannot be written without, or the id
    /// is already taken for that kind.</exception>
    public static async Task AddToCatalogAsync(CopilotToolConfigurationPaths paths, CopilotToolDraft draft, CancellationToken ct = default)
    {
        var id = draft.Id?.Trim() ?? string.Empty;
        var source = draft.Source?.Trim() ?? string.Empty;

        if (id.Length == 0)
        {
            throw new InvalidOperationException(draft.Kind is CopilotToolKind.Plugin
                ? "A plugin needs a name."
                : "An MCP server needs a package id.");
        }

        // A plugin with no source is an entry the host cannot install from, so it
        // is rejected here rather than written and failed against later. An MCP
        // server needs none: its package id is where it comes from.
        if (draft.Kind is CopilotToolKind.Plugin && source.Length == 0)
        {
            throw new InvalidOperationException("A plugin needs a source.");
        }

        await CatalogWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(paths.CatalogPath))
            {
                throw new InvalidOperationException($"There is no tool catalog at {paths.CatalogPath} yet. Create it first.");
            }

            var root = await ReadCatalogAsync(paths.CatalogPath, ct).ConfigureAwait(false);
            var (arrayName, idName, _) = ParseKey(KeyFor(draft.Kind, id));
            var array = GetOrCreateArray(root, arrayName);

            // OrdinalIgnoreCase, matching every other lookup in this file: the
            // catalog is read case-insensitively, so two entries differing only in
            // case would be one tool with two rows and an ambiguous key.
            if (FindObject(array, idName, id) is not null)
            {
                throw new InvalidOperationException($"{id} is already in the catalog.");
            }

            var entry = new JsonObject { [idName] = id };

            if (draft.Kind is CopilotToolKind.Plugin)
            {
                entry["source"] = source;
                if (!string.IsNullOrWhiteSpace(draft.PluginKind))
                {
                    entry["kind"] = draft.PluginKind.Trim();
                }
            }
            else
            {
                // An MCP server is identified by its package id and read out by its
                // name, so a display name is a second property rather than the key.
                if (!string.IsNullOrWhiteSpace(draft.DisplayName))
                {
                    entry["name"] = draft.DisplayName.Trim();
                }

                if (source.Length > 0)
                {
                    entry["source"] = source;
                }
            }

            entry["enabled"] = true;
            array.Add(entry);

            await WriteCatalogAsync(paths.CatalogPath, root, ct).ConfigureAwait(false);
        }
        finally
        {
            CatalogWriteLock.Release();
        }
    }

    /// <summary>Drops one entry from the catalog.</summary>
    /// <exception cref="InvalidOperationException">Nothing in the catalog
    /// answers to that key.</exception>
    public static async Task RemoveFromCatalogAsync(CopilotToolConfigurationPaths paths, string key, CancellationToken ct = default)
    {
        var (arrayName, idName, idValue) = ParseKey(key);

        await CatalogWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(paths.CatalogPath))
            {
                throw new InvalidOperationException($"There is no tool catalog at {paths.CatalogPath}.");
            }

            var root = await ReadCatalogAsync(paths.CatalogPath, ct).ConfigureAwait(false);
            var array = root[arrayName] as JsonArray;
            var entry = array is null ? null : FindObject(array, idName, idValue);

            if (array is null || entry is null)
            {
                throw new InvalidOperationException($"{idValue} is no longer in the catalog.");
            }

            array.Remove(entry);

            await WriteCatalogAsync(paths.CatalogPath, root, ct).ConfigureAwait(false);
        }
        finally
        {
            CatalogWriteLock.Release();
        }
    }

    /// <summary>
    /// Prunes one tool's entry from the per-PC override file.
    ///
    /// <para>Removing a tool and adding it back would otherwise return it
    /// disabled: the override survives the catalog entry, and the merge applies
    /// it again the moment a matching entry reappears. A machine with no
    /// override file has nothing to prune, which is a no-op rather than an
    /// error.</para>
    /// </summary>
    public static async Task RemoveEnabledOverrideAsync(CopilotToolConfigurationPaths paths, string key, CancellationToken ct = default)
    {
        if (!File.Exists(paths.PcConfigPath))
        {
            return;
        }

        var root = await ReadPcConfigOrEmptyAsync(paths.PcConfigPath, ct).ConfigureAwait(false);
        var (arrayName, idName, idValue) = ParseKey(key);

        if (root[arrayName] is not JsonArray array || FindObject(array, idName, idValue) is not { } entry)
        {
            return;
        }

        array.Remove(entry);

        await using var stream = File.Create(paths.PcConfigPath);
        await JsonSerializer.SerializeAsync(stream, root, JsonOptions, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the catalog with the document in <paramref name="json"/>. A
    /// replace and not a merge: an import is somebody saying "this is the
    /// catalog", and a merge would leave entries behind that the file they
    /// handed over does not have.
    ///
    /// <para>The previous catalog is copied to a <c>.bak</c> sidecar first,
    /// because that is the only copy of what a replace discards.</para>
    ///
    /// <para>The per-PC file is left alone. Its stale overrides go inert on their
    /// own — the merge drops a PC entry with no catalog match — and rewriting it
    /// here would throw away the enable state of every tool the import keeps.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The text is not a catalog.
    /// Thrown before the catalog file is opened, so a rejected import leaves it
    /// exactly as it was.</exception>
    public static async Task ImportCatalogAsync(CopilotToolConfigurationPaths paths, string json, CancellationToken ct = default)
    {
        if (!TryReadCatalog(json, out var root, out var error))
        {
            throw new InvalidOperationException(error);
        }

        await CatalogWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(paths.CatalogPath))
            {
                File.Copy(paths.CatalogPath, paths.CatalogPath + ".bak", overwrite: true);
            }

            await WriteCatalogAsync(paths.CatalogPath, root, ct).ConfigureAwait(false);
        }
        finally
        {
            CatalogWriteLock.Release();
        }
    }

    /// <summary>
    /// Whether <paramref name="json"/> is a tool catalog, and if not, what to
    /// tell the person who pasted it.
    ///
    /// <para>Pure and synchronous on purpose. Everything it checks is decided
    /// from the text alone, so the whole answer is known before any file is
    /// opened for writing — which is what makes a rejected import a no-op rather
    /// than a truncated catalog.</para>
    ///
    /// <para>The bar is deliberately low: an object, at least one of the two
    /// arrays, and an id on every entry. Anything stricter would reject a
    /// catalog carrying a property this version has not met yet, and the file is
    /// hand-edited often enough that that is a real shape rather than a
    /// hypothetical one.</para>
    /// </summary>
    public static bool TryReadCatalog(string json, out JsonObject root, out string error)
    {
        root = [];

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "There is nothing to import.";
            return false;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"That is not valid JSON: {ex.Message}";
            return false;
        }

        if (parsed is not JsonObject document)
        {
            error = "A tool catalog is a JSON object.";
            return false;
        }

        var plugins = document["plugins"] as JsonArray;
        var servers = document["mcpServers"] as JsonArray;

        if (plugins is null && servers is null)
        {
            error = "A tool catalog needs a \"plugins\" or an \"mcpServers\" array.";
            return false;
        }

        if (!EveryEntryCarriesAnId(plugins, "plugins", "name", out error)
            || !EveryEntryCarriesAnId(servers, "mcpServers", "packageId", out error))
        {
            return false;
        }

        root = document;
        error = string.Empty;
        return true;
    }

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

    /// <summary>A string property, or nothing. It reads the value rather than
    /// demanding one, because an imported catalog is hand-written and a
    /// <c>"name": 3</c> in it is a validation finding rather than a crash.</summary>
    private static string GetString(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    /// <summary>Which array a key addresses, which property identifies an entry
    /// in it, and the id itself. Public because it is how a caller turns the key
    /// a row carries back into the entry behind it.</summary>
    public static (string ArrayName, string IdName, string IdValue) ParseKey(string key)
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

    private static JsonObject EmptyCatalog() => new()
    {
        ["plugins"] = new JsonArray(),
        ["mcpServers"] = new JsonArray()
    };

    private static bool EveryEntryCarriesAnId(JsonArray? array, string arrayName, string idName, out string error)
    {
        error = string.Empty;

        if (array is null)
        {
            return true;
        }

        foreach (var node in array)
        {
            if (node is JsonObject entry && !string.IsNullOrWhiteSpace(GetString(entry, idName)))
            {
                continue;
            }

            error = $"Every entry in \"{arrayName}\" needs a \"{idName}\".";
            return false;
        }

        return true;
    }

    private static async Task<JsonObject> ReadCatalogAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false))?.AsObject()
            ?? throw new InvalidOperationException("Tool catalog is empty.");
    }

    /// <summary>
    /// Writes the whole catalog through a temp file and moves it into place.
    ///
    /// <para>Serialising straight into the catalog truncates it first, so a
    /// failure part-way through — a full disk, a sync client holding the handle —
    /// would leave a file that is neither the old catalog nor the new one and
    /// that nothing can parse. The move is the only step that touches the real
    /// path, and it either happens or does not.</para>
    /// </summary>
    private static async Task WriteCatalogAsync(string catalogPath, JsonNode root, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath) ?? Environment.CurrentDirectory);

        var tempPath = catalogPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, root, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, catalogPath, overwrite: true);
    }
}

public interface ICopilotToolService
{
    Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default);

    /// <summary>Writes the empty catalog a machine with none starts from. The
    /// one act that is available when <see cref="CopilotToolCatalog.CatalogExists"/>
    /// is false.</summary>
    Task<CopilotToolActionResult> CreateCatalogAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> AddAsync(CopilotToolDraft draft, CancellationToken ct = default);

    Task<CopilotToolActionResult> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Replaces the whole catalog with the document in
    /// <paramref name="json"/>.
    /// <para>
    /// A string, and not a stream, a path or an <c>IBrowserFile</c>. Whatever the
    /// screen picked the catalog out of is the screen's business; taking a file
    /// here would put <c>Microsoft.AspNetCore.Components.Forms</c> in a port that
    /// a console host and a test both have to be able to call.
    /// </para></summary>
    Task<CopilotToolActionResult> ImportAsync(string json, CancellationToken ct = default);
}

public sealed class UnsupportedCopilotToolService : ICopilotToolService
{
    private const string Message = "Copilot tool management is only available in the desktop app.";

    /// <summary>No catalog, no path to name, and nothing on this host can edit
    /// one — so the pane draws the message and none of the affordances.</summary>
    public Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(new CopilotToolCatalog([], Message, CatalogExists: false, CatalogPath: string.Empty, CanEditCatalog: false));

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> CreateCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> AddAsync(CopilotToolDraft draft, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> RemoveAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> ImportAsync(string json, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));
}
