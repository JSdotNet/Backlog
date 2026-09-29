using System.Text.Json.Nodes;
using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.DevPc;

/// <summary>
/// The commands the automated providers run, spelled once.
///
/// <para>Its own type rather than private to the adapter that launches them, for
/// the same reason the stdout parsers are: an argument list is the half of a command
/// surface that can be checked without a machine to run it on, and every trap
/// these carry — <c>--silent</c> is <c>-h</c> while <c>-s</c> is <c>--source</c>,
/// the msstore copy of a package needs a signed-in Store account — is a trap that
/// shows up in a diff and never in a suite that mocked the process away.</para>
/// </summary>
public static class DevToolCommands
{
    /// <summary>Which source a package is taken from, always named.
    ///
    /// <para>Left unsaid, winget may resolve an id to its <c>msstore</c> twin,
    /// which needs a signed-in Store account and fails <c>0x8A150044</c> on a
    /// machine that has none — an install that reads as broken tooling rather than
    /// as the account it is really about.</para></summary>
    private const string WingetSource = "winget";

    /// <summary>What every winget call carries: no prompts, and the source
    /// agreement accepted up front. Without them a first run on a fresh machine
    /// blocks on a y/n nobody can see, behind a redirected pipe, until the
    /// timeout.</summary>
    private static readonly string[] WingetQuiet = ["--disable-interactivity", "--accept-source-agreements"];

    public static DevToolCommandSpec WingetVersion() => new("winget", ["--version"]);

    /// <summary>Everything installed, in one call.
    ///
    /// <para>Unfiltered on purpose. A <c>--id</c> per catalog row would be one
    /// process launch per row and thirty of them per refresh; the whole table is
    /// read once and the rows are matched against it in memory.</para></summary>
    public static DevToolCommandSpec WingetList() => new("winget", ["list", .. WingetQuiet]);

    /// <summary>Everything with a newer version, in one call.
    ///
    /// <para><c>--include-unknown</c> because a package whose installed version
    /// winget cannot read drops out of this listing entirely without it — and
    /// those are exactly the MSIX and click-to-run entries that most often have
    /// one.</para></summary>
    public static DevToolCommandSpec WingetUpgrade() => new("winget", ["upgrade", "--include-unknown", .. WingetQuiet]);

    /// <summary>What one package's manifest publishes. The per-row fallback, for a
    /// package the two batched listings could not answer for — which is a package
    /// this machine does not have.</summary>
    public static DevToolCommandSpec WingetShow(string id) =>
        new("winget", ["show", "--id", id, "--exact", "--source", WingetSource, .. WingetQuiet]);

    /// <summary>An unattended install of one package.
    ///
    /// <para><c>--silent</c> is spelled out because its short form is <c>-h</c>:
    /// the <c>-s</c> a reader expects is <c>--source</c>, and the two are one
    /// keystroke apart in a line where the wrong one installs from the Microsoft
    /// Store.</para>
    ///
    /// <para><paramref name="installerType"/> is only emitted when the catalog
    /// pinned one, so every entry that pinned nothing runs the line it has always
    /// run. Passed through unread — see
    /// <see cref="DevToolApplication.InstallerType"/> for why the vocabulary is
    /// winget's rather than this app's.</para></summary>
    public static DevToolCommandSpec WingetInstall(string id, string? installerType = null)
    {
        // Two argv elements or none. A blank pin is treated as no pin rather than
        // spelled out with an empty value behind it, which winget rejects.
        string[] installer = string.IsNullOrWhiteSpace(installerType)
            ? []
            : ["--installer-type", installerType.Trim()];

        return new("winget", [
            "install",
            "--id", id,
            "--exact",
            "--source", WingetSource,
            .. installer,
            "--silent",
            "--accept-source-agreements",
            "--accept-package-agreements",
            "--disable-interactivity",
            "--nowarn"
        ]);
    }

