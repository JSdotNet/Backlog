using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Backlog.Modules.DevPc.Abstractions;

public enum DevToolKind
{
    Plugin,
    McpServer,

    /// <summary>A Claude plugin marketplace — the place Claude plugins are
    /// installed <em>from</em>, rather than a tool in its own right.
    ///
    /// <para>It earns a row because it is the one piece of Claude setup that has
    /// to be right before any Claude plugin can resolve at all: an id is
    /// <c>&lt;name&gt;@&lt;marketplace&gt;</c>, and a machine whose marketplace was
    /// never added fails every Claude plugin for one reason that was nowhere on
    /// the screen.</para></summary>
    Marketplace
}

/// <summary>
/// Which AI host a catalog entry is for.
///
/// <para>Flags rather than an enum with a Both member alone, because the question
/// asked of it is always "does this entry target that host" and a bit test is the
/// honest form of that. A catalog entry that says nothing means both: the
/// catalogs predate Claude support entirely, and reading silence as "Copilot
/// only" would have quietly dropped every existing entry out of the Claude
/// half.</para>
/// </summary>
[Flags]
public enum DevToolHosts
{
    None = 0,
    Copilot = 1,
    Claude = 2,
    Both = Copilot | Claude
}

/// <summary>
/// What one host has to say about one catalog entry.
///
/// <para>A plugin that targets both hosts is installed twice, from two different
/// mechanisms, and the two answers routinely disagree — Copilot has it at 1.2.0
/// and Claude has never heard of it. The row stays one row because the catalog
/// entry is one entry, and this is where the two answers live so that neither has
/// to be flattened away to make room for the other.</para>
/// </summary>
public sealed record DevToolHostState(
    DevToolHosts Host,
    bool Installed,
    string InstalledVersion,
    string AvailableVersion,
    string Status);

public enum DevToolAction
{
    Update,
    Enable,
    Disable
}

