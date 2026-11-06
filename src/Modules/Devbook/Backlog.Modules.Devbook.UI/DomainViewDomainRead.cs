using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>What the Domain view's Domain page read for one domain file: the file,
/// the context's domain, requirements and invariants files beside it, and the context's name —
/// or, with no file, why.</summary>
internal sealed record DomainViewDomainRead(
    DomainChapterFile? Page,
    IReadOnlyList<DomainChapterFile> DomainFiles,
    IReadOnlyList<DomainChapterFile> RequirementFiles,
    IReadOnlyList<DomainChapterFile> InvariantFiles,
    string ContextName,
    string? Problem)
{
    /// <summary>The context's Model file, which the unit pages' "on Model" links
    /// open, or <see langword="null"/> when the context has none.</summary>
    public string? ModelPath { get; init; }

    public static DomainViewDomainRead Failed(string problem) => new(null, [], [], [], "the context", problem);
}
