namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// What the window the Devbook is shown in is scoped to, for a Devbook opened with
/// no <c>scope</c> of its own (local ADR 0041 §6). The scope chips are the shell's,
/// so the Devbook asks rather than reads them; the host answers from the window's
/// own view state. A host that registers none leaves such a Devbook on the Brain.
/// </summary>
public interface IDevbookWindowScope
{
    /// <summary>The repository alias the window's scope is anchored on, or the empty
    /// alias for the Brain — and for a window scoped to nothing, which the Devbook
    /// reads as the Brain too.</summary>
    string RepositoryAlias { get; }
}
