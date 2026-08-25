using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    Marketplace,

    /// <summary>A piece of software this machine is supposed to have — a winget
    /// package, a VS Code extension, a setting the setup guide asks for.
    ///
    /// <para>The three kinds above each <em>are</em> their own mechanism: a plugin
    /// is what the host CLI installs, an MCP server is a .NET tool plus a
    /// registration. That held while every kind had exactly one mechanism and
    /// applications break it — a marketplace extension and a winget package are
    /// both "software this PC should have" and are installed two entirely
    /// different ways. So this is the one kind whose mechanism is declared rather
    /// than implied, in the entry's own <see cref="DevToolProvider"/>.</para></summary>
    Application
}

/// <summary>
/// What installs an <see cref="DevToolKind.Application"/> and what answers for it.
///
/// <para>Three mechanisms and a fourth that is the absence of one. A checklist row
/// is a <see cref="Command"/> entry that declined to say how to install itself,
/// rather than a kind of its own, so "detect-only" does not have to be handled in
/// every switch that already handles a provider.</para>
/// </summary>
public enum DevToolProvider
{
    /// <summary>A winget package, detected and installed by its exact package id.</summary>
    Winget,

    /// <summary>A VS Code extension, by its <c>publisher.name</c> marketplace id.</summary>
    VsCodeExtension,

    /// <summary>A command the entry itself declares. Its <c>detect</c> answers
    /// whether the machine already has it — by the version it prints, or by the
    /// substring <see cref="DevToolCommandSpec.Expect"/> names when it prints
    /// prose instead. An entry with no <c>install</c> is the checklist row: worth
    /// looking for, and not ours to press a button about.</summary>
    Command,

    /// <summary>Something with no honest automated answer at all — a sign-in, a
    /// menu item that renders, a first run without errors. It carries a per-machine
    /// acknowledgement instead of a probe, and must never be drawn as a detected
    /// state.
    ///
    /// <para>Also where an entry whose <c>provider</c> this version does not
    /// recognise lands. The safe reading of a mechanism nobody here knows is the
    /// one that runs nothing; <see cref="DevToolApplication.ProviderRecognised"/>
    /// is what keeps that from being silent.</para></summary>
    Manual
}

/// <summary>
/// A command an application entry declares, exactly as the catalog spells it.
///
/// <para><paramref name="Args"/> is a list and not a command line because the
/// launcher passes them through <c>ArgumentList</c>: a winget id with a literal
/// <c>+</c> in it and a path with a space both survive that, and neither survives
/// hand-quoting.</para>
///
/// <para><paramref name="Expect"/> is how a probe with no version answers. Several
/// real probes print prose rather than a number, and without a substring to look
/// for each of them would have to invent a fake version to avoid claiming "up to
/// date" about nothing.</para>
///
/// <para><paramref name="Shell"/> and <paramref name="Encoding"/> are two facts
/// about running the process that only the entry knows: some CLIs on PATH are
/// <c>.cmd</c> shims that cannot be started directly, and some write UTF-16LE to a
/// redirected pipe, where a UTF-8 reader gets every other byte as a null.</para>
/// </summary>
public sealed record DevToolCommandSpec(
    string Command,
    IReadOnlyList<string> Args,
    string? Expect = null,
    bool Shell = false,
    string? Encoding = null)
{
    /// <summary>What Windows is actually asked to start.
    ///
    /// <para><c>code</c> is the case this exists for. It resolves to
    /// <c>…\Microsoft VS Code\bin\code.cmd</c>, and a launcher that redirects its
    /// streams cannot use <c>UseShellExecute</c> — so starting it directly throws
    /// "not a valid application for this OS platform" before a single byte of
    /// output exists to explain why.</para></summary>
    public string FileName => Shell ? "cmd.exe" : Command;

    /// <summary>The arguments in the order the launcher hands them to
    /// <c>ArgumentList</c>, with the command itself folded in when it is being run
    /// through the shell.
    ///
    /// <para>A list and never a joined string: <c>cmd.exe</c> is handed <c>/c</c>,
    /// the command, and each argument as separate items, so a winget id with a
    /// literal <c>+</c> in it and a path with a space both arrive intact. Every
    /// hand-quoted form of this loses one of the two.</para></summary>
    public IReadOnlyList<string> LaunchArguments => Shell ? ["/c", Command, .. Args] : Args;

    /// <summary>How this command's redirected output has to be read.
    ///
    /// <para>UTF-8 unless the entry says otherwise, because that is what winget
    /// writes to a pipe whatever the console code page is. <c>wsl.exe</c> is the
    /// exception the field exists for: it writes UTF-16LE with no BOM, and a UTF-8
    /// reader turns <c>WSL</c> into <c>W\0S\0L\0</c> — output that parses as
    /// nothing at all rather than as an error anyone could see.</para></summary>
    public Encoding OutputEncoding => ReadEncoding(Encoding);

    /// <summary>Names an encoding, falling back to UTF-8 rather than throwing.
    /// The value comes out of a hand-edited catalog, and a typo in it should cost
    /// one command its non-default reader rather than take the whole listing down
    /// from inside a probe.</summary>
    private static Encoding ReadEncoding(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "utf-8" or "utf8" => System.Text.Encoding.UTF8,
        "utf-16" or "utf-16le" or "unicode" => System.Text.Encoding.Unicode,
        "utf-16be" or "bigendianunicode" => System.Text.Encoding.BigEndianUnicode,
        var other => NamedEncoding(other)
    };

    private static Encoding NamedEncoding(string name)
    {
        try
        {
            return System.Text.Encoding.GetEncoding(name);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return System.Text.Encoding.UTF8;
        }
    }
}

