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

    /// <summary><c>APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND</c>, winget's way
    /// of saying it has no such package.
    ///
    /// <para>Written as the unsigned code winget documents rather than as the
    /// signed integer a process reports, because <c>-1978335212</c> on its own is
    /// unreadable and one digit away from meaning something else
    /// entirely.</para></summary>
    private const int WingetNoApplicationsFound = unchecked((int)0x8A150014);

    /// <summary>The headers winget prints, which are also the names the columns
    /// under them are addressed by. <c>Available</c> is absent from the header
    /// whenever nothing on the machine can be upgraded.</summary>
    private const string WingetNameColumn = "Name";

    /// <inheritdoc cref="WingetNameColumn" />
    private const string WingetIdColumn = "Id";

    /// <inheritdoc cref="WingetNameColumn" />
    private const string WingetVersionColumn = "Version";

    /// <inheritdoc cref="WingetNameColumn" />
    private const string WingetAvailableColumn = "Available";

    /// <inheritdoc cref="WingetNameColumn" />
    private const string WingetSourceColumn = "Source";

    /// <summary>The manifest property by which a published extension version
    /// declares itself a pre-release.</summary>
    private const string PreReleaseProperty = "Microsoft.VisualStudio.Code.PreRelease";

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
    ///
    /// <para><c>claude-desktop</c> is a host in its own right and is never part of
    /// what silence means — see <see cref="DevToolHosts.Both" />. An entry reaches
    /// the desktop app only by naming it.</para>
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
            // The desktop app before the CLI, and matched whole. One name starts
            // with the other, so anything reading them by prefix or by substring
            // would take claude-desktop for claude and register the server with
            // the host that was not asked for.
            else if (text.Equals("claude-desktop", StringComparison.OrdinalIgnoreCase))
            {
                hosts |= DevToolHosts.ClaudeDesktop;
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

    /// <summary>
    /// What <c>winget list</c> says is on this machine, by package id.
    ///
    /// <para>Read by column offset rather than by splitting on whitespace,
    /// because a real listing carries ids with spaces in them — a Steam title
    /// registers as <c>ARP\Machine\X64\Steam App 1086940</c> — and splitting one
    /// of those takes the last word for a version and shifts every remaining
    /// cell of that row.</para>
    ///
    /// <para>Rows arrive that this context has no interest in at all: an empty
    /// Source, a literal <c>Unknown</c> where a version should be, a synthetic
    /// <c>MSIX\</c> or <c>ARP\</c> id. They are read and returned rather than
    /// filtered, because which ids matter is the catalog's question and a filter
    /// here would answer it wrongly for the next entry somebody adds.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, WingetPackage> ParseWingetList(string output)
    {
        var packages = new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadWingetTable(output))
        {
            var id = row[WingetIdColumn];
            var available = row[WingetAvailableColumn];

            // The same package is registered twice on a real machine —
            // PowerShell appears as both the app and its versioned entry, and
            // both rows carry Microsoft.PowerShell. Adding rather than assigning
            // would throw and take the whole listing with it. The first row wins,
            // except that a later one may still be the only row carrying the
            // upgrade.
            if (packages.TryGetValue(id, out var existing))
            {
                if (existing.AvailableVersion is null && available.Length > 0)
                {
                    packages[id] = existing with { AvailableVersion = available };
                }

                continue;
            }

            packages[id] = new WingetPackage(
                id,
                row[WingetNameColumn],
                row[WingetVersionColumn],
                available.Length == 0 ? null : available,
                row[WingetSourceColumn]);
        }

        return packages;
    }

    /// <summary>What <c>winget upgrade</c> says can be upgraded, as id to the
    /// version it would move to.
    ///
    /// <para>Versions come back exactly as winget printed them. Two rows of one
    /// listing read <c>v1.0.65</c> and <c>2.55.0.3</c> — different prefixes and
    /// different component counts — and anything that normalised them would have
    /// to be able to turn them back into the strings the installer expects.</para>
    ///
    /// <para>The trailing <c>16 upgrades available.</c> is not a package. It falls
    /// out on its own rather than being matched by text, because it is far too
    /// short to reach the Id column — which is also what makes it survive winget
    /// wording it differently.</para></summary>
    public static IReadOnlyDictionary<string, string> ParseWingetUpgrade(string output)
    {
        var upgrades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadWingetTable(output))
        {
            var available = row[WingetAvailableColumn];
            if (available.Length > 0)
            {
                upgrades[row[WingetIdColumn]] = available;
            }
        }

        return upgrades;
    }

    /// <summary>The version <c>winget show</c> reports for a package, or
    /// <see langword="null" /> when it reported none.
    ///
    /// <para>Anchored to the start of a line, which is the whole trick: the
    /// installer block further down describes the same version and indents every
    /// one of its keys, so nothing below the header can be mistaken for the
    /// headline version.</para>
    ///
    /// <para>Null is the ordinary answer for a package winget does not know. It
    /// prints "No package found matching input criteria." to stdout and exits
    /// <see cref="IsWingetNotFound" />, so there is nothing here to
    /// throw about.</para></summary>
    public static string? ParseWingetShowVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = WingetShowVersionRegex().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>Whether an exit code is winget saying it has no such package.
    ///
    /// <para>Not installed is an exit code and nothing else: the sentence goes to
    /// stdout, stderr stays empty, and <c>winget list</c> and <c>winget show</c>
    /// both answer this way. Reading a non-zero exit as a failure would turn "this
    /// machine needs it" — the case the pane exists for — into a broken
    /// row.</para></summary>
    public static bool IsWingetNotFound(int exitCode) => exitCode == WingetNoApplicationsFound;

    /// <summary>What <c>code --list-extensions --show-versions</c> says is
    /// installed, by extension id.
    ///
    /// <para>Case-insensitive because the CLI lowercases what it prints: the
    /// marketplace, and therefore the catalog, spells it
    /// <c>ms-vscode.PowerShell</c> and the CLI answers
    /// <c>ms-vscode.powershell</c>. An ordinal lookup finds neither in the
    /// other and every extension reads as missing.</para>
    ///
    /// <para>A line with no <c>@</c> is what the CLI prints without
    /// <c>--show-versions</c>. The extension is there; only its version is not,
    /// which is <see cref="Installed" /> rather than absent.</para></summary>
    public static IReadOnlyDictionary<string, string> ParseVsCodeExtensionList(string output)
    {
        var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();

            // An extension id has no spaces in it, so anything with one is a
            // sentence the CLI decided to print rather than a row.
            if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
            {
                continue;
            }

            var separator = trimmed.IndexOf('@');
            if (separator == 0)
            {
                continue;
            }

            var id = separator < 0 ? trimmed : trimmed[..separator];
            var version = separator < 0 ? string.Empty : trimmed[(separator + 1)..];

            extensions[id] = version.Length == 0 ? Installed : version;
        }

        return extensions;
    }

    /// <summary>
    /// The latest published version of each extension in a marketplace
    /// <c>extensionquery</c> response, by <c>publisher.name</c> id.
    ///
    /// <para>Keyed case-insensitively for the same reason
    /// <see cref="ParseVsCodeExtensionList" /> is: the API answers in the
    /// publisher's own casing and the CLI this is compared against does not.</para>
    ///
    /// <para>A pre-release is skipped rather than reported. The marketplace
    /// answers with the absolute newest build, and for two of the three
    /// extensions asked for here that is a pre-release — PowerShell's four newest
    /// are, while the stable channel sits at 2025.4.0, which is what
    /// <c>code --install-extension</c> installs. Reporting the pre-release would
    /// offer an update that the install can never deliver, on every refresh,
    /// forever. An extension with no stable version in the response is left out
    /// entirely, so its row reads "version unknown" instead.</para>
    ///
    /// <para>Never throws, for the same reason
    /// <see cref="ParseClaudePluginList" /> does not: this is one column of a
    /// listing, and an HTTP error page in place of JSON should cost that column
    /// rather than the pane.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseMarketplaceExtensionVersions(string json)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in EnumerateJsonArray(json))
        {
            if (result.ValueKind is not JsonValueKind.Object
                || !result.TryGetProperty("extensions", out var extensions)
                || extensions.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var extension in extensions.EnumerateArray())
            {
                if (extension.ValueKind is not JsonValueKind.Object
                    || !extension.TryGetProperty("publisher", out var publisher)
                    || publisher.ValueKind is not JsonValueKind.Object
                    || ReadString(publisher, "publisherName") is not { Length: > 0 } publisherName
                    || ReadString(extension, "extensionName") is not { Length: > 0 } extensionName
                    || LatestStableVersion(extension) is not { } version)
                {
                    continue;
                }

                versions[$"{publisherName}.{extensionName}"] = version;
            }
        }

        return versions;
    }

    /// <summary>
    /// The version a tool printed when asked for one, or <see langword="null" />
    /// when it printed none.
    ///
    /// <para>One reader for all of them rather than one per tool. Eight probes
    /// say it eight ways — <c>13.5.2+a22cec2…</c>, <c>PowerShell 7.6.5</c>,
    /// <c>gh version 2.95.0 (2026-06-17)</c>, <c>Docker version 29.5.3, build
    /// d1c06ef</c>, <c>WSL version: 2.7.10.0</c>, a bare <c>10.0.400</c> — and
    /// every one of them is the first dotted number on the line. Eight regexes
    /// would be eight things to keep true about tools that release without
    /// asking.</para>
    ///
    /// <para>Only the first non-empty line is read, so the release URL
    /// <c>gh --version</c> prints underneath and the commit sha and architecture
    /// under <c>code --version</c> cannot be mistaken for the answer.</para>
    ///
    /// <para>What comes back is a token, not a <see cref="Version" />:
    /// <c>git version 2.54.0.windows.1</c> has four components and a word in it
    /// and parses as no version at all, and cutting it to three would name a build
    /// this machine does not have.</para>
    ///
    /// <para>Encoding is the caller's problem, not this one's — <c>wsl.exe</c>
    /// writes UTF-16LE with no BOM, and a UTF-8 reader hands this
    /// <c>W\0S\0L\0</c>.</para>
    /// </summary>
    public static string? ParseVersionProbe(string output)
    {
        foreach (var line in SplitLines(output))
        {
            var match = VersionTokenRegex().Match(line);

            return match.Success ? match.Value : null;
        }

        return null;
    }

    /// <summary>The newest version of one marketplace extension that is not a
    /// pre-release, or <see langword="null" /> when the response carries
    /// none.</summary>
    private static string? LatestStableVersion(JsonElement extension)
    {
        if (!extension.TryGetProperty("versions", out var versions) || versions.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var version in versions.EnumerateArray())
        {
            if (version.ValueKind is JsonValueKind.Object
                && !IsPreRelease(version)
                && ReadString(version, "version") is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Whether one published version is a pre-release, as the extension's
    /// own manifest properties declare it.</summary>
    private static bool IsPreRelease(JsonElement version)
    {
        if (!version.TryGetProperty("properties", out var properties) || properties.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        foreach (var property in properties.EnumerateArray())
        {
            if (property.ValueKind is JsonValueKind.Object
                && ReadString(property, "key").Equals(PreReleaseProperty, StringComparison.OrdinalIgnoreCase)
                && ReadString(property, "value").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The rows of whichever table winget printed, each addressable by the name of
    /// the column above it.
    ///
    /// <para>Offsets come from this invocation's header and nowhere else. winget
    /// has no JSON and sizes every column to the widest cell it is about to print,
    /// so <c>Name</c> is four characters wide for a one-row listing and sixty-one
    /// for the whole machine — and, worse, <c>Available</c> is printed only when
    /// something can actually be upgraded, which moves <c>Source</c> as well.
    /// Anything hard-coded here is right until the day the machine changes.</para>
    ///
    /// <para>A row has to reach the Id column with whitespace in front of it and
    /// carry both an id and a version. That is what keeps prose out: the summary
    /// line, and the sentence winget prints before its second table, are not
    /// recognised by their wording — which it is free to change — but by not being
    /// shaped like a row. A second header re-reads the offsets rather than ending
    /// the table, because that second table is packages that need explicit
    /// targeting and they belong in the same answer.</para>
    /// </summary>
    private static IEnumerable<WingetRow> ReadWingetTable(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        string[]? headers = null;
        int[]? offsets = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || IsRuleLine(line))
            {
                continue;
            }

            var tokens = ReadColumns(line);
            if (tokens.Names.Contains(WingetIdColumn) && tokens.Names.Contains(WingetVersionColumn))
            {
                headers = tokens.Names;
                offsets = tokens.Offsets;
                continue;
            }

            if (headers is null || offsets is null)
            {
                continue;
            }

            var cells = SliceRow(line, offsets);
            if (cells is null)
            {
                continue;
            }

            var row = new WingetRow(headers, cells);
            if (row[WingetIdColumn].Length > 0 && row[WingetVersionColumn].Length > 0)
            {
                yield return row;
            }
        }
    }

    /// <summary>Where each header word starts, which is where the column under it
    /// starts too — winget left-aligns every cell to its header.</summary>
    private static (string[] Names, int[] Offsets) ReadColumns(string header)
    {
        var names = new List<string>();
        var offsets = new List<int>();

        for (var index = 0; index < header.Length;)
        {
            if (char.IsWhiteSpace(header[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < header.Length && !char.IsWhiteSpace(header[index]))
            {
                index++;
            }

            names.Add(header[start..index]);
            offsets.Add(start);
        }

        return ([.. names], [.. offsets]);
    }

    /// <summary>One line cut at the column offsets, or <see langword="null" /> when
    /// it is not a row at all.
    ///
    /// <para>Every cell is padded to at least one space wider than its widest
    /// value, so a column always has whitespace immediately before it. A line that
    /// has a letter there is prose long enough to reach into the table — which is
    /// exactly what winget prints between its two tables.</para></summary>
    private static string[]? SliceRow(string line, int[] offsets)
    {
        var cells = new string[offsets.Length];

        for (var column = 0; column < offsets.Length; column++)
        {
            var start = offsets[column];
            if (start >= line.Length)
            {
                cells[column] = string.Empty;
                continue;
            }

            if (start > 0 && !char.IsWhiteSpace(line[start - 1]))
            {
                return null;
            }

            var end = column + 1 < offsets.Length ? Math.Min(offsets[column + 1], line.Length) : line.Length;
            cells[column] = line[start..end].Trim();
        }

        return cells;
    }

    /// <summary>The rule of dashes winget draws under every header.</summary>
    private static bool IsRuleLine(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Length > 0 && trimmed.All(character => character == '-');
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

    /// <summary>The version <c>winget show</c> leads with. Anchored hard at the
    /// start of a line — no leading whitespace allowed — because the installer
    /// block below indents everything it says about the same version, and this has
    /// to be the manifest's version rather than the installer's.</summary>
    [GeneratedRegex(@"^Version:\s*(?<value>.+)$", RegexOptions.Multiline)]
    private static partial Regex WingetShowVersionRegex();

    /// <summary>A version as a tool prints it: a number, then at least one more
    /// dot-separated part.
    ///
    /// <para>Letters and hyphens are part of a component, which is what keeps
    /// <c>2.54.0.windows.1</c> and <c>10.0.100-preview.5</c> whole. The build
    /// metadata <c>aspire</c> appends after a <c>+</c>, the comma and build id
    /// <c>docker</c> appends, and the release date <c>gh</c> appends all fall
    /// outside a component and end the match on their own.</para>
    ///
    /// <para>A leading <c>v</c> is left out of the match rather than rejected:
    /// tools print it inconsistently, and the version is the same version with or
    /// without it.</para></summary>
    [GeneratedRegex(@"\d+(?:\.[0-9A-Za-z-]+)+")]
    private static partial Regex VersionTokenRegex();

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

    /// <summary>One row of a winget listing.
    ///
    /// <para><see cref="AvailableVersion" /> is null rather than empty when winget
    /// printed no <c>Available</c> column at all, which it does whenever nothing
    /// can be upgraded. Null is "there is no newer version"; an empty string would
    /// read as a lookup that came back with nothing.</para>
    ///
    /// <para><see cref="InstalledVersion" /> is whatever winget printed, up to and
    /// including the literal <c>Unknown</c> it prints for a package registered
    /// without one. Deciding what that means is the caller's, not this
    /// one's.</para></summary>
    public sealed record WingetPackage(string Id, string Name, string InstalledVersion, string? AvailableVersion, string Source);

    /// <summary>One sliced row, addressed by the header above each cell. A column
    /// the header did not carry reads as empty rather than throwing, because
    /// <c>Available</c> genuinely is not always there.</summary>
    private sealed class WingetRow(string[] headers, string[] cells)
    {
        public string this[string column]
        {
            get
            {
                var index = Array.IndexOf(headers, column);

                return index < 0 || index >= cells.Length ? string.Empty : cells[index];
            }
        }
    }

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
