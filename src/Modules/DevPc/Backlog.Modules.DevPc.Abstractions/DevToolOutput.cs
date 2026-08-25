using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// The text the Copilot CLI, the dotnet CLI and a plugin manifest print, read
/// back as the versions this context talks in.
///
/// <para>Parsing sits here rather than in the desktop adapter beside the process
/// launching for the same reason the catalog format does: it is the language the
/// CLIs and this context have to agree on, not one host's private detail. It is
/// also the half that was wrong — the pane reported every configured plugin as
/// "not installed" because a regex here expected a whitespace table the CLI has
/// never printed — and the half worth testing directly, because a fixture is the
/// real output and a fake process is not. Starting processes stays in the host
/// adapter, where the machine is.</para>
/// </summary>
public static partial class DevToolOutput
{
    /// <summary>What a version reads as when the tool is absent, when no lookup
    /// answered, and when the tool is present but said nothing about which
    /// version it is. Spelled once because three files compare against them and
    /// a fourth renders them.</summary>
    public const string NotInstalled = "not installed";

    /// <inheritdoc cref="NotInstalled" />
    public const string Unknown = "unknown";

    /// <inheritdoc cref="NotInstalled" />
    public const string Installed = "installed";

    /// <summary>What a version column reads when the thing in that row has no
    /// version at all.
    ///
    /// <para>A Claude marketplace is the case: it is added or it is not, and there
    /// is nothing published to compare it against. It has to be a value the version
    /// comparison refuses rather than an empty cell — an empty cell reads as a
    /// lookup that failed, and a dash compared against "configured" would report an
    /// update on every check forever.</para></summary>
    public const string NoVersion = "—";

    /// <summary>A plugin's published version lives in its manifest, at this path
    /// relative to the plugin's own folder in the source repository.</summary>
    private const string ManifestFileName = ".claude-plugin/plugin.json";

    /// <summary>How much of a commit sha is a version. Seven because that is what
    /// <c>git rev-parse --short</c> gives by default; the width matters less than
    /// both ends using the same one — see <see cref="ShortCommit" />.</summary>
    private const int ShortCommitLength = 7;

    /// <summary>What <c>copilot plugin list</c> says is on this machine, by
    /// plugin name.
    ///
    /// <para>A plugin the CLI marks <c>[disabled]</c> is still installed. Whether
    /// this machine should have it is the catalog's <c>enabled</c> flag to
    /// answer; the CLI is only reporting what is on disk, and treating its marker
    /// as the verdict would let the tool override the config it is configured
    /// by.</para></summary>
    public static IReadOnlyDictionary<string, string> ParsePluginList(string output)
    {
        var plugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(output))
        {
            var match = PluginListLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var version = match.Groups["version"].Value;
            plugins[match.Groups["name"].Value] = string.IsNullOrWhiteSpace(version) ? Installed : version;
        }