/// <summary>
/// The commands the automated providers run, spelled once.
///
/// <para>Here rather than in the desktop adapter that launches them, for the same
/// reason the stdout parsers are: an argument list is the half of a command
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
    /// Store.</para></summary>
    public static DevToolCommandSpec WingetInstall(string id) =>
        new("winget", [
            "install",
            "--id", id,
            "--exact",
            "--source", WingetSource,
            "--silent",
            "--accept-source-agreements",
            "--accept-package-agreements",
            "--disable-interactivity",
            "--nowarn"
        ]);

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

/// <summary>
/// One entry of the catalog's <c>applications</c> array, read into something a
/// describer can branch on without touching <see cref="JsonNode"/> again.
/// </summary>
public sealed record DevToolApplication(
    string Id,
    string Name,
    DevToolProvider Provider,
    bool Enabled)
{
    /// <summary>How the row is addressed, minted the same way every other kind's
    /// key is.</summary>
    public string Key => DevToolConfiguration.KeyFor(DevToolKind.Application, Id);

    /// <summary>A heading to file the row under. The setup guide this catalog
    /// follows is a sequence of steps, and thirty ungrouped rows lose that
    /// structure — so the grouping is a property of the entry rather than the
    /// array order, which nothing preserves once a person hand-edits the file.</summary>
    public string? Group { get; init; }

    /// <summary>Something the row has to say to the person reading it: an install
    /// that needs a reboot, a version that legitimately reads backwards.</summary>
    public string? Note { get; init; }

    /// <summary>The package manager knows this package and cannot install it
    /// unattended — an IDE whose workloads need an hour and an override, a suite
    /// whose activation is a sign-in. The row still reports what is installed; it
    /// just has no button.</summary>
    public bool DetectOnly { get; init; }

    /// <summary>An optional cross-check against what actually answers on PATH.
    /// The package manager reports what is registered, which is not the same
    /// question, and the two disagreeing is worth seeing rather than picking a
    /// winner between.</summary>
    public DevToolCommandSpec? Probe { get; init; }

    /// <summary>What answers whether this machine has it, for
    /// <see cref="DevToolProvider.Command"/>.</summary>
    public DevToolCommandSpec? Detect { get; init; }

    /// <summary>What puts it there, or nothing — which is what makes the row a
    /// checklist item.</summary>
    public DevToolCommandSpec? Install { get; init; }

    /// <summary>Whether the person said they had done it, for a
    /// <see cref="DevToolProvider.Manual"/> row. Per machine, so it lives in the
    /// per-PC override file rather than the shared catalog.</summary>
    public bool Acknowledged { get; init; }

    /// <summary>The <c>provider</c> string exactly as the catalog spelled it, or
    /// empty when it said nothing.</summary>
    public string DeclaredProvider { get; init; } = string.Empty;

    /// <summary>Whether <see cref="Provider"/> is what the entry asked for.
    ///
    /// <para>False means the entry named a mechanism this version has never heard
    /// of, and the row was parked on <see cref="DevToolProvider.Manual"/> so that
    /// nothing runs on its behalf. Dropping such an entry would hide a typo in a
    /// hand-edited file behind a row that simply is not there.</para></summary>
    /// <remarks>Defaults to true, because a caller that builds one of these named
    /// the mechanism as an enum value rather than as text — there was nothing to
    /// fail to recognise. Only the catalog reader ever says otherwise.</remarks>
    public bool ProviderRecognised { get; init; } = true;
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

    /// <summary>The Claude desktop app, which is a separate registration from the
    /// Claude CLI beside it: its own config file, its own server list, and a full
    /// restart before a change to either takes effect.</summary>
    ClaudeDesktop = 4,

    /// <summary>What an entry means by saying nothing.
    ///
    /// <para>Deliberately not every host. This is the value silence parses to, and
    /// every entry on every machine is silent — so folding
    /// <see cref="ClaudeDesktop"/> in here would make each of them claim a
    /// registration that was never made, and the pane would offer an Install for
    /// each. The desktop host is opt-in, by an entry naming it.</para></summary>
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

    /// <summary>Whether the person said they had done this, for a row nothing can
    /// probe.
    ///
    /// <para>It is not <see cref="Installed"/> under another name, and collapsing
    /// the two would be the lie the whole manual provider exists to avoid: nothing
    /// checked this machine, somebody ticked a box on it.</para></summary>
    public bool Acknowledged { get; init; }

    /// <summary>Whether there is a mechanism behind this row at all.
    ///
    /// <para>False is the checklist row and the row the package manager knows and
    /// cannot install unattended — "Dev Drive configured", an IDE whose workloads
    /// need an override and an hour. Both are worth reporting and neither has a
    /// button, and without this they were indistinguishable from a tool that is
    /// simply missing: the row offered an Install that had nothing to run and
    /// reported the nothing as a failure.</para>
    ///
    /// <para>Defaults to true, so every row that predates applications keeps the
    /// answer it always had.</para></summary>
    public bool Installable { get; init; } = true;

    public bool UpdateAvailable => HostStates.Count > 0
        ? HostStates.Any(state => VersionDiffers(state.InstalledVersion, state.AvailableVersion))
        : VersionDiffers(InstalledVersion, AvailableVersion);

    /// <summary>An update on <em>any</em> targeted host is an update to offer. One
    /// press acts on every host the entry targets, so a Claude plugin that is a
    /// version behind is worth a button even when the Copilot copy is current.</summary>
    public bool CanUpdate => Installable && ConfiguredEnabled && (HostStates.Count > 0
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
    public bool CanInstall => Installable && ConfiguredEnabled && (HostStates.Count > 0
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

    /// <summary>Whether a string in a version column is a version somebody
    /// looked up.
    ///
    /// <para><see cref="DevToolOutput.NoVersion"/> counts as no: it is what a row
    /// with nothing to compare puts there, and "up to date" said about a dash is
    /// a claim about nothing. A checklist row — "Dev Drive configured", answered
    /// by a substring in some prose — is exactly that shape, and it read as up to
    /// date whether or not the machine had done the thing.</para></summary>
    private static bool IsKnownVersion(string availableVersion) =>
        !string.IsNullOrWhiteSpace(availableVersion)
        && !availableVersion.Trim().Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase)
        && !availableVersion.Trim().Equals(DevToolOutput.NoVersion, StringComparison.Ordinal);

    /// <summary>
    /// Whether the available version is an update to the installed one.
    ///
    /// <para>It was a string inequality, and two real packages break that
    /// permanently: an MSIX app and a click-to-run suite each self-update on a
    /// channel of their own, so the version on the machine routinely reads
    /// <em>ahead</em> of the package manager's manifest. Both announced an update
    /// on every check, forever, and pressing it changed nothing.</para>
    ///
    /// <para>So where both sides are dotted numbers they are ordered component by
    /// component and only a genuinely newer available version counts. Where they
    /// are not — <c>2.54.0.windows.1</c>, <c>13.5.2+a22cec24</c> and a pair of
    /// short commit shas are all real contents of these two columns — nothing can
    /// be ordered and the answer falls back to the inequality this used to be.</para>
    /// </summary>
    public static bool VersionDiffers(string installedVersion, string availableVersion)
    {
        var installed = NormalizeVersion(installedVersion);
        var available = NormalizeVersion(availableVersion);

        if (installed is null || available is null)
        {
            return false;
        }

        if (string.Equals(installed, available, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CompareNumeric(installed, available) is not { } comparison || comparison > 0;
    }

    /// <summary>
    /// How the available version orders against the installed one, or nothing when
    /// one of them is not a number to begin with.
    ///
    /// <para>Both sides must carry a dot. A bare run of digits is not a version —
    /// it is what a repository-backed row puts in these columns, where an
    /// all-numeric short commit sorting lower than the local one is a pending
    /// update rather than a machine that is ahead.</para>
    ///
    /// <para>Missing trailing components are zero, so <c>1.2</c> and <c>1.2.0</c>
    /// are one version. The package manager prints both forms for the same
    /// package.</para>
    /// </summary>
    private static int? CompareNumeric(string installed, string available)
    {
        if (TryReadComponents(installed) is not { } installedParts || TryReadComponents(available) is not { } availableParts)
        {
            return null;
        }

        for (var index = 0; index < Math.Max(installedParts.Length, availableParts.Length); index++)
        {
            var installedPart = index < installedParts.Length ? installedParts[index] : 0;
            var availablePart = index < availableParts.Length ? availableParts[index] : 0;

            if (installedPart != availablePart)
            {
                return availablePart.CompareTo(installedPart);
            }
        }

        return 0;
    }

    /// <summary>Every component of a dotted number, or nothing if any one of them
    /// is not a number.
    ///
    /// <para>All of it or none of it: <c>2.55.0.windows.1</c> would otherwise be
    /// ordered against <c>2.54.0.windows.1</c> on the second component and never
    /// reach the word that says these are not numbers. Deciding on a prefix is how
    /// a comparison starts suppressing updates it does not actually
    /// understand.</para></summary>
    private static long[]? TryReadComponents(string version)
    {
        // A bare run of digits is not a version — it is what a repository-backed
        // row puts in these columns, and ordering two commit shas is meaningless.
        if (!version.Contains('.'))
        {
            return null;
        }

        var parts = version.Split('.');
        var components = new long[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            if (!long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out components[index]))
            {
                return null;
            }
        }

        return components;
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

    /// <summary>What installs the new application, for
    /// <see cref="DevToolKind.Application"/> and nothing else.
    ///
    /// <para>Unlike every other property here, this one is written even when it
    /// carries its default. The catalog's silence means "both hosts" for
    /// <c>hosts</c> and "no override" for the rest, but it means nothing at all
    /// for a mechanism — an entry that does not say how it is installed is an
    /// entry nothing can act on — so there is no default to leave out.</para></summary>
    public DevToolProvider Provider { get; init; } = DevToolProvider.Winget;

    /// <summary>What to run to find out whether this machine already has it, for
    /// a <see cref="DevToolProvider.Command"/> application.</summary>
    public string? DetectCommand { get; init; }

    /// <inheritdoc cref="ClaudeArgs" />
    public IReadOnlyList<string> DetectArgs { get; init; } = [];

    /// <summary>The substring that means "yes" when the detect command answers in
    /// prose rather than with a version.</summary>
    public string? DetectExpect { get; init; }

    /// <summary>What to run to put it there. Left blank on purpose for a checklist
    /// row: an entry with no install is one to look at rather than press.</summary>
    public string? InstallCommand { get; init; }

    /// <inheritdoc cref="ClaudeArgs" />
    public IReadOnlyList<string> InstallArgs { get; init; } = [];
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

    /// <summary>Where the machine's own software inventory lives, beside the two
    /// arrays that were only ever about AI tooling.</summary>
    public const string ApplicationsArrayName = "applications";

    /// <summary>What identifies an application entry. A winget id, an extension id
    /// or a slug — all of which contain dots and none of which contain a colon, so
    /// the <c>app:</c> prefix stays unambiguous.</summary>
    public const string ApplicationIdName = "id";

    /// <summary>
    /// The key a tool is addressed by, minted in one place so the prefixes
    /// <see cref="ParseKey"/> reads are the prefixes callers write.
    ///
    /// <para>Every kind is spelled out and there is no catch-all, which is the
    /// point: the arm this replaced minted an <c>mcp:</c> key for any kind it had
    /// not been told about, and <see cref="ParseKey"/> then resolved it against
    /// the wrong array — silently, and for exactly as long as it took somebody to
    /// add an enum member. A member added now fails to compile here instead.</para>
    /// </summary>
    public static string KeyFor(DevToolKind kind, string id) => kind switch
    {
        DevToolKind.Plugin => $"plugin:{id}",
        DevToolKind.McpServer => $"mcp:{id}",
        DevToolKind.Marketplace => $"marketplace:{id}",
        DevToolKind.Application => $"app:{id}"
    };

    /// <summary>The <c>provider</c> string a <see cref="DevToolProvider"/> is
    /// written as. Exhaustive for the same reason <see cref="KeyFor"/> is: a
    /// mechanism added here and not spelled out would be written as another
    /// mechanism's name.</summary>
    public static string ProviderName(DevToolProvider provider) => provider switch
    {
        DevToolProvider.Winget => "winget",
        DevToolProvider.VsCodeExtension => "vscode-extension",
        DevToolProvider.Command => "command",
        DevToolProvider.Manual => "manual"
    };

    /// <summary>
    /// Which mechanism a <c>provider</c> string names.
    ///
    /// <para>A catch-all here is right where <see cref="KeyFor"/>'s was wrong: the
    /// input is arbitrary text out of a hand-edited file rather than a value this
    /// code produced, and the safe reading of a mechanism nobody knows is the one
    /// that runs nothing. <see cref="DevToolApplication.ProviderRecognised"/>
    /// carries the fact that it happened.</para>
    /// </summary>
    public static DevToolProvider ParseProvider(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "winget" => DevToolProvider.Winget,
        "vscode-extension" => DevToolProvider.VsCodeExtension,
        "command" => DevToolProvider.Command,
        _ => DevToolProvider.Manual
    };

    /// <summary>
    /// The application entries a catalog declares, in the order it declares them.
    ///
    /// <para>An entry that cannot be read is skipped rather than thrown on. This
    /// array is the whole software inventory of a machine — thirty-odd rows in a
    /// file people hand-edit — and one mistyped entry taking the other twenty-nine
    /// off the screen would be a far worse answer than one missing row.</para>
    /// </summary>
    public static IReadOnlyList<DevToolApplication> ReadApplications(JsonNode? root)
    {
        if (FindArray(root, ApplicationsArrayName) is not { } array)
        {
            return [];
        }

        var applications = new List<DevToolApplication>();

        foreach (var node in array)
        {
            if (node is JsonObject entry && ReadApplication(entry) is { } application)
            {
                applications.Add(application);
            }
        }

        return applications;
    }

    /// <summary>One application entry, or nothing when it carries no id — the one
    /// property the import already refuses a catalog for going without, because it
    /// is what every override and every button on the row is addressed by.</summary>
    public static DevToolApplication? ReadApplication(JsonObject entry)
    {
        var id = GetString(entry, ApplicationIdName).Trim();
        if (id.Length == 0)
        {
            return null;
        }

        var name = GetString(entry, "name").Trim();
        var declaredProvider = GetString(entry, "provider").Trim();
        var provider = ParseProvider(declaredProvider);

        return new DevToolApplication(
            id,
            name.Length == 0 ? id : name,
            provider,
            GetBool(entry, "enabled"))
        {
            Group = GetOptionalString(entry, "group"),
            Note = GetOptionalString(entry, "note"),
            DetectOnly = GetBool(entry, "detectOnly"),
            Probe = ReadCommandSpec(entry["probe"]),
            Detect = ReadCommandSpec(entry["detect"]),
            Install = ReadCommandSpec(entry["install"]),
            Acknowledged = GetBool(entry, "acknowledged"),
            DeclaredProvider = declaredProvider,
            ProviderRecognised = ProviderName(provider).Equals(declaredProvider, StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>A declared command, or nothing when there is no command to run.
    /// An entry that named arguments and no executable is the same as an entry
    /// that named neither: there is nothing to launch.</summary>
    private static DevToolCommandSpec? ReadCommandSpec(JsonNode? node)
    {
        if (node is not JsonObject spec || GetString(spec, "command").Trim() is not { Length: > 0 } command)
        {
            return null;
        }

        var args = new List<string>();
        if (spec["args"] is JsonArray declared)
        {
            foreach (var argument in declared)
            {
                if (argument is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    args.Add(text);
                }
            }
        }

        return new DevToolCommandSpec(
            command,
            args,
            GetOptionalString(spec, "expect"),
            GetBool(spec, "shell"),
            GetOptionalString(spec, "encoding"));
    }

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
            // Spelled out for every kind, and with no catch-all, for the reason
            // KeyFor gives: the arm this replaced answered for any kind it had not
            // been told about, so a new one told the person to name a package id
            // for something that has none.
            throw new InvalidOperationException(draft.Kind switch
            {
                DevToolKind.Plugin => "A plugin needs a name.",
                DevToolKind.McpServer => "An MCP server needs a package id.",
                DevToolKind.Marketplace => "A marketplace needs a name.",
                DevToolKind.Application => "An application needs an id."
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

        // The other providers know how to find their own package. A command
        // application is the one that has to be told, and one that was not can
        // never answer whether the machine has it — so it is refused here rather
        // than written and drawn forever as a row of unknowns.
        if (draft.Kind is DevToolKind.Application
            && draft.Provider is DevToolProvider.Command
            && string.IsNullOrWhiteSpace(draft.DetectCommand))
        {
            throw new InvalidOperationException("A command application needs a detect command.");
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

            if (draft.Kind is DevToolKind.Application)
            {
                // No hosts property: an application is software this machine
                // should have, not a registration with an AI host, so the question
                // the hosts filter answers is not one this entry has.
                entry["provider"] = ProviderName(draft.Provider);
                WriteIfPresent(entry, "name", draft.DisplayName);

                if (CommandSpecFor(draft.DetectCommand, draft.DetectArgs, draft.DetectExpect) is { } detect)
                {
                    entry["detect"] = detect;
                }

                if (CommandSpecFor(draft.InstallCommand, draft.InstallArgs, expect: null) is { } install)
                {
                    entry["install"] = install;
                }

                entry["enabled"] = true;
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
        var applications = document[ApplicationsArrayName] as JsonArray;

        // Marketplaces count as content on their own. A machine that installs only
        // Claude plugins is set up by adding the marketplace first, and refusing
        // that catalog would mean the import could not be used to bootstrap the one
        // thing every Claude plugin id resolves against. Applications count for the
        // matching reason: a machine being set up has software to check before it
        // has a single plugin.
        if (plugins is null && servers is null && marketplaces is null && applications is null)
        {
            error = "A tool catalog needs a \"plugins\", an \"mcpServers\", a \"claude.marketplaces\" or an \"applications\" array.";
            return false;
        }

        // Applications are held to the same bar as the rest, which is why a
        // grouping marker in the catalog is a "group" property on a real entry
        // rather than an object of its own: an entry with no id has nothing for an
        // override to address and nothing for a button to act on.
        if (!EveryEntryCarriesAnId(plugins, "plugins", "name", out error)
            || !EveryEntryCarriesAnId(servers, "mcpServers", "packageId", out error)
            || !EveryEntryCarriesAnId(marketplaces, MarketplacesPath, "name", out error)
            || !EveryEntryCarriesAnId(applications, ApplicationsArrayName, ApplicationIdName, out error))
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

        // Applications merge for the same reason the two above do, and the cost of
        // forgetting is worse: an application override is where a machine says it
        // has acknowledged a manual row or does not want an app, and an array that
        // is not merged makes every one of those writes silently inert — a saved
        // setting that reads back as never saved.
        MergeArray(root, pcRoot, ApplicationsArrayName, ApplicationIdName);

        return new DevToolConfigurationDocument(root, true, paths.CatalogPath, paths.PcConfigPath);
    }

    public static Task WriteEnabledOverrideAsync(DevToolConfigurationPaths paths, string key, bool enabled, CancellationToken ct = default) =>
        WritePcOverrideAsync(paths, key, "enabled", enabled, ct);

    /// <summary>
    /// Records that the person has done what a manual row asks for.
    ///
    /// <para>Per machine, and in the per-PC file rather than the shared catalog,
    /// because that is what the fact is about: "this laptop is signed in" is not
    /// something to sync to the next one. It is deliberately a second property
    /// beside <c>enabled</c> and not a reuse of it — a row can be one the machine
    /// wants and has not done yet, and collapsing the two would make ticking the
    /// box the same act as removing the row.</para>
    /// </summary>
    public static Task WriteAcknowledgementAsync(DevToolConfigurationPaths paths, string key, bool acknowledged, CancellationToken ct = default) =>
        WritePcOverrideAsync(paths, key, "acknowledged", acknowledged, ct);

    /// <summary>Sets one property on one entry of the per-PC file, making the file,
    /// the array and the entry when they are not there yet. The file is small and
    /// rewritten whole, so both overrides go through here rather than each
    /// re-deriving where an entry lives.</summary>
    private static async Task WritePcOverrideAsync(DevToolConfigurationPaths paths, string key, string propertyName, bool value, CancellationToken ct)
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

        tool[propertyName] = value;

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

        // The desktop host is the one that has to be written down: it is outside
        // what silence means, so an entry that does not name it is an entry that
        // does not target it.
        if (hosts.HasFlag(DevToolHosts.ClaudeDesktop))
        {
            names.Add("claude-desktop");
        }

        entry["hosts"] = names;
    }

    /// <summary>A declared command as the catalog spells it, or nothing when there
    /// is no command — which is how a checklist row says it has no install: by the
    /// property not being there at all.</summary>
    private static JsonObject? CommandSpecFor(string? command, IReadOnlyList<string> args, string? expect)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var spec = new JsonObject { ["command"] = command.Trim() };
        var values = new JsonArray();

        foreach (var argument in args)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                values.Add(argument);
            }
        }

        if (values.Count > 0)
        {
            spec["args"] = values;
        }

        WriteIfPresent(spec, "expect", expect);

        return spec;
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

    /// <inheritdoc cref="GetString" />
    /// <summary>A string property that is absent when it is blank, so the record
    /// it lands on can say "nothing here" with a null rather than with an empty
    /// string every caller has to remember to test for.</summary>
    private static string? GetOptionalString(JsonObject node, string name) =>
        GetString(node, name).Trim() is { Length: > 0 } text ? text : null;

    /// <summary>A boolean property, or false. It reads the value the same way
    /// <see cref="GetString"/> does rather than demanding one: a
    /// <c>"enabled": "yes"</c> in a hand-edited catalog is a row that is not
    /// enabled, not an exception out of a read of the whole file.</summary>
    private static bool GetBool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

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

        if (key.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return (ApplicationsArrayName, ApplicationIdName, key["app:".Length..]);
        }

        throw new ArgumentException("Unknown tool key.", nameof(key));
    }

    /// <summary>
    /// Which kind a key addresses.
    ///
    /// <para>Here rather than as another chain of <c>StartsWith</c> at each call
    /// site, because the two chains that already existed were both written as a
    /// ternary whose <em>else</em> was "MCP server" — so a key with a prefix
    /// neither arm named was not rejected, it was quietly run as an MCP server
    /// against an entry that was not one. An <c>app:</c> key would have been the
    /// first to hit that.</para>
    ///
    /// <para>It throws on an unknown prefix for the same reason
    /// <see cref="ParseKey"/> does: a key is minted by <see cref="KeyFor"/> and
    /// nowhere else, so one that parses as nothing is a bug in this file rather
    /// than bad input from a user.</para>
    /// </summary>
    public static DevToolKind KindOf(string key)
    {
        if (key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase))
        {
            return DevToolKind.Plugin;
        }

        if (key.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            return DevToolKind.McpServer;
        }

        if (key.StartsWith("marketplace:", StringComparison.OrdinalIgnoreCase))
        {
            return DevToolKind.Marketplace;
        }

        if (key.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return DevToolKind.Application;
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

/// <summary>
/// The Claude desktop app's own MCP server list, which is a file and not a CLI.
///
/// <para>The MSIX package declares no execution alias, so nothing of Claude
/// Desktop's is ever on PATH, and the app's internal update channel can only
/// change a server that is already registered — it structurally cannot add one.
/// Editing <c>claude_desktop_config.json</c> is the whole mechanism.</para>
///
/// <para>Which makes this the one host whose registration is a document merge,
/// and the one whose failure mode is losing something. The file is the app's
/// entire settings store — preferences, the global shortcut, the Cowork files
/// path, feature switches — so a writer that serialises its own idea of the
/// document wipes all of it. Everything here reads the document it was given and
/// puts one property back.</para>
/// </summary>
public static class DevToolClaudeDesktopConfig
{
    /// <summary>The file every path candidate below ends in.</summary>
    public const string FileName = "claude_desktop_config.json";

    /// <summary>Where the servers live in it.</summary>
    public const string ServersPropertyName = "mcpServers";

    /// <summary>What has to happen before a change here is live.
    ///
    /// <para>The app reads this file once at startup and memoises it, with no
    /// watch on it. Closing the window is not enough — the process stays in the
    /// tray holding the old list — so a row that reported the server as running
    /// after a write would be wrong until the person happened to reboot.</para></summary>
    public const string RestartRequired = "Quit Claude Desktop from the tray and relaunch it for this to take effect.";

    /// <summary>
    /// Where the config might be, best first.
    ///
    /// <para>Three, because the app has moved: the roaming path is where a
    /// current install keeps it, the packaged <c>LocalCache</c> path is the MSIX
    /// redirection of that same roaming folder, and <c>Claude-Data</c> is the
    /// older layout. A machine that has been upgraded can have more than one of
    /// them on disk, so the order is the answer rather than a preference.</para>
    ///
    /// <para>Takes the two roots as arguments so the order can be checked without
    /// a Claude install to check it against.</para>
    /// </summary>
    public static IReadOnlyList<string> ConfigPathCandidates(string appDataRoaming, string appDataLocal) =>
    [
        Path.Combine(appDataRoaming, "Claude", FileName),
        Path.Combine(appDataLocal, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", FileName),
        Path.Combine(appDataLocal, "Claude-Data", FileName)
    ];

    /// <summary>
    /// What the config says one server is, or nothing when it says nothing.
    ///
    /// <para>An absent <c>mcpServers</c> is zero servers and not a broken file:
    /// the app omits the property entirely until the first server is registered,
    /// so every machine that has never used one reads this way.</para>
    /// </summary>
    public static DevToolClaudeDesktopServer? ReadServer(JsonNode? root, string name)
    {
        if (root?[ServersPropertyName] is not JsonObject servers || servers[name] is not JsonObject entry)
        {
            return null;
        }

        var command = entry["command"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
        var args = new List<string>();

        if (entry["args"] is JsonArray declared)
        {
            foreach (var argument in declared)
            {
                if (argument is JsonValue item && item.TryGetValue<string>(out var argumentText))
                {
                    args.Add(argumentText);
                }
            }
        }

        return new DevToolClaudeDesktopServer(command, args);
    }

    /// <summary>
    /// Puts one server into the document and hands the whole document back.
    ///
    /// <para>The document, not a new one built from the servers: every other key
    /// in it is the person's own settings, and the version of this that emitted
    /// <c>{"mcpServers": …}</c> would be indistinguishable from a correct one
    /// until somebody noticed their preferences had gone.</para>
    ///
    /// <para>Only <c>command</c>, <c>args</c> and <c>env</c> are written, and only
    /// when there is something to write. The app's own validator takes stdio
    /// servers and nothing else — no <c>type</c>, no <c>url</c>, no
    /// <c>transport</c> — and silently strips whatever else it finds, so a
    /// property added here would vanish without ever being reported.</para>
    /// </summary>
    public static JsonObject MergeServer(
        JsonObject root,
        string name,
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (root[ServersPropertyName] is not JsonObject servers)
        {
            servers = [];
            root[ServersPropertyName] = servers;
        }

        var entry = new JsonObject { ["command"] = command };

        if (args is { Count: > 0 })
        {
            var values = new JsonArray();
            foreach (var argument in args)
            {
                values.Add(argument);
            }

            entry["args"] = values;
        }

        if (env is { Count: > 0 })
        {
            var values = new JsonObject();
            foreach (var pair in env)
            {
                values[pair.Key] = pair.Value;
            }

            entry["env"] = values;
        }

        servers[name] = entry;

        return root;
    }

    /// <summary>
    /// Takes one server out of the document, leaving everything else exactly as
    /// it was — including an <c>mcpServers</c> that is now empty.
    ///
    /// <para>The empty object stays rather than being tidied away. It is the
    /// difference between "this machine has no servers registered" and "this
    /// machine has never registered one", and only one of those is a config a
    /// person has been editing.</para>
    /// </summary>
    public static JsonObject RemoveServer(JsonObject root, string name)
    {
        if (root[ServersPropertyName] is JsonObject servers)
        {
            servers.Remove(name);
        }

        return root;
    }
}

/// <summary>One entry of the Claude desktop app's server list, in the only shape
/// its own validator accepts: a command, and the arguments it is given. There is
/// no transport to read — the file holds stdio servers and nothing else.</summary>
public sealed record DevToolClaudeDesktopServer(string Command, IReadOnlyList<string> Args)
{
    /// <summary>The registration as one line, which is what the row compares
    /// against the catalog's own command. The arguments are part of it: a
    /// registration pointing at the right executable with the wrong arguments is
    /// a registration that does not work, and the command alone cannot say
    /// so.</summary>
    public string CommandLine => string.Join(' ', new[] { Command }.Concat(Args));
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
