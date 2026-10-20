using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>What the Domain view's Domain page read for one domain file: the file,
/// the context's domain and requirements files beside it, and the context's name —
/// or, with no file, why.</summary>
internal sealed record DomainViewDomainRead(
    DomainChapterFile? Page,
    IReadOnlyList<DomainChapterFile> DomainFiles,
    IReadOnlyList<DomainChapterFile> RequirementFiles,
    string ContextName,
    string? Problem)
{
    public static DomainViewDomainRead Failed(string problem) => new(null, [], [], "the context", problem);
}
