using System;
using System.Collections.Generic;
using System.Linq;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// Which mechanism answered a version column.
///
/// <para>It exists because two columns of one row were routinely two different
/// questions. A Claude plugin's Installed came out of Claude's own marketplace
/// clone and its Available out of a live read of the source repository's default
/// branch — two refs, so <c>claude-desktop</c> read 0.7.0 against 0.8.0 for as
/// long as the clone stayed behind, and pressing Update could not clear it: the
/// update installs from the clone, which is the side that said 0.7.0.</para>
///
/// <para>The rule this enables is one sentence: a row may only conclude something
/// from two columns that came from the same authority, and the authority is
/// whatever the row's own Update button installs from. It deliberately does not
/// mean "the same command" — <c>winget list</c> and <c>winget upgrade</c> are two
/// commands and one authority, and an extension's installed version and the
/// gallery's published one are two mechanisms and one authority, because the
/// gallery is what <c>--install-extension</c> pulls from.</para>
/// </summary>
public enum DevToolVersionAuthority
{
    /// <summary>What a caller that predates attribution means: nobody said.
    ///
    /// <para>The default, and never a third authority that disagrees with the
    /// other two. Every row built positionally — the harness, the unsupported
    /// service, every fixture — lands here and keeps comparing exactly as it
    /// always did.</para></summary>
    Unattributed = 0,

    /// <summary>Claude's marketplace: the clone <c>plugin marketplace update</c>
    /// pulls, which is what <c>plugin list --json</c> answers out of and what
    /// <c>plugin update</c> installs from.</summary>
    ClaudeMarketplace,

    /// <summary>The plugin's source repository at its default branch, which is
    /// what <c>copilot plugin install</c> and <c>plugin update</c> pull.</summary>
    CopilotSource,

    /// <summary>The git mirror this host clones and pulls itself, for the
    /// <c>repository-skills</c> and <c>repository-canvases</c> kinds.</summary>
    RepositoryMirror
}

/// <summary>
/// The source-refresh half of a listing: what has to be pulled before a version
/// column means anything, in what order, and what a column may say when the pull
/// did not happen.
///
/// <para>"Check for updates" re-read every source and refreshed none of them —
/// no <c>plugin marketplace update</c> before <c>plugin list --json</c>, no
/// <c>source update</c> before <c>winget list</c>, no <c>fetch</c> before a
/// repository row's two commits. So every column was as old as whichever cache
/// somebody last happened to write, and the button that was supposed to fix that
/// was the one that could not.</para>
///
/// <para>Here rather than in the desktop adapter that runs it, for the reason
/// <see cref="DevToolCommands"/> is: the ordering is the whole fix, and an
/// ordering is checkable without a machine to check it on.</para>
/// </summary>
public static class DevToolRefresh
{
    /// <summary>What the local clone is at.</summary>
    private const string LocalHead = "HEAD";

    /// <summary>What the fetch that just ran wrote, rather than a second network
    /// call. Two calls can answer about two different tips, and a row that
    /// compares one of those against the other is reporting the gap between two
    /// reads as a pending update.</summary>
    private const string FetchedHead = "FETCH_HEAD";