    /// <summary>
    /// The local source index, pulled before either listing reads it.
    ///
    /// <para>Both batched listings answer out of that index and nothing was ever
    /// refreshing it, so a machine whose index predated a package's new manifest
    /// reported the old version in the Available column and omitted the package
    /// from <c>winget upgrade</c> entirely — a row that read as current about a
    /// package that was not.</para>
    ///
    /// <para>Named to the same source every other call here names, because
    /// updating every configured source is a per-source network round trip for
    /// answers no row asks about.</para>
    ///
    /// <para>Spelled out rather than spread from <see cref="WingetQuiet"/>:
    /// <c>source update</c> takes only <c>--wait</c>, <c>--logs</c>,
    /// <c>--verbose</c>, <c>--nowarn</c>, <c>--disable-interactivity</c>,
    /// <c>--proxy</c> and <c>--no-proxy</c>. The
    /// <c>--accept-source-agreements</c> that every other winget call here needs
    /// is rejected outright by this one — argument parsing fails before the pull
    /// is attempted, which would fail the refresh on every run.</para>
    /// </summary>
    public static DevToolCommandSpec WingetSourceUpdate() =>
        new("winget", ["source", "update", "--name", WingetSource, "--disable-interactivity"]);

    /// <inheritdoc cref="DevToolCommandSpec.FileName" />
    public static DevToolCommandSpec VsCodeVersion() => new("code", ["--version"], Shell: true);

    /// <summary>Every installed extension and its version in one call — the whole
    /// inventory for every extension row in the catalog.</summary>
    public static DevToolCommandSpec VsCodeExtensionList() =>
        new("code", ["--list-extensions", "--show-versions"], Shell: true);

    /// <summary>Installs or updates one extension. An extension that is already
    /// there exits 0 with a sentence saying so, which is why the caller does not
    /// have to know which of the two it is doing.</summary>
    public static DevToolCommandSpec VsCodeInstallExtension(string id) =>
        new("code", ["--install-extension", id], Shell: true);

    /// <summary>Which marketplaces Claude knows, which is what names the ones a
    /// refresh has anything to pull.</summary>
    public static DevToolCommandSpec ClaudeMarketplaceList(string cli) =>
        new(cli, ["plugin", "marketplace", "list", "--json"]);

    /// <summary>
    /// One marketplace pulled to its remote tip.
    ///
    /// <para>The missing step behind the reported defect. Claude answers
    /// <see cref="ClaudePluginList"/> out of its own clone of a marketplace and
    /// installs out of that same clone, so a listing that never pulled it is a
    /// listing about whatever the clone happened to hold — and an update offered
    /// against a newer published version could not be delivered by the very
    /// button that offered it.</para>
    /// </summary>
    public static DevToolCommandSpec ClaudeMarketplaceUpdate(string cli, string name) =>
        new(cli, ["plugin", "marketplace", "update", name]);

    /// <summary>What Claude has installed, at any scope, read after every
    /// marketplace behind it has been pulled.</summary>
    public static DevToolCommandSpec ClaudePluginList(string cli) =>
        new(cli, ["plugin", "list", "--json"]);

    /// <summary>
    /// Registering a server that is reached over HTTP rather than started.
    /// </summary>
    /// <remarks>
    /// <para>There is no <c>--</c> anywhere in this vector, and that is the trap
    /// it is written down to avoid. The <c>--</c> in the stdio call beside it
    /// belongs to stdio: it separates the flags from the command and the
    /// arguments that command is to be started with. Put one before a URL and the
    /// CLI reads the URL as a command to run — the add succeeds, and registers a
    /// server that can never answer.</para>
    ///
    /// <para>Checked against <c>claude mcp add --help</c>: <c>-t/--transport</c>
    /// takes <c>stdio|sse|http</c>, <c>-s/--scope</c> takes
    /// <c>local|user|project</c>, and <c>-H/--header</c> repeats. The long forms
    /// are spelled out for the reason <c>--silent</c> is in
    /// <see cref="WingetInstall"/>: a short flag in a line nobody reads twice is
    /// how the wrong one gets typed.</para>
    ///
    /// <para>User scope, matching the stdio registration this sits beside: a
    /// machine-wide tool belongs to the machine, and anything narrower is a
    /// registration somebody made for one project and did not ask us to
    /// touch.</para>
    ///
    /// <para><paramref name="headers"/> are passed through unread and in order.
    /// Whatever expansion a value needed happened before this was called — this
    /// builds a command line and decides nothing about what goes on it.</para>
    /// </remarks>
    public static DevToolCommandSpec ClaudeMcpAddHttp(
        string cli,
        string name,
        string url,
        IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        // Two argv elements per header, never one joined string: the value
        // carries a space after the colon, and every hand-quoted form of that
        // loses either the space or the quoting.
        var flags = new List<string>();
        foreach (var (header, value) in headers)
        {
            flags.Add("--header");
            flags.Add($"{header}: {value}");
        }

        return new(cli, ["mcp", "add", "--transport", HttpTransportName, "--scope", UserScopeName, name, url, .. flags]);
    }

