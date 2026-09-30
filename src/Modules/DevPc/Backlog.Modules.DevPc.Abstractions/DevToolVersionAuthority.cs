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
