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

    /// <summary>Every Model file of the context (<see cref="ModelPathsOf"/>), which
    /// the Glossary pairs each row's own domain file with.</summary>
    public IReadOnlyList<string> ModelPaths { get; init; } = [];

    /// <summary>The domain's name as its context map heads it — "Backlog" — which
    /// a unit's events say the rest of hears (<see cref="DomainNameOf"/>), or
    /// <see langword="null"/> for "the domain".</summary>
    public string? DomainName { get; init; }

    public static DomainViewDomainRead Failed(string problem) => new(null, [], [], [], "the context", problem);

    /// <summary>
    /// The Model file the opened domain file is built in, by the pairing
    /// <see cref="DomainUnits.ModelFileFor"/> applies — the Glossary's "Open the
    /// model" uses the same one: by name first, then the file's <c>related</c> at
    /// every level, then the context's <c>model.md</c>, else its first Model file.
    /// </summary>
    /// <param name="page">The opened domain file.</param>
    /// <param name="documents">The context's documents.</param>
    /// <param name="parsed">The opened file as read, whose <c>related</c> entries
    /// decide when no name matches. Unset, they are not consulted.</param>
    public static string? ModelFor(DomainDevbookDocument page, IEnumerable<DomainDevbookDocument> documents, DomainChapterFile? parsed = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(documents);

        return DomainUnits.ModelFileFor(page.Path, ModelPathsOf(documents), parsed);
    }

    /// <summary>The context's Model files, in the order the context lists them.</summary>
    public static IReadOnlyList<string> ModelPathsOf(IEnumerable<DomainDevbookDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return [.. documents.Where(d => d.Kind == DomainDevbookDocumentKind.Model).Select(d => d.Path)];
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
}
