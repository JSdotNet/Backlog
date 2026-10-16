using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.DevPc;

/// <summary>
/// Where each host keeps the manifest that says what it has installed.
///
/// <para>These are the files the hosts' own CLIs answer <c>plugin list</c>,
/// <c>marketplace list</c> and <c>mcp get</c> out of. Reading them rather than
/// running the CLIs is what lets a new catalog be written in one press, on a
/// machine where one of the CLIs is missing, without a dozen processes.</para>
/// </summary>
/// <param name="CopilotConfig"><c>~/.copilot/config.json</c>, whose
/// <c>installedPlugins</c> carries each Copilot plugin with where it came from.</param>
/// <param name="ClaudeInstalledPlugins"><c>~/.claude/plugins/installed_plugins.json</c>.</param>
/// <param name="ClaudeKnownMarketplaces"><c>~/.claude/plugins/known_marketplaces.json</c>.</param>
/// <param name="ClaudeSettings"><c>~/.claude/settings.json</c>, for <c>enabledPlugins</c>.</param>
/// <param name="ClaudeUserConfig"><c>~/.claude.json</c>, for the user-scope <c>mcpServers</c>.</param>
/// <param name="ClaudeDesktopConfigCandidates">Where Claude Desktop's config may be,
/// in the order <see cref="DevToolClaudeDesktopConfig.ConfigPathCandidates"/> gives.</param>
internal sealed record DevToolHostManifestPaths(
    string CopilotConfig,
    string ClaudeInstalledPlugins,
    string ClaudeKnownMarketplaces,
    string ClaudeSettings,
    string ClaudeUserConfig,
    IReadOnlyList<string> ClaudeDesktopConfigCandidates)
{
    /// <summary>The manifests under one user's folders.</summary>
    public static DevToolHostManifestPaths ForUser(string userProfile, string appDataRoaming, string appDataLocal) => new(
        Path.Combine(userProfile, ".copilot", "config.json"),
        Path.Combine(userProfile, ".claude", "plugins", "installed_plugins.json"),
        Path.Combine(userProfile, ".claude", "plugins", "known_marketplaces.json"),
        Path.Combine(userProfile, ".claude", "settings.json"),
        Path.Combine(userProfile, ".claude.json"),
        DevToolClaudeDesktopConfig.ConfigPathCandidates(appDataRoaming, appDataLocal));

    /// <summary>The manifests of the user this process runs as.</summary>
    public static DevToolHostManifestPaths ForCurrentUser() => ForUser(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}

/// <summary>The text of each manifest, or <see langword="null"/> for one that is
/// not on this machine.</summary>
internal sealed record DevToolHostManifests(
    string? CopilotConfig = null,
    string? ClaudeInstalledPlugins = null,
    string? ClaudeKnownMarketplaces = null,
    string? ClaudeSettings = null,
    string? ClaudeUserConfig = null,
    string? ClaudeDesktopConfig = null);

/// <summary>A catalog built from what this machine has, and what it says about
/// itself.</summary>
/// <param name="Notes">What was seen and deliberately left out, one sentence
/// each, so the person reading the result knows which rows to add by hand.</param>
internal sealed record DevToolCatalogSeedResult(
    JsonObject Catalog,
    int Plugins,
    int Marketplaces,
    int McpServers,
    IReadOnlyList<string> Notes)
{
    /// <summary>The sentence the pane shows after the write.</summary>
    public string Describe(string catalogPath)
    {
        var counts = Plugins + Marketplaces + McpServers == 0
            ? $"Created an empty tool catalog at {catalogPath}: nothing installed was found to seed it with."
            : $"Created a tool catalog at {catalogPath} from this machine: {Count(Plugins, "plugin")}, {Count(Marketplaces, "marketplace")} and {Count(McpServers, "MCP server")}.";

        return Notes.Count == 0 ? counts : $"{counts} {string.Join(' ', Notes)}";
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// Builds the first catalog for "Create from this machine" out of the hosts'
/// own manifests.
///
/// <para>What goes in: every Copilot plugin with a source to install it from;
/// every Claude plugin installed at user scope, folded into the Copilot entry of
/// the same name when there is one; every Claude marketplace; every MCP server
/// registered with Claude at user scope or with Claude Desktop. Each entry is
/// written enabled as the host has it, so the first check after the write shows
/// the machine as it already is.</para>
///
/// <para>What stays out, and why:</para>
/// <list type="bullet">
/// <item>Applications. Every application probe is read out of its catalog entry,
/// so nothing can name an installed application before the entry exists.</item>
/// <item>Claude plugins installed only at project scope. Those are one
/// repository's choice, the same line local ADR 0012 §3 draws for MCP
/// registrations.</item>
/// <item>Copilot's own MCP servers. Nothing in this adapter registers an MCP
/// server with Copilot, so an entry for one would be a row nothing acts on.</item>
/// <item>Headers, environment values, and URLs or arguments that look like they
/// carry a credential. They are where tokens live, and the catalog is a file
/// meant to be shared.</item>
/// <item>A marketplace kept in a local folder, whose path names this machine.</item>
/// </list>
///
/// <para>Never throws for a manifest that is missing, unreadable or the wrong
/// shape: that host contributes nothing and the result says so.</para>
/// </summary>
internal static class DevToolCatalogSeed
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        // Copilot writes a comment line at the top of its config.json.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads every manifest at <paramref name="paths"/> and builds the seed.</summary>
    public static async Task<DevToolCatalogSeedResult> ReadAsync(DevToolHostManifestPaths paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string? desktop = null;
        foreach (var candidate in paths.ClaudeDesktopConfigCandidates)
        {
            if ((desktop = await ReadIfPresentAsync(candidate, ct).ConfigureAwait(false)) is not null)
            {
                break;
            }
        }

        return Build(new DevToolHostManifests(
            await ReadIfPresentAsync(paths.CopilotConfig, ct).ConfigureAwait(false),
            await ReadIfPresentAsync(paths.ClaudeInstalledPlugins, ct).ConfigureAwait(false),
            await ReadIfPresentAsync(paths.ClaudeKnownMarketplaces, ct).ConfigureAwait(false),
            await ReadIfPresentAsync(paths.ClaudeSettings, ct).ConfigureAwait(false),
            await ReadIfPresentAsync(paths.ClaudeUserConfig, ct).ConfigureAwait(false),
            desktop));
    }

    /// <summary>Builds the seed from manifest text already read.</summary>
    public static DevToolCatalogSeedResult Build(DevToolHostManifests manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var notes = new List<string>();

        var marketplaces = ReadMarketplaces(Parse(manifests.ClaudeKnownMarketplaces, "Claude's marketplace list", notes), notes);
        var plugins = new List<JsonObject>();
        var byName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        AddCopilotPlugins(Parse(manifests.CopilotConfig, "Copilot's config", notes), plugins, byName, notes);
        AddClaudePlugins(
            Parse(manifests.ClaudeInstalledPlugins, "Claude's plugin list", notes),
            Parse(manifests.ClaudeSettings, "Claude's settings", notes),
            plugins,
            byName,
            notes);

        var servers = ReadMcpServers(
            Parse(manifests.ClaudeUserConfig, "Claude's user config", notes),
            Parse(manifests.ClaudeDesktopConfig, "Claude Desktop's config", notes),
            notes);

        var catalog = new JsonObject();
        if (marketplaces.Count > 0)
        {
            catalog["claude"] = new JsonObject
            {
                ["marketplaces"] = new JsonArray([.. marketplaces.Select(pair => (JsonNode)new JsonObject
                {
                    ["name"] = pair.Key,
                    ["source"] = pair.Value
                })])
            };
        }

        catalog["plugins"] = new JsonArray([.. plugins.Cast<JsonNode>()]);
        catalog[DevToolConfiguration.McpServersArrayName] = new JsonArray([.. servers.Cast<JsonNode>()]);

        return new DevToolCatalogSeedResult(catalog, plugins.Count, marketplaces.Count, servers.Count, notes);
    }

    /// <summary>Claude's marketplaces by name, each with the source
    /// <c>claude plugin marketplace add</c> takes: a GitHub repository or a git
    /// URL. Kept in the manifest's order. A marketplace kept in a local folder is
    /// left out: its path names this machine and this user, and the catalog is
    /// meant to be shared.</summary>
    private static List<KeyValuePair<string, string>> ReadMarketplaces(JsonNode? root, List<string> notes)
    {
        var marketplaces = new List<KeyValuePair<string, string>>();
        if (root is not JsonObject known)
        {
            return marketplaces;
        }

        foreach (var (name, value) in known)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var source = Get(value, "source");
            if ((Text(Get(source, "repo")) ?? SafeUrl(Text(Get(source, "url")))) is { } where)
            {
                marketplaces.Add(new(name, where));
            }
            else
            {
                notes.Add($"Claude's {name} marketplace was left out: it is not on GitHub or at a git URL, so another machine could not add it.");
            }
        }

        return marketplaces;
    }

    private static void AddCopilotPlugins(JsonNode? config, List<JsonObject> plugins, Dictionary<string, JsonObject> byName, List<string> notes)
    {
        if (Get(config, "installedPlugins") is not JsonArray installed)
        {
            return;
        }

        foreach (var plugin in installed)
        {
            if (Text(Get(plugin, "name")) is not { } name || byName.ContainsKey(name))
            {
                continue;
            }

            // The source `copilot plugin install` takes back: owner/repo:path for
            // one installed straight from a repository, name@marketplace for one
            // installed out of a marketplace.
            var repo = Text(Get(Get(plugin, "source"), "repo"));
            var path = Text(Get(Get(plugin, "source"), "path"));
            var marketplace = Text(Get(plugin, "marketplace"));
            var source = repo is not null
                ? path is null ? repo : $"{repo}:{path}"
                : marketplace is not null ? $"{name}@{marketplace}" : null;

            if (source is null)
            {
                notes.Add($"Copilot's {name} was left out: its manifest does not say where it came from.");
                continue;
            }

            var entry = new JsonObject
            {
                ["name"] = name,
                ["source"] = source,
                ["hosts"] = new JsonArray("copilot"),
                ["enabled"] = Flag(Get(plugin, "enabled")) ?? true
            };

            plugins.Add(entry);
            byName[name] = entry;
        }
    }

    private static void AddClaudePlugins(
        JsonNode? installed,
        JsonNode? settings,
        List<JsonObject> plugins,
        Dictionary<string, JsonObject> byName,
        List<string> notes)
    {
        if (Get(installed, "plugins") is not JsonObject ids)
        {
            return;
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectOnly = 0;

        foreach (var (id, installs) in ids)
        {
            var at = id.LastIndexOf('@');
            if (at <= 0 || at == id.Length - 1)
            {
                continue;
            }

            switch (InstalledForTheUser(installs))
            {
                case null:
                    continue;
                case false:
                    projectOnly++;
                    continue;
            }

            var name = id[..at];
            var marketplace = id[(at + 1)..];

            // Absent reads as on: an installed plugin Claude has never been told
            // to switch off is one it loads.
            var enabled = Flag(Get(Get(settings, "enabledPlugins"), id)) ?? true;

            if (!claimed.Add(name))
            {
                notes.Add($"Claude's {id} was left out: a plugin called {name} from another marketplace is already in.");
                continue;
            }

            if (byName.TryGetValue(name, out var shared))
            {
                // The same plugin in both hosts is one row: silence on hosts is
                // both, and the marketplace says where Claude resolves it. One flag
                // answers for both hosts, so it is on when either has it on, and a
                // disagreement is said rather than settled silently.
                var copilotEnabled = Flag(shared["enabled"]) ?? true;
                if (copilotEnabled != enabled)
                {
                    notes.Add($"{name} is on in {(enabled ? "Claude" : "Copilot")} and off in {(enabled ? "Copilot" : "Claude")}; it was written enabled.");
                }

                shared.Remove("hosts");
                shared.Remove("enabled");
                shared["claudeMarketplace"] = marketplace;
                shared["enabled"] = enabled || copilotEnabled;
                continue;
            }

            // No source: a Claude plugin is installed as name@marketplace, and a
            // source here would be read as the plugin's own repository when the
            // row's available version is looked up.
            var entry = new JsonObject
            {
                ["name"] = name,
                ["hosts"] = new JsonArray("claude"),
                ["claudeMarketplace"] = marketplace,
                ["enabled"] = enabled
            };

            plugins.Add(entry);
            byName[name] = entry;
        }

        if (projectOnly > 0)
        {
            notes.Add(projectOnly == 1
                ? "1 Claude plugin installed only for a project was left out."
                : $"{projectOnly} Claude plugins installed only for a project were left out.");
        }
    }

    /// <summary>Whether a plugin's installs include one for the user: true, false
    /// for one installed only for projects, null for a value that is not an
    /// install at all. An install that names no scope predates scopes, when every
    /// install was the user's.</summary>
    private static bool? InstalledForTheUser(JsonNode? installs)
    {
        var all = installs switch
        {
            JsonArray array => array.OfType<JsonObject>().ToList(),
            JsonObject single => [single],
            _ => null
        };

        if (all is null || all.Count == 0)
        {
            return null;
        }

        return all.Any(install => Text(install["scope"]) is not { } scope || scope.Equals("user", StringComparison.OrdinalIgnoreCase));
    }

    private static List<JsonObject> ReadMcpServers(JsonNode? claude, JsonNode? desktop, List<string> notes)
    {
        var servers = new List<JsonObject>();
        var byName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var withheld = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, entry) in Servers(Get(claude, "mcpServers"), "Claude", "claude", notes, withheld))
        {
            servers.Add(entry);
            byName[name] = entry;
        }

        foreach (var (name, entry) in Servers(Get(desktop, DevToolClaudeDesktopConfig.ServersPropertyName), "Claude Desktop", "claude-desktop", notes, withheld))
        {
            if (!byName.TryGetValue(name, out var registered))
            {
                servers.Add(entry);
                byName[name] = entry;
                continue;
            }

            if (SameTarget(registered, entry))
            {
                ((JsonArray)registered["hosts"]!).Add("claude-desktop");
            }
            else
            {
                notes.Add($"Claude Desktop's {name} was left out: Claude has a server of that name that starts something else.");
            }
        }

        if (withheld.Count > 0)
        {
            notes.Add($"Headers, environment values and arguments that looked like credentials were not copied for {string.Join(", ", withheld)}; add them by hand.");
        }

        return servers;
    }

    private static IEnumerable<(string Name, JsonObject Entry)> Servers(JsonNode? node, string hostName, string host, List<string> notes, ISet<string> withheld)
    {
        if (node is not JsonObject registered)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, server) in registered)
        {
            if (string.IsNullOrWhiteSpace(name) || server is not JsonObject declared)
            {
                continue;
            }

            if (!seen.Add(name))
            {
                notes.Add($"{hostName}'s second {name} was left out: the catalog tells servers apart by name alone.");
                continue;
            }

            var type = Text(declared["type"]);
            var entry = new JsonObject { ["name"] = name };
            var withholding = declared["headers"] is not null || declared["headersHelper"] is not null || declared["env"] is not null;

            if (Text(declared["url"]) is { } url
                && (type is null || type.Equals(DevToolConfiguration.HttpTypeName, StringComparison.OrdinalIgnoreCase)))
            {
                if (SafeUrl(url) is not { } safe)
                {
                    notes.Add($"{hostName}'s {name} was left out: its URL carries a query or credentials, which may be a token.");
                    continue;
                }

                entry["type"] = DevToolConfiguration.HttpTypeName;
                entry["url"] = safe;
            }
            else if (Text(declared["command"]) is { } command && (type is null || type.Equals("stdio", StringComparison.OrdinalIgnoreCase)))
            {
                entry["command"] = command;

                var args = (declared["args"] as JsonArray)?
                    .Select(arg => arg is JsonValue value && value.TryGetValue<string>(out var text) ? text : arg?.ToJsonString() ?? string.Empty)
                    .ToList() ?? [];
                var kept = WithoutCredentials(args);
                withholding |= kept.Count != args.Count;

                if (kept.Count > 0)
                {
                    entry["args"] = new JsonArray([.. kept.Select(arg => (JsonNode?)JsonValue.Create(arg))]);
                }
            }
            else
            {
                notes.Add($"{hostName}'s {name} was left out: a {type ?? "server"} registration is not one the catalog can describe.");
                continue;
            }

            if (withholding)
            {
                withheld.Add(name);
            }

            entry["hosts"] = new JsonArray(host);
            entry["enabled"] = true;

            yield return (name, entry);
        }
    }

    /// <summary>The arguments with anything that looks like a credential taken
    /// out: a flag named for a key, token, secret or password together with its
    /// value, and a bare value that looks like a token by itself.</summary>
    private static List<string> WithoutCredentials(IReadOnlyList<string> args)
    {
        var kept = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsCredentialFlag(arg, out var carriesValue))
            {
                // --token=value carries its own value; --token value takes the next.
                if (!carriesValue && i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            if (!LooksLikeAToken(arg))
            {
                kept.Add(arg);
            }
        }

        return kept;
    }

    private static readonly string[] CredentialWords = ["key", "token", "secret", "password", "passwd", "auth", "bearer"];

    private static bool IsCredentialFlag(string arg, out bool carriesValue)
    {
        var equals = arg.IndexOf('=', StringComparison.Ordinal);
        carriesValue = equals >= 0;

        if (!arg.StartsWith('-'))
        {
            return false;
        }

        var flag = (carriesValue ? arg[..equals] : arg).TrimStart('-');
        return CredentialWords.Any(word => flag.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] TokenPrefixes = ["sk-", "ghp_", "gho_", "github_pat_", "xoxb-", "xoxp-", "Bearer "];

    private static bool LooksLikeAToken(string arg) =>
        TokenPrefixes.Any(prefix => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The URL when it carries nothing that may be a credential — no
    /// query and no user information — else nothing. A placeholder such as
    /// <c>${PORT}</c> keeps a URL from parsing, so one that does not parse is
    /// judged by its text instead.</summary>
    private static string? SafeUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Query.Length == 0 && uri.UserInfo.Length == 0 ? url : null;
        }

        var afterScheme = url.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0 ? url[(scheme + 3)..] : url;
        var authority = afterScheme.Split('/', 2)[0];

        return url.Contains('?', StringComparison.Ordinal) || authority.Contains('@', StringComparison.Ordinal) ? null : url;
    }

    private static bool SameTarget(JsonObject left, JsonObject right) =>
        string.Equals(Text(left["url"]), Text(right["url"]), StringComparison.Ordinal)
        && string.Equals(Text(left["command"]), Text(right["command"]), StringComparison.Ordinal)
        && string.Equals(left["args"]?.ToJsonString() ?? "[]", right["args"]?.ToJsonString() ?? "[]", StringComparison.Ordinal);

    private static JsonNode? Parse(string? text, string what, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: Lenient);
        }
        catch (JsonException)
        {
            notes.Add($"{what} could not be read, so nothing was taken from it.");
            return null;
        }
    }

    /// <summary>One property of an object, or nothing for any other shape. The
    /// manifests are other programs' files, and an indexer on a node that turned
    /// out to be an array or a string throws rather than answering.</summary>
    private static JsonNode? Get(JsonNode? node, string name) => node is JsonObject obj ? obj[name] : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static bool? Flag(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static async Task<string?> ReadIfPresentAsync(string path, CancellationToken ct)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
