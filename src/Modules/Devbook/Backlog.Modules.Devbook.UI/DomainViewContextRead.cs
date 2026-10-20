using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>What the Domain view read for a Features or Requirements page: the
/// file the menu row names, every file of its context a reference may land in,
/// and the context's name — or, with no file, why.</summary>
public sealed record DomainViewContextRead(
    DomainChapterFile? Page,
    IReadOnlyList<DomainChapterFile> Files,
    string ContextName,
    string? Problem)
{
    public static DomainViewContextRead Failed(string problem) => new(null, [], "the context", problem);
}
