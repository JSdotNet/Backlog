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
    /// <summary>The context's Model file for the opened domain file
    /// (<see cref="ModelFor"/>), which the unit pages' "on Model" links open, or
    /// <see langword="null"/> when the context has none.</summary>
    public string? ModelPath { get; init; }

    /// <summary>The domain's name as its context map heads it — "Backlog" — which
    /// a unit's events say the rest of hears (<see cref="DomainNameOf"/>), or
    /// <see langword="null"/> for "the domain".</summary>
    public string? DomainName { get; init; }

    public static DomainViewDomainRead Failed(string problem) => new(null, [], [], [], "the context", problem);

    /// <summary>
    /// The Model file the opened domain file is built in: for a split
    /// <c>domain.&lt;name&gt;.md</c> the <c>model.&lt;name&gt;.md</c> beside it when
    /// there is one; otherwise, and for <c>domain.md</c>, the context's
    /// <c>model.md</c>, else its first Model file.
    /// </summary>
    public static string? ModelFor(DomainDevbookDocument page, IEnumerable<DomainDevbookDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(documents);

        var models = documents.Where(d => d.Kind == DomainDevbookDocumentKind.Model).ToList();
        var name = FileName(page.Path);
        if (name.StartsWith("domain.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "domain.md", StringComparison.OrdinalIgnoreCase)
            && models.FirstOrDefault(d => string.Equals(FileName(d.Path), "model." + name["domain.".Length..], StringComparison.OrdinalIgnoreCase)) is { } split)
        {
            return split.Path;
        }

        return (models.FirstOrDefault(d => string.Equals(FileName(d.Path), "model.md", StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault())?.Path;
    }

    /// <summary>The context map's heading as the domain's name, or
    /// <see langword="null"/> when there is none or it only names the map —
    /// "Context map", "Context Map: Backlog", or the file name a map with no
    /// heading is titled by, <c>context-map.md</c>.</summary>
    public static string? DomainNameOf(string? contextMapTitle)
    {
        var title = contextMapTitle?.Trim();
        return string.IsNullOrEmpty(title) || title.Replace('-', ' ').Contains("context map", StringComparison.OrdinalIgnoreCase) ? null : title;
    }

    private static string FileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }
}