public sealed record DevToolInfo(
    string Key,
    DevToolKind Kind,
    string Name,
    string? Source,
    bool ConfiguredEnabled,
    bool Installed,
    string InstalledVersion,
    string AvailableVersion,
    string Status)
{
    /// <summary>Which hosts this entry is for. Init-only with a default, so the
    /// harness, the unsupported service and every test that builds one
    /// positionally still compile — and so an entry nobody has thought about
    /// lands on the same "both hosts" the catalog format means by silence.</summary>
    public DevToolHosts Hosts { get; init; } = DevToolHosts.Both;

    /// <summary>What each targeted host answered, or empty when the host behind
    /// this row does not separate them.
    ///
    /// <para>Empty is not "no hosts": it is a host that reports one aggregate
    /// answer, which is what every caller did before Claude existed. The derived
    /// properties below read the per-host detail when it is there and fall back to
    /// the single values when it is not, so an old-shaped row behaves exactly as
    /// it always did.</para></summary>
    public IReadOnlyList<DevToolHostState> HostStates { get; init; } = [];

    public bool UpdateAvailable => HostStates.Count > 0
        ? HostStates.Any(state => VersionDiffers(state.InstalledVersion, state.AvailableVersion))
        : VersionDiffers(InstalledVersion, AvailableVersion);

    /// <summary>An update on <em>any</em> targeted host is an update to offer. One
    /// press acts on every host the entry targets, so a Claude plugin that is a
    /// version behind is worth a button even when the Copilot copy is current.</summary>
    public bool CanUpdate => ConfiguredEnabled && (HostStates.Count > 0
        ? HostStates.Any(state => state.Installed && VersionDiffers(state.InstalledVersion, state.AvailableVersion))
        : Installed && UpdateAvailable);

    /// <summary>A tool this machine is configured to have and does not.
    ///
    /// <para>Separate from <see cref="CanUpdate" /> because the two are not the
    /// same offer and the screen had only the one: an enabled tool that is absent
    /// cannot be updated, so it fell through to whatever the "nothing to do"
    /// branch said and was announced as up to date beside its own "not installed"
    /// version.</para>
    ///
    /// <para>Missing on any one targeted host counts. A plugin Copilot already has
    /// and Claude has not is still a plugin this machine is short of.</para></summary>
    public bool CanInstall => ConfiguredEnabled && (HostStates.Count > 0
        ? HostStates.Any(state => !state.Installed)
        : !Installed);

    /// <summary>Whether a lookup actually answered with a version.
    ///
    /// <para>"Up to date" is a claim about something somebody found. When the
    /// lookup failed there is no version to have matched, and saying so is the
    /// difference between a checked tool and an unchecked one.</para></summary>
    public bool AvailableVersionKnown => HostStates.Count > 0
        ? HostStates.Any(state => IsKnownVersion(state.AvailableVersion))
        : IsKnownVersion(AvailableVersion);

    private static bool IsKnownVersion(string availableVersion) =>
        !string.IsNullOrWhiteSpace(availableVersion)
        && !availableVersion.Trim().Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase);

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

        // Four values that occupy a version column without being one. The dash is
        // the newest of them and the one with teeth: a marketplace row carries it
        // opposite the word "configured", and comparing those two as versions
        // reported an update on every check forever.
        if (trimmed.Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(DevToolOutput.NotInstalled, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(DevToolOutput.NoVersion, StringComparison.OrdinalIgnoreCase)
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
/// <see cref="DevToolKind"/> enum beside it. The enum says which array an
/// entry lives in; this says what the host does with it once it is there, and
/// the host reads it as free text it may not recognise.
/// </para>
/// <para>
/// The Claude fields are all optional and all nullable, because every one of them
/// has a documented fallback in the catalog format — a plugin's Claude name falls
/// back to its name, its marketplace to the first one configured, an MCP server's
/// Claude name to the server's own. Writing a property that only restates the
/// fallback would make the catalog harder to read for no gain, so a blank one is
/// left out of the entry entirely.
/// </para></summary>
public sealed record DevToolDraft(
    DevToolKind Kind,
    string Id,
    string? Source = null,
    string? DisplayName = null,
    string? PluginKind = null)
{
    /// <summary>Which hosts the new entry is for. <see cref="DevToolHosts.Both"/>
    /// is written as no <c>hosts</c> property at all, matching what the format
    /// means by silence and what every catalog written before Claude support
    /// already says.</summary>
    public DevToolHosts Hosts { get; init; } = DevToolHosts.Both;

    /// <summary>What the plugin is called in the Claude marketplace, when that is
    /// not what Copilot calls it.</summary>
    public string? ClaudeName { get; init; }

    /// <summary>Which marketplace the plugin resolves against, when it is not the
    /// first one in the catalog.</summary>
    public string? ClaudeMarketplace { get; init; }

    /// <summary>What the MCP server is registered as with <c>claude mcp add</c>.</summary>
    public string? ClaudeServerName { get; init; }

    /// <summary>The executable <c>claude mcp add</c> is pointed at. An MCP server
    /// entry with no command is registered nowhere — the shared .NET tool install
    /// still happens, because that half is what both hosts share.</summary>
    public string? ClaudeCommand { get; init; }

    /// <summary>The arguments that follow the command, in order.</summary>
    public IReadOnlyList<string> ClaudeArgs { get; init; } = [];
}

/// <summary>
/// One command a host ran while answering, with everything it printed.
///
/// <para>Checking tools means running about a dozen processes — a CLI probe, two
/// inventory listings, and a version lookup per configured tool — and until this
/// existed the only trace of any of them was the single sentence in
/// <see cref="DevToolCatalog.Message"/>. A failing <c>dotnet tool search</c>
/// or a <c>plugin install</c> that refused had already been captured and was then
/// dropped on the floor, which left the operator with a summary and nothing to
/// read behind it.</para>
/// </summary>
public sealed record DevToolCommand(string CommandLine, int ExitCode, string Output);

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
public sealed record DevToolCatalog(
    IReadOnlyList<DevToolInfo> Tools,
    string Message,
    bool CatalogExists = false,
    string CatalogPath = "",
    bool CanEditCatalog = false)
{
    /// <summary>What was run to produce this catalog, in the order it ran.
    ///
    /// <para>An init-only property rather than a positional parameter: a host
    /// that has no processes behind it — the browser harness, the unsupported
    /// service — still constructs this without it, and diagnostics arriving is
    /// not a reason for those to stop compiling.</para></summary>
    public IReadOnlyList<DevToolCommand> Commands { get; init; } = [];
}

public sealed record DevToolActionResult(bool Succeeded, string Message)
{
    /// <inheritdoc cref="DevToolCatalog.Commands" />
    public IReadOnlyList<DevToolCommand> Commands { get; init; } = [];

    public static DevToolActionResult Ok(string message, IReadOnlyList<DevToolCommand>? commands = null) =>
        new(true, message) { Commands = commands ?? [] };

    public static DevToolActionResult Failed(string message, IReadOnlyList<DevToolCommand>? commands = null) =>
        new(false, message) { Commands = commands ?? [] };
}

public sealed record DevToolConfigurationPaths(string CatalogPath, string PcConfigPath)
{
    private const string DefaultRepositoryRoot = "%USERPROFILE%\\.copilot\\repos\\Backlog";
    private const string ToolFolderName = ".tools";

    /// <summary>What the catalog is called now that it drives two hosts. The file
    /// was <c>copilot-tools.json</c> when Copilot was the only thing in it, and the
    /// name had become a lie about its contents.</summary>
    public const string CatalogFileName = "ai-tools.json";

    /// <summary>What it used to be called.
    ///
    /// <para>Read, never written. The catalog lives in a synced folder that several
    /// machines share and that a person hand-edits, so an upgrade that only looked
    /// for the new name would present every one of those machines with the
    /// "no catalog yet" empty state and a create button pointed at a path beside
    /// the catalog they already had.</para></summary>
    public const string LegacyCatalogFileName = "copilot-tools.json";

    public static DevToolConfigurationPaths CreateDefault(string? machineName = null, string? startPath = null, string? storageRootDirectory = null)
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

    public static DevToolConfigurationPaths FromRepositoryRoot(string repositoryRoot, string? machineName = null)
        => FromStorageRoot(repositoryRoot, machineName);

    public static DevToolConfigurationPaths FromStorageRoot(string storageRoot, string? machineName = null)
    {
        var expandedRoot = Environment.ExpandEnvironmentVariables(storageRoot);
        var pcName = NormalizeMachineName(machineName ?? Environment.MachineName);
        var toolFolder = Path.Combine(expandedRoot, ToolFolderName);

        return new DevToolConfigurationPaths(
            ResolveCatalogFile(toolFolder),
            ResolveCatalogFile(Path.Combine(toolFolder, pcName)));
    }

    public static DevToolConfigurationPaths FromCatalogPath(string catalogPath, string? machineName = null)
    {
        var expandedCatalog = Environment.ExpandEnvironmentVariables(catalogPath);
        var toolRoot = Path.GetDirectoryName(expandedCatalog) ?? Environment.CurrentDirectory;
        var pcName = NormalizeMachineName(machineName ?? Environment.MachineName);

        return new DevToolConfigurationPaths(
            expandedCatalog,
            ResolveCatalogFile(Path.Combine(toolRoot, pcName)));
    }

    /// <summary>
    /// Which of the two names a folder's catalog actually goes by.
    ///
    /// <para>The new name wins whenever it is on disk, and the legacy one is only
    /// answered with when it is the only one there — so a machine mid-rename, with
    /// both files present, reads the one the rename produced rather than the one it
    /// left behind. A folder with neither answers with the new name, which is what
    /// the create button then writes.</para>
    /// </summary>
    private static string ResolveCatalogFile(string folder)
    {
        var current = Path.Combine(folder, CatalogFileName);
        if (File.Exists(current))
        {
            return current;
        }

        var legacy = Path.Combine(folder, LegacyCatalogFileName);

        return File.Exists(legacy) ? legacy : current;
    }

    private static string? FindCatalogRoot(string startPath)
    {
        var directory = Directory.Exists(startPath)
            ? new DirectoryInfo(startPath)
            : new FileInfo(startPath).Directory;

        while (directory is not null)
        {
            // Either name stops the walk. A repository that has not been renamed
            // yet is still a repository with a catalog in it, and walking past it
            // would land on whichever ancestor happened to have one.
            if (File.Exists(Path.Combine(directory.FullName, ToolFolderName, CatalogFileName))
                || File.Exists(Path.Combine(directory.FullName, ToolFolderName, LegacyCatalogFileName)))
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

public sealed record DevToolConfigurationDocument(JsonNode Root, bool PcConfigExists, string CatalogPath, string PcConfigPath);

public static class DevToolConfiguration
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
    public static bool CatalogExists(DevToolConfigurationPaths paths) => File.Exists(paths.CatalogPath);

    /// <summary>Where the Claude marketplaces live in the catalog. A path rather
    /// than a property name because it is the one array that is nested, and
    /// spelling it as one keeps every array reader in this file taking the same
    /// kind of argument.</summary>
    public const string MarketplacesPath = "claude.marketplaces";

    /// <summary>The key a tool is addressed by, minted in one place so the
    /// prefixes <see cref="ParseKey"/> reads are the prefixes callers write.</summary>
    public static string KeyFor(DevToolKind kind, string id) => kind switch
    {
        DevToolKind.Plugin => $"plugin:{id}",
        DevToolKind.Marketplace => $"marketplace:{id}",
        _ => $"mcp:{id}"
    };

    /// <summary>
    /// Which hosts an entry declares, read from its <c>hosts</c> array.
    ///
    /// <para>Absent, empty, and present-but-all-blank all mean both. The catalog
    /// format uses silence for the common case, and a machine whose entries all
    /// predate Claude support has to keep working rather than lose its Claude
    /// half to a property nobody wrote.</para>
    /// </summary>
    public static DevToolHosts ParseHosts(JsonNode? entry) =>
        DevToolOutput.ParseHosts(entry?["hosts"] is JsonArray hosts
            ? hosts.Select(node => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToString())
            : null);

    /// <summary>Writes the empty catalog a machine starts from: the two arrays
    /// every reader here expects, indented the way the rest of the file is, so
    /// the first hand edit after this lands in a document that already looks
    /// hand-written.</summary>
    /// <exception cref="InvalidOperationException">A catalog is already there.
    /// Creating over it would discard every entry in it.</exception>
    public static async Task CreateCatalogAsync(DevToolConfigurationPaths paths, CancellationToken ct = default)
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
    public static async Task AddToCatalogAsync(DevToolConfigurationPaths paths, DevToolDraft draft, CancellationToken ct = default)
    {
        var id = draft.Id?.Trim() ?? string.Empty;
        var source = draft.Source?.Trim() ?? string.Empty;

        if (id.Length == 0)
        {
            throw new InvalidOperationException(draft.Kind switch
            {
                DevToolKind.Plugin => "A plugin needs a name.",
                DevToolKind.Marketplace => "A marketplace needs a name.",
                _ => "An MCP server needs a package id."
            });
        }

        // A plugin with no source is an entry the host cannot install from, so it
        // is rejected here rather than written and failed against later. An MCP
        // server needs none: its package id is where it comes from. A marketplace
        // is all source — the name is only how the CLI refers to it afterwards.
        if (draft.Kind is DevToolKind.Plugin && source.Length == 0)
        {
            throw new InvalidOperationException("A plugin needs a source.");
        }

        if (draft.Kind is DevToolKind.Marketplace && source.Length == 0)
        {
            throw new InvalidOperationException("A marketplace needs a source.");
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

            if (draft.Kind is DevToolKind.Marketplace)
            {
                // A marketplace carries no enabled flag: it is not a tool this
                // machine may or may not want, it is where the Claude plugins that
                // do want it are resolved from.
                entry["source"] = source;
                array.Add(entry);

                await WriteCatalogAsync(paths.CatalogPath, root, ct).ConfigureAwait(false);
                return;
            }

            if (draft.Kind is DevToolKind.Plugin)
            {
                entry["source"] = source;
                if (!string.IsNullOrWhiteSpace(draft.PluginKind))
                {
                    entry["kind"] = draft.PluginKind.Trim();
                }

                WriteIfPresent(entry, "claudeName", draft.ClaudeName);
                WriteIfPresent(entry, "claudeMarketplace", draft.ClaudeMarketplace);
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

                if (ClaudeServerSection(draft) is { } claude)
                {
                    entry["claude"] = claude;
                }
            }

            WriteHosts(entry, draft.Hosts);
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
    public static async Task RemoveFromCatalogAsync(DevToolConfigurationPaths paths, string key, CancellationToken ct = default)
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
            var array = FindArray(root, arrayName);
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
    public static async Task RemoveEnabledOverrideAsync(DevToolConfigurationPaths paths, string key, CancellationToken ct = default)
    {
        if (!File.Exists(paths.PcConfigPath))
        {
            return;
        }

        var root = await ReadPcConfigOrEmptyAsync(paths.PcConfigPath, ct).ConfigureAwait(false);
        var (arrayName, idName, idValue) = ParseKey(key);

        if (FindArray(root, arrayName) is not { } array || FindObject(array, idName, idValue) is not { } entry)
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
    public static async Task ImportCatalogAsync(DevToolConfigurationPaths paths, string json, CancellationToken ct = default)
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
        var marketplaces = FindArray(document, MarketplacesPath);

        // Marketplaces count as content on their own. A machine that installs only
        // Claude plugins is set up by adding the marketplace first, and refusing
        // that catalog would mean the import could not be used to bootstrap the one
        // thing every Claude plugin id resolves against.
        if (plugins is null && servers is null && marketplaces is null)
        {
            error = "A tool catalog needs a \"plugins\", an \"mcpServers\" or a \"claude.marketplaces\" array.";
            return false;
        }

        if (!EveryEntryCarriesAnId(plugins, "plugins", "name", out error)
            || !EveryEntryCarriesAnId(servers, "mcpServers", "packageId", out error)
            || !EveryEntryCarriesAnId(marketplaces, MarketplacesPath, "name", out error))
        {
            return false;
        }

        root = document;
        error = string.Empty;
        return true;
    }

    /// <summary>The Claude marketplaces a catalog declares, in the order it
    /// declares them — which matters, because the first is the default.</summary>
    public static IEnumerable<JsonNode> MarketplaceEntries(JsonNode? root) =>
        FindArray(root, MarketplacesPath)?.Where(node => node is not null).Cast<JsonNode>() ?? [];

    /// <summary>
    /// The marketplace a plugin resolves against when it names none.
    ///
    /// <para>The first one in the array, and the ordering of a JSON array is the
    /// only thing making it the default — so this reads it in one place rather
    /// than leaving each caller to decide that <c>[0]</c> is meaningful.</para>
    /// </summary>
    public static string? DefaultMarketplaceName(JsonNode? root)
    {
        foreach (var marketplace in MarketplaceEntries(root))
        {
            if (marketplace["name"] is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return null;
    }

    public static async Task<DevToolConfigurationDocument> ReadAsync(DevToolConfigurationPaths paths, CancellationToken ct = default)
    {
        await using var catalogStream = File.OpenRead(paths.CatalogPath);
        var root = await JsonNode.ParseAsync(catalogStream, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tool catalog is empty.");

        if (!File.Exists(paths.PcConfigPath))
        {
            return new DevToolConfigurationDocument(root, false, paths.CatalogPath, paths.PcConfigPath);
        }

        await using var pcStream = File.OpenRead(paths.PcConfigPath);
        var pcRoot = await JsonNode.ParseAsync(pcStream, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PC tool config is empty.");

        MergeArray(root, pcRoot, "plugins", "name");
        MergeArray(root, pcRoot, "mcpServers", "packageId");

        return new DevToolConfigurationDocument(root, true, paths.CatalogPath, paths.PcConfigPath);
    }

    public static async Task WriteEnabledOverrideAsync(DevToolConfigurationPaths paths, string key, bool enabled, CancellationToken ct = default)
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

    /// <summary>
    /// The array at a dotted path, making every object along the way.
    ///
    /// <para>Only the marketplaces are nested today, and the alternative was a
    /// second family of readers and writers that knew about <c>claude</c>
    /// specifically. One path-walking lookup keeps <see cref="ParseKey"/> able to
    /// answer for all three kinds with the same tuple.</para>
    /// </summary>
    private static JsonArray GetOrCreateArray(JsonObject root, string arrayName)
    {
        var segments = arrayName.Split('.');
        var parent = root;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (parent[segments[index]] is not JsonObject child)
            {
                child = [];
                parent[segments[index]] = child;
            }

            parent = child;
        }

        var leaf = segments[^1];
        if (parent[leaf] is JsonArray existing)
        {
            return existing;
        }

        var array = new JsonArray();
        parent[leaf] = array;
        return array;
    }

    /// <inheritdoc cref="GetOrCreateArray" />
    /// <summary>The array at a dotted path, or nothing when any step of it is
    /// missing. The reading half of <see cref="GetOrCreateArray"/>, which never
    /// writes into a document it was only asked to look at.</summary>
    private static JsonArray? FindArray(JsonNode? root, string arrayName)
    {
        JsonNode? node = root;
        foreach (var segment in arrayName.Split('.'))
        {
            node = node?[segment];
        }

        return node as JsonArray;
    }

    private static void WriteIfPresent(JsonObject entry, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entry[name] = value.Trim();
        }
    }

    /// <summary>Writes <c>hosts</c> only when it says something the format does not
    /// already say by omission. <see cref="DevToolHosts.Both"/> is what an entry
    /// with no such property means, so writing it would add a line that changes
    /// nothing and invites the reader to wonder why the entry beside it lacks
    /// one.</summary>
    private static void WriteHosts(JsonObject entry, DevToolHosts hosts)
    {
        if (hosts is DevToolHosts.Both or DevToolHosts.None)
        {
            return;
        }

        var names = new JsonArray();
        if (hosts.HasFlag(DevToolHosts.Copilot))
        {
            names.Add("copilot");
        }

        if (hosts.HasFlag(DevToolHosts.Claude))
        {
            names.Add("claude");
        }

        entry["hosts"] = names;
    }

    /// <summary>The <c>claude</c> section of an MCP server entry, or nothing when
    /// the draft named no command. The section exists to be handed to
    /// <c>claude mcp add</c>, and one with no command is a registration that could
    /// never be made.</summary>
    private static JsonObject? ClaudeServerSection(DevToolDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ClaudeCommand))
        {
            return null;
        }

        var claude = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(draft.ClaudeServerName)
                ? (string.IsNullOrWhiteSpace(draft.DisplayName) ? draft.Id.Trim() : draft.DisplayName.Trim())
                : draft.ClaudeServerName.Trim(),
            ["command"] = draft.ClaudeCommand.Trim()
        };

        var args = draft.ClaudeArgs.Where(argument => !string.IsNullOrWhiteSpace(argument)).ToArray();
        if (args.Length > 0)
        {
            var values = new JsonArray();
            foreach (var argument in args)
            {
                values.Add(argument.Trim());
            }

            claude["args"] = values;
        }

        return claude;
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

        if (key.StartsWith("marketplace:", StringComparison.OrdinalIgnoreCase))
        {
            return (MarketplacesPath, "name", key["marketplace:".Length..]);
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

public interface IDevToolService
{
    Task<DevToolCatalog> ListAsync(CancellationToken ct = default);

    Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default);

    Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default);

    Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default);

    Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default);

    /// <summary>Writes the empty catalog a machine with none starts from. The
    /// one act that is available when <see cref="DevToolCatalog.CatalogExists"/>
    /// is false.</summary>
    Task<DevToolActionResult> CreateCatalogAsync(CancellationToken ct = default);

    Task<DevToolActionResult> AddAsync(DevToolDraft draft, CancellationToken ct = default);

    Task<DevToolActionResult> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Replaces the whole catalog with the document in
    /// <paramref name="json"/>.
    /// <para>
    /// A string, and not a stream, a path or an <c>IBrowserFile</c>. Whatever the
    /// screen picked the catalog out of is the screen's business; taking a file
    /// here would put <c>Microsoft.AspNetCore.Components.Forms</c> in a port that
    /// a console host and a test both have to be able to call.
    /// </para></summary>
    Task<DevToolActionResult> ImportAsync(string json, CancellationToken ct = default);
}

public sealed class UnsupportedDevToolService : IDevToolService
{
    private const string Message = "Tool management is only available in the desktop app.";

    /// <summary>No catalog, no path to name, and nothing on this host can edit
    /// one — so the pane draws the message and none of the affordances.</summary>
    public Task<DevToolCatalog> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(new DevToolCatalog([], Message, CatalogExists: false, CatalogPath: string.Empty, CanEditCatalog: false));

    public Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> CreateCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> AddAsync(DevToolDraft draft, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> RemoveAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));

    public Task<DevToolActionResult> ImportAsync(string json, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed(Message));
}