    /// <summary>The transport <c>claude mcp add</c> is told to use, which is the
    /// same word the catalog's <c>type</c> uses for it.</summary>
    private const string HttpTransportName = "http";

    /// <summary>The scope every registration this app makes is made at: the
    /// machine's, because a catalog is about a machine.</summary>
    private const string UserScopeName = "user";

    /// <summary>
    /// One mirror brought up to date with its remote, without touching the
    /// working tree.
    ///
    /// <para>A fetch and not a pull: this runs inside a listing, and a listing
    /// that moved the checkout would be changing the machine while reporting on
    /// it. What the fetch leaves behind is <c>FETCH_HEAD</c>, which is the ref
    /// <see cref="GitCommit"/> reads the available side out of.</para>
    /// </summary>
    public static DevToolCommandSpec GitFetch(string repoPath) =>
        new("git", ["-C", repoPath, "fetch", "origin", "--quiet"]);

    /// <summary>
    /// The newest commit of one revision, optionally scoped to one subtree.
    ///
    /// <para>The scope is what makes a repository-backed row's two versions about
    /// the row. Such an entry installs one folder out of a repository that carries
    /// twenty of them, and unscoped the comparison moved every time anything at
    /// all in that repository did — a pending update on a row whose artifacts had
    /// not changed since the last one.</para>
    ///
    /// <para><paramref name="artifactPath"/> is expected in git's own form,
    /// forward slashes and all: a backslash in a pathspec is an escape rather than
    /// a separator. <see cref="DevToolRefresh.ArtifactPath"/> is what converts the
    /// catalog's Windows spelling into it.</para>
    /// </summary>
    public static DevToolCommandSpec GitCommit(string repoPath, string revision, string? artifactPath) =>
        new("git", string.IsNullOrWhiteSpace(artifactPath)
            ? ["-C", repoPath, "log", "-1", "--format=%H", revision]
            : ["-C", repoPath, "log", "-1", "--format=%H", revision, "--", artifactPath]);

    /// <summary>Where the marketplace answers what the CLI cannot: an extension's
    /// latest published version. There is no <c>code --list-outdated</c>, no
    /// <c>--check-updates</c> and no JSON output of any kind.</summary>
    public const string MarketplaceQueryUrl = "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery";

    /// <summary>The Accept header the gallery answers JSON to. Without the
    /// <c>api-version</c> in it the endpoint replies with a page rather than a
    /// document.</summary>
    public const string MarketplaceQueryAccept = "application/json;api-version=7.2-preview.1";

    /// <summary>
    /// One query for every extension in the catalog.
    ///
    /// <para><c>filterType 7</c> is an exact <c>publisher.name</c> match, and
    /// several criteria go in one filter — so the whole Available column costs one
    /// HTTP call rather than one per row.</para>
    ///
    /// <para><c>flags</c> is 402 and deliberately not 914. The extra bit in 914 is
    /// <c>IncludeLatestVersionOnly</c>, which answers with the newest build of any
    /// channel: <c>ms-vscode.PowerShell</c> replies with a pre-release while the
    /// stable channel — the one <c>code --install-extension</c> actually installs —
    /// sits several versions below it. That is a permanent update offer for a
    /// version the install can never deliver.</para>
    /// </summary>
    public static string MarketplaceExtensionQuery(IEnumerable<string> ids)
    {
        var criteria = new JsonArray();
        var count = 0;

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            criteria.Add(new JsonObject { ["filterType"] = 7, ["value"] = id.Trim() });
            count++;
        }

        var query = new JsonObject
        {
            ["filters"] = new JsonArray
            {
                new JsonObject
                {
                    ["criteria"] = criteria,
                    ["pageNumber"] = 1,
                    ["pageSize"] = Math.Max(count, 1)
                }
            },
            ["flags"] = 402
        };

        return query.ToJsonString();
    }
}
