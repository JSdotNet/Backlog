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
public static partial class CopilotToolOutput
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
    public static CopilotPluginSource? ParsePluginSource(string? source)
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

    private static CopilotPluginSource? FromUrl(Uri uri)
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

        return new CopilotPluginSource(segments[0], TrimGitSuffix(segments[1]), path);
    }

    private static CopilotPluginSource? FromShorthand(string source)
    {
        var separator = source.IndexOf(':');
        var repository = separator < 0 ? source : source[..separator];
        var path = separator < 0 ? null : source[(separator + 1)..].Trim('/');
        var segments = repository.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 2
            ? new CopilotPluginSource(segments[0], TrimGitSuffix(segments[1]), string.IsNullOrWhiteSpace(path) ? null : path)
            : null;
    }

    private static string TrimGitSuffix(string repository) =>
        repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;

    private static IEnumerable<string> SplitLines(string value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnGapRegex();

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
    public sealed record CopilotPluginSource(string Owner, string Repository, string? PluginPath)
    {
        /// <summary>Forward slashes throughout: this addresses a GitHub API
        /// resource, not a file on the machine reading it.</summary>
        public string ManifestPath => string.IsNullOrWhiteSpace(PluginPath)
            ? ManifestFileName
            : $"{PluginPath.Trim('/')}/{ManifestFileName}";
    }
}