    /// <summary>
    /// Claude's plugins, in the order they have to be read.
    ///
    /// <para>The marketplace listing first because it names what there is to
    /// refresh, then one pull per marketplace, and only then the question every
    /// Claude row's Installed column is answered from. Asked in any other order
    /// it is a question about the last refresh somebody else happened to
    /// run.</para>
    /// </summary>
    public static IReadOnlyList<DevToolCommandSpec> ClaudePlugins(string cli, IEnumerable<string> marketplaces) =>
    [
        DevToolCommands.ClaudeMarketplaceList(cli),
        .. marketplaces
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => DevToolCommands.ClaudeMarketplaceUpdate(cli, name.Trim())),
        DevToolCommands.ClaudePluginList(cli)
    ];

    /// <summary>
    /// The machine's winget inventory, in the order it has to be read.
    ///
    /// <para>The source index is updated before either listing, because both of
    /// them answer out of it: a stale index reports a stale Available column for
    /// every package at once, and <c>winget upgrade</c> then omits the package
    /// that really does have one.</para>
    /// </summary>
    public static IReadOnlyList<DevToolCommandSpec> WingetInventory() =>
    [
        DevToolCommands.WingetVersion(),
        DevToolCommands.WingetSourceUpdate(),
        DevToolCommands.WingetList(),
        DevToolCommands.WingetUpgrade()
    ];

    /// <summary>
    /// One repository-backed row's two commits, in the order they have to be read.
    ///
    /// <para>The fetch first, and both commits scoped to
    /// <paramref name="artifactPath"/> when the entry declares one. That scope is
    /// the reported defect: <c>copilot-app-canvases</c> installs one folder out of
    /// a repository carrying twenty plugins, and the mirror's HEAD moves on every
    /// commit to any of them — so compared whole, the row announced an update
    /// whenever anything at all changed in that repository, which is how the
    /// machine came to "already have" the newer version.</para>
    /// </summary>
    public static IReadOnlyList<DevToolCommandSpec> Repository(string repoPath, string? artifactPath) =>
    [
        DevToolCommands.GitFetch(repoPath),
        InstalledCommit(repoPath, artifactPath),
        AvailableCommit(repoPath, artifactPath)
    ];

    /// <summary>The local half of <see cref="Repository"/>, for the caller that
    /// reads one side at a time.
    ///
    /// <para><paramref name="artifactPath"/> arrives in whichever form the catalog
    /// spelled it and is converted here, so the conversion happens once and both
    /// sides of a comparison are scoped by the same string.</para></summary>
    public static DevToolCommandSpec InstalledCommit(string repoPath, string? artifactPath) =>
        DevToolCommands.GitCommit(repoPath, LocalHead, ArtifactPath(artifactPath, null));

    /// <inheritdoc cref="InstalledCommit" />
    public static DevToolCommandSpec AvailableCommit(string repoPath, string? artifactPath) =>
        DevToolCommands.GitCommit(repoPath, FetchedHead, ArtifactPath(artifactPath, null));

    /// <summary>
    /// What a Claude row's Available column may say.
    ///
    /// <para>A published version only counts once the marketplace behind it has
    /// been pulled. Until then the number is about a ref Claude cannot install
    /// from, and reporting it is how a permanent update offer gets made — so the
    /// column says nothing was looked up instead, which the pane renders as
    /// "Version unknown" rather than as an agreement nobody checked.</para>
    /// </summary>
    public static string ClaudeAvailable(bool marketplaceRefreshed, string? publishedVersion) =>
        marketplaceRefreshed && !string.IsNullOrWhiteSpace(publishedVersion)
            ? publishedVersion.Trim()
            : DevToolOutput.Unknown;

    /// <summary>What one commit lookup answered, cut to the width the other side
    /// is cut to. A lookup that did not run or printed nothing is unknown rather
    /// than a sha somebody could compare.</summary>
    public static string RepositoryCommit(bool succeeded, string output) =>
        succeeded && FirstField(output) is { Length: > 0 } sha
            ? DevToolOutput.ShortCommit(sha)
            : DevToolOutput.Unknown;

    /// <summary>
    /// The subtree of the mirror a repository-backed row is versioned by, or
    /// nothing when the entry declares none.
    ///
    /// <para><c>extensionsPath</c> and <c>skillsPath</c> are the two catalog
    /// fields nothing read. Each of them says which folder of the repository this
    /// row's artifacts are copied out of, which is exactly the scope its version
    /// question has — and a row that reads neither is comparing a whole
    /// repository against itself.</para>
    ///
    /// <para>Returned with forward slashes: a backslash is an escape in a git
    /// pathspec rather than a separator, and the catalog is hand-written in
    /// Windows form.</para>
    /// </summary>
    public static string? ArtifactPath(string? extensionsPath, string? skillsPath)
    {
        var declared = string.IsNullOrWhiteSpace(extensionsPath) ? skillsPath : extensionsPath;

        return string.IsNullOrWhiteSpace(declared) ? null : declared.Trim().Replace('\\', '/');
    }

    /// <summary>
    /// What the row has to say about what it inspected.
    ///
    /// <para>A repository-backed row reports the local mirror, not the files
    /// copied out of it into <c>~/.copilot</c>: nothing here reads that
    /// destination, and two commits from a clone are not a claim about an install.
    /// Rather than let the version columns imply one, the row says which of the
    /// two it is reporting.</para>
    /// </summary>
    public static string MirrorNote(string? artifactPath) =>
        ArtifactPath(artifactPath, null) is { } path
            ? $"Versioned by the local source mirror at {path}, not by the copied artifacts."
            : "Versioned by the local source mirror, not by the copied artifacts.";

    /// <summary>The first whitespace-separated field of a command's output.
    /// <c>log -1 --format=%H</c> answers a bare sha and <c>ls-remote</c> answers
    /// a sha followed by a tab and a ref name, so one reader covers both.</summary>
    private static string FirstField(string output) =>
        output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : string.Empty;
}