        return plugins;
    }

    /// <summary>What <c>dotnet tool list --global</c> says is on this machine, by
    /// package id. The ids come back lowercased and the catalog spells them in
    /// mixed case, so the lookup is case-insensitive rather than the ids being
    /// normalised on the way in.</summary>
    public static IReadOnlyDictionary<string, string> ParseDotNetToolList(string output)
    {
        var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("Package Id", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith('-'))
            {
                continue;
            }

            var parts = ColumnGapRegex().Split(trimmed);
            if (parts.Length >= 2)
            {
                tools[parts[0]] = parts[1];
            }
        }

        return tools;
    }

    /// <summary>The latest published version of one package in what
    /// <c>dotnet tool search</c> returned.
    ///
    /// <para>The search is a substring search, so its answer routinely contains
    /// ids that merely start the same way — <c>jsdotnet.project.guidelines.mcpserver</c>
    /// sits two rows from <c>jsdotnet.mcp.guidelines</c>. Matching the whole first
    /// column is what keeps one package's version from being reported as
    /// another's.</para></summary>
    public static string ParseDotNetToolSearchVersion(string output, string packageId)
    {
        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('-'))
            {
                continue;
            }

            var parts = ColumnGapRegex().Split(trimmed);
            if (parts.Length >= 2 && parts[0].Equals(packageId, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return Unknown;
    }

    /// <summary>Where a catalog <c>source</c> points, or <see langword="null" />
    /// when it points somewhere this cannot read.
    ///
    /// <para>Catalog entries are written in the Copilot CLI's own
    /// <c>owner/repo:path</c> shorthand rather than as URLs, so a resolver that
    /// only understood absolute GitHub URLs recognised none of them.</para></summary>
    public static PluginSource? ParsePluginSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var trimmed = source.Trim();

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? FromUrl(uri)
            : FromShorthand(trimmed);
    }

    /// <summary>The version a plugin manifest declares, or <see langword="null" />
    /// when the body is not one.
    ///
    /// <para>The manifest is the authority rather than a GitHub release, because
    /// the repository the plugins ship from publishes no releases at all — asking
    /// for one answered "release not found" for every plugin, which is how every
    /// available version came back unknown. Bodies that are not manifests arrive
    /// routinely for the same reason, so this reports nothing rather than
    /// throwing.</para></summary>
    public static string? ParsePluginManifestVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind is not JsonValueKind.String)
            {
                return null;
            }

            var value = version.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What <c>claude plugin list --json</c> says is installed, keyed by the
    /// <c>&lt;name&gt;@&lt;marketplace&gt;</c> id Claude addresses a plugin by.
    ///
    /// <para>Tolerant of three shapes the CLI has actually produced: a bare array,
    /// an object with the array under some property, and an empty body when
    /// nothing is installed. A version is optional — a plugin installed from a
    /// marketplace need not declare one — so an entry without one reads as
    /// <see cref="Installed"/> rather than as absent, which is the difference
    /// between "here but unversioned" and "not here".</para>
    ///
    /// <para>Never throws. This runs inside the pane's own listing, and a CLI that
    /// printed a warning ahead of its JSON is a row that should say so rather than
    /// a tools tab that fell over.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ClaudePluginState> ParseClaudePluginList(string json)
    {
        var plugins = new Dictionary<string, ClaudePluginState>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in EnumerateJsonArray(json))
        {
            if (entry.ValueKind is not JsonValueKind.Object
                || ReadString(entry, "id") is not { Length: > 0 } id)
            {
                continue;
            }

            // Claude reports a disabled plugin in the same list as an enabled one.
            // Whether it is disabled decides whether an update has to be followed
            // by an enable, so it travels with the version rather than being
            // filtered out here.
            var enabled = !entry.TryGetProperty("enabled", out var enabledValue) || enabledValue.ValueKind is not JsonValueKind.False;
            var version = ReadString(entry, "version");

            plugins[id] = new ClaudePluginState(enabled, string.IsNullOrWhiteSpace(version) ? Installed : version);
        }

        return plugins;
    }

    /// <summary>The marketplace names <c>claude plugin marketplace list --json</c>
    /// reports, read with the same tolerance and for the same reason as
    /// <see cref="ParseClaudePluginList" />.</summary>
    public static IReadOnlySet<string> ParseClaudeMarketplaceList(string json)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in EnumerateJsonArray(json))
        {
            if (entry.ValueKind is JsonValueKind.String && entry.GetString() is { Length: > 0 } bare)
            {
                names.Add(bare);
                continue;
            }

            if (entry.ValueKind is JsonValueKind.Object && ReadString(entry, "name") is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// What <c>claude mcp get &lt;name&gt;</c> reports about one registration, or
    /// <see langword="null" /> when there is no such registration.
    ///
    /// <para>Absence is a normal answer, not a failure: the CLI says
    /// "No MCP server named ..." and exits non-zero, and reading that exit code as
    /// an error would turn "this needs registering" — the case the whole feature
    /// exists for — into a broken row.</para>
    ///
    /// <para>Only the scope and the command are read back. The scope decides
    /// whether the registration is ours to touch at all, and the command decides
    /// whether it still points where the catalog says it should.</para>
    /// </summary>
    public static ClaudeMcpServerDetails? ParseClaudeMcpServer(string output)
    {
        if (string.IsNullOrWhiteSpace(output) || NoSuchMcpServerRegex().IsMatch(output))
        {
            return null;
        }

        var scope = McpServerScopeRegex().Match(output);
        var command = McpServerCommandRegex().Match(output);

        return new ClaudeMcpServerDetails(
            scope.Success ? scope.Groups["value"].Value.Trim() : string.Empty,
            command.Success ? command.Groups["value"].Value.Trim() : string.Empty);
    }

    /// <summary>
    /// The id Claude addresses a catalog plugin by, or <see langword="null" /> when
    /// no marketplace can be resolved for it.
    ///
    /// <para>Null rather than a throw. A catalog with no marketplaces at all is a
    /// real state — it is what every catalog looked like before this — and the row
    /// for such a plugin has to be able to say so beside the plugins that resolved
    /// fine, instead of taking the listing down with it.</para>
    /// </summary>
    public static string? ClaudePluginId(string name, string? claudeName, string? claudeMarketplace, string? defaultMarketplace)
    {
        var pluginName = FirstNonBlank(claudeName, name);
        var marketplace = FirstNonBlank(claudeMarketplace, defaultMarketplace);

        return pluginName is null || marketplace is null ? null : $"{pluginName}@{marketplace}";
    }

    /// <summary>
    /// Which hosts a <c>hosts</c> array names.
    ///
    /// <para>Nothing, an empty array, and an array of blanks all mean both hosts —
    /// see <see cref="DevToolConfiguration.ParseHosts" /> for why silence has
    /// to mean both. A name this version has not met is ignored rather than
    /// rejected, so a catalog written for a third host still installs its Copilot
    /// and Claude entries here.</para>
    /// </summary>
    public static DevToolHosts ParseHosts(IEnumerable<string?>? values)
    {
        if (values is null)
        {
            return DevToolHosts.Both;
        }

        var hosts = DevToolHosts.None;
        var named = false;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            named = true;
            var text = value.Trim();

            if (text.Equals("copilot", StringComparison.OrdinalIgnoreCase))
            {
                hosts |= DevToolHosts.Copilot;
            }
            else if (text.Equals("claude", StringComparison.OrdinalIgnoreCase))
            {
                hosts |= DevToolHosts.Claude;
            }
        }

        return named ? hosts : DevToolHosts.Both;
    }

    /// <summary>A commit sha cut to the width everything else uses.
    ///
    /// <para>Both sides of a repository-backed comparison go through this. A
    /// local HEAD can be asked for short and a remote one cannot, so leaving the
    /// remote at full length would make every repository tool differ from itself
    /// forever — and truncating neither made them equal by construction, which is
    /// how a real pending update stayed invisible.</para></summary>
    public static string ShortCommit(string sha)
    {
        var trimmed = sha.Trim();

        return trimmed.Length <= ShortCommitLength ? trimmed : trimmed[..ShortCommitLength];
    }

    private static PluginSource? FromUrl(Uri uri)
    {
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        // A browsable URL names a ref before it names a folder, and the ref is
        // not part of the path the API wants.
        var path = segments.Length > 4 && (segments[2].Equals("tree", StringComparison.OrdinalIgnoreCase) || segments[2].Equals("blob", StringComparison.OrdinalIgnoreCase))
            ? string.Join('/', segments[4..])
            : null;

        return new PluginSource(segments[0], TrimGitSuffix(segments[1]), path);
    }

    private static PluginSource? FromShorthand(string source)
    {
        var separator = source.IndexOf(':');
        var repository = separator < 0 ? source : source[..separator];
        var path = separator < 0 ? null : source[(separator + 1)..].Trim('/');
        var segments = repository.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 2
            ? new PluginSource(segments[0], TrimGitSuffix(segments[1]), string.IsNullOrWhiteSpace(path) ? null : path)
            : null;
    }

    private static string TrimGitSuffix(string repository) =>
        repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    /// <summary>
    /// The entries of whatever array a JSON body carries, however it is wrapped.
    ///
    /// <para>The Claude CLI's <c>--json</c> output has arrived both bare and inside
    /// an object, and both are read rather than one being declared correct: this
    /// runs against a CLI that ships on its own cadence, and a shape change should
    /// cost a row's version rather than every Claude row on the pane.</para>
    ///
    /// <para>A body that is not JSON at all yields nothing. The CLI prints
    /// warnings and login prompts to the same stream, and those are not a reason
    /// to throw out of a listing.</para>
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind is JsonValueKind.Array)
            {
                return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
            }

            if (document.RootElement.ValueKind is JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Array)
                    {
                        return property.Value.EnumerateArray().Select(element => element.Clone()).ToArray();
                    }
                }
            }

            return [];
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static IEnumerable<string> SplitLines(string value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnGapRegex();

    /// <summary>How the Claude CLI says a registration is not there. Matched
    /// loosely on whitespace because the sentence continues with the name and a
    /// list of the servers that <em>are</em> configured.</summary>
    [GeneratedRegex(@"No\s+MCP\s+server\s+named", RegexOptions.IgnoreCase)]
    private static partial Regex NoSuchMcpServerRegex();

    [GeneratedRegex(@"^\s*Scope:\s*(?<value>.+)$", RegexOptions.Multiline)]
    private static partial Regex McpServerScopeRegex();

    [GeneratedRegex(@"^\s*Command:\s*(?<value>.+)$", RegexOptions.Multiline)]
    private static partial Regex McpServerCommandRegex();

    /// <summary>One installed Claude plugin, as far as this needs to know it.
    ///
    /// <para><see cref="Enabled" /> is Claude's own switch and not the catalog's:
    /// a plugin can be installed and switched off in Claude while the catalog says
    /// this machine wants it, and an update then has to be followed by an enable or
    /// it stays as invisible as it was.</para></summary>
    public sealed record ClaudePluginState(bool Enabled, string Version);

    /// <summary>One Claude MCP registration, as far as this needs to know it.
    ///
    /// <para>A registration whose <see cref="Scope" /> is not <c>user</c> belongs
    /// to a project or to a local override somebody made deliberately, and the
    /// right move on it is to report it and change nothing.</para></summary>
    public sealed record ClaudeMcpServerDetails(string Scope, string Command)
    {
        public bool IsUserScope => Scope.Contains("user", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One entry of <c>copilot plugin list</c>: an indented bullet, the
    /// plugin name, the marketplace it came from, its version and its enabled
    /// marker — the last four all optional, the first three all discarded.
    ///
    /// <para>Anchored at both ends, and a name has to carry a letter or a digit,
    /// so the section header and a rule of dashes fail to match rather than being
    /// filtered out by name afterwards. The regex it replaces matched the first
    /// word of any line at all, which is how "Installed" and "•" became plugins
    /// and every real one went missing.</para></summary>
    [GeneratedRegex(@"^\s*(?:[•\-\*]\s+)?(?<name>[^\s@]*[A-Za-z0-9][^\s@]*)(?:@\S+)?(?:\s+\(v?(?<version>[^)\s]+)\))?(?:\s+\[[^\]]*\])?\s*$")]
    private static partial Regex PluginListLineRegex();

    /// <summary>A plugin's home in GitHub: which repository, and where in it.
    ///
    /// <para><see cref="PluginPath" /> is null for a repository that is itself one
    /// plugin, which is why the manifest path is derived here instead of being
    /// stitched together at each call site.</para></summary>
    public sealed record PluginSource(string Owner, string Repository, string? PluginPath)
    {
        /// <summary>Forward slashes throughout: this addresses a GitHub API
        /// resource, not a file on the machine reading it.</summary>
        public string ManifestPath => string.IsNullOrWhiteSpace(PluginPath)
            ? ManifestFileName
            : $"{PluginPath.Trim('/')}/{ManifestFileName}";
    }
}
