using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>Which list the architecture surface's left navigation draws: the model's,
/// or the chapters', the decision log or the debt beside an open document.</summary>
public enum Arc42SurfaceMode
{
    Model,
    Chapters,
    Decisions,
    Debt
}

/// <summary>One numbered arc42 chapter on the architecture surface's chapter list, with
/// the counts its badges carry.</summary>
/// <param name="Number">The two digits its file name opens with, or null for a file
/// that has none — a building block's own file.</param>
/// <param name="Views">The C4 views that say they document it.</param>
/// <param name="Decisions">On the chapter that holds the architecture decisions (09),
/// how many ADRs there are; zero on every other.</param>
/// <param name="Debt">On the chapter that holds the risks and technical debt (11), how
/// many TDRs there are; zero on every other.</param>
public sealed record Arc42SurfaceChapter(string Path, string? Number, string Title, int Views, int Decisions, int Debt);

/// <summary>One decision or debt record on the surface's lists.</summary>
/// <param name="Proposed">An ADR still at <c>status: proposed</c>.</param>
/// <param name="Guideline">An ADR adopted from elsewhere, under <c>adr/guidelines/</c>.</param>
/// <param name="Note">What is said on the right: "proposed", a TDR's severity and rung.</param>
public sealed record Arc42SurfaceRecord(string Path, string Number, string Title, bool Proposed, bool Guideline, string? Note);

/// <summary>A chapter that documents the open view, by the reference the views map
/// states — a section anchor kept.</summary>
public sealed record Arc42SurfaceCitation(string Reference, string Path, string? Number, string Title);

/// <summary>A C4 view an open chapter documents, with the section the reference names.</summary>
public sealed record Arc42SurfaceView(C4ViewEntry View, string? Section);

/// <summary>The right-hand panel of the design's arc42 screen.</summary>
public sealed record Arc42ChapterLinks(
    IReadOnlyList<Arc42SurfaceView> Views,
    IReadOnlyList<Arc42SurfaceRecord> Decisions,
    IReadOnlyList<Arc42SurfaceRecord> Debt)
{
    public static Arc42ChapterLinks None { get; } = new([], [], []);
}

/// <summary>What the find field offers: an element of the model, a chapter or a record.</summary>
public enum Arc42FindKind
{
    Element,
    Chapter,
    Decision,
    Debt
}

/// <summary>One suggestion under the find field.</summary>
/// <param name="Key">What picking it opens: an element's identifier, or a chapter's or
/// record's path.</param>
/// <param name="ViewKey">For an element, the shallowest view that draws it.</param>
public sealed record Arc42FindHit(Arc42FindKind Kind, string Key, string Label, string Detail, string? ViewKey = null)
{
    /// <summary>The value the find field's option carries: unique across kinds.</summary>
    public string Value => $"{Kind}:{Key}";
}

/// <summary>
/// What the architecture surface (plan c4-zoom-navigation, the design's every screen)
/// reads from the arc42 folder and the C4 model, besides the model itself: the chapter
/// list with its badges, the decision log and the debt list, the chapters that document
/// the open view, an open chapter's right-hand panel, and the find field's hits.
/// <para>
/// Pure, over the catalogs the panel already holds, so every count is tested without a
/// renderer. Nothing here is stored a second time: each answer is the chapters' own
/// fields and the views map read another way round, as <see cref="C4ElementInspection"/>
/// and <see cref="C4RecordPlacement"/> read them.
/// </para>
/// </summary>
public static class Arc42Surface
{
    /// <summary>The arc42 chapter whose badge counts the ADRs.</summary>
    public const string DecisionsChapter = "09";

    /// <summary>The arc42 chapter whose badge counts the TDRs.</summary>
    public const string DebtChapter = "11";

    /// <summary>The numbered chapters, in file order, then any other chapter file — a
    /// building block's — in file order. Records and the folders' indexes are not
    /// chapters.</summary>
    public static IReadOnlyList<Arc42SurfaceChapter> Chapters(Arc42DevbookCatalog catalog, C4Catalog c4)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(c4);

        var decisions = Decisions(catalog).Count(record => !record.Guideline);
        var debt = Debt(catalog).Count;

        return
        [
            .. catalog.Documents
                .Where(IsChapter)
                .Select(document =>
                {
                    var number = C4ElementInspection.NumberOf(document.Path);
                    return new Arc42SurfaceChapter(
                        document.Path,
                        number,
                        C4ElementInspection.WithoutNumber(document.Title),
                        c4.Views.Count(view => view.Documented(document.Path)),
                        number == DecisionsChapter ? decisions : 0,
                        number == DebtChapter ? debt : 0);
                })
                .OrderBy(chapter => chapter.Number is null)
                .ThenBy(chapter => C4ViewEntry.FileOf(chapter.Path), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Whether a document is a chapter of the folder rather than a record or
    /// an index: a numbered file at the folder's top, or a building block's file.</summary>
    public static bool IsChapter(DevbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Arc42DevbookCatalog.IsDecisionRecord(document.Path)) return false;
        if (Arc42DevbookCatalog.IsBuildingBlock(document.Path)) return true;

        return C4ElementInspection.NumberOf(document.Path) is not null;
    }

    /// <summary>The decision log: the local ADRs in number order, then the guidelines.
    /// The folder's index is no record.</summary>
    public static IReadOnlyList<Arc42SurfaceRecord> Decisions(Arc42DevbookCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return
        [
            .. catalog.Documents
                .Where(document => Arc42DevbookCatalog.IsArchitectureDecision(document.Path))
                .Select(document =>
                {
                    var proposed = string.Equals(document.Status?.Trim(), "proposed", StringComparison.OrdinalIgnoreCase);
                    return new Arc42SurfaceRecord(
                        document.Path,
                        C4ElementInspection.NumberOf(document.Path) ?? Path.GetFileNameWithoutExtension(document.Path),
                        C4ElementInspection.WithoutNumber(document.Title),
                        proposed,
                        IsGuideline(document.Path),
                        proposed ? "proposed" : null);
                })
                .OrderBy(record => record.Guideline)
                .ThenBy(record => C4ViewEntry.FileOf(record.Path), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The debt records in number order, each with its severity and rung.</summary>
    public static IReadOnlyList<Arc42SurfaceRecord> Debt(Arc42DevbookCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return
        [
            .. catalog.Documents
                .Where(document => Arc42DevbookCatalog.IsDebtRecord(document.Path))
                .OrderBy(document => C4ViewEntry.FileOf(document.Path), StringComparer.OrdinalIgnoreCase)
                .Select(DebtRecord)
        ];
    }

    /// <summary>The chapters the views map lists against a view, each reference once,
    /// in file order.</summary>
    public static IReadOnlyList<Arc42SurfaceCitation> DocumentsView(Arc42DevbookCatalog catalog, C4WorkspaceEntry? workspace, string? viewKey)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (workspace is null || viewKey is null) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cited = new List<Arc42SurfaceCitation>();

        foreach (var reference in workspace.Documents(viewKey))
        {
            var file = C4ViewEntry.FileOf(reference);
            if (!seen.Add(file)) continue;

            var document = catalog.Documents.FirstOrDefault(candidate =>
                string.Equals(C4ViewEntry.FileOf(candidate.Path), file, StringComparison.OrdinalIgnoreCase));

            cited.Add(new Arc42SurfaceCitation(
                reference,
                document?.Path ?? reference,
                C4ElementInspection.NumberOf(file),
                document is null ? Path.GetFileNameWithoutExtension(file) : C4ElementInspection.WithoutNumber(document.Title)));
        }

        return [.. cited.OrderBy(citation => C4ViewEntry.FileOf(citation.Path), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The right-hand panel of an open chapter: the views it documents, with the section
    /// each reference names; the decisions that cite it — those its <c>related:</c> lists
    /// name and those whose <c>related:</c> name it; and the open or closed debt on the
    /// elements its views draw, or that names it.
    /// </summary>
    public static Arc42ChapterLinks ChapterLinks(Arc42DevbookCatalog catalog, C4Catalog c4, DevbookDocument chapter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(c4);
        ArgumentNullException.ThrowIfNull(chapter);

        var self = C4ViewEntry.FileOf(chapter.Path);

        var views = c4.Views
            .Where(view => view.Documented(chapter.Path))
            .Select(view => new Arc42SurfaceView(view, SectionOf(view, chapter)))
            .ToList();

        var named = RelatedFiles(chapter);
        var decisions = Decisions(catalog)
            .Where(record => named.Contains(C4ViewEntry.FileOf(record.Path))
                || catalog.Documents.Any(document => SameFile(document, record.Path) && RelatedFiles(document).Contains(self)))
            .ToList();

        var drawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in views)
        {
            if (c4.WorkspaceOf(view.View) is not { } workspace || workspace.Workspace.View(view.View.Key) is not { } model) continue;
            foreach (var node in C4Exploration.Nodes(workspace.Workspace, model)) drawn.Add(node.ElementId);
        }

        var debt = catalog.Documents
            .Where(document => Arc42DevbookCatalog.IsDebtRecord(document.Path))
            .Where(document => RelatedFiles(document).Contains(self)
                || named.Contains(C4ViewEntry.FileOf(document.Path))
                || c4.ElementsOf(document.Path).Any(link => drawn.Contains(link.ElementId) || Below(c4, link.ElementId, drawn)))
            .OrderBy(document => C4ViewEntry.FileOf(document.Path), StringComparer.OrdinalIgnoreCase)
            .Select(DebtRecord)
            .ToList();

        return new Arc42ChapterLinks(views, decisions, debt);
    }

    /// <summary>
    /// The find field's suggestions for what was typed: elements of the model first —
    /// each at the shallowest view that draws it — then chapters, decisions and debt
    /// whose number or title holds the text. Fewer than two characters finds nothing.
    /// </summary>
    public static IReadOnlyList<Arc42FindHit> Find(Arc42DevbookCatalog catalog, C4Workspace? workspace, string? query, int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];

        var needle = query.Trim();
        var hits = new List<Arc42FindHit>();

        if (workspace is not null)
        {
            foreach (var hit in C4Exploration.Search(workspace, needle, limit).Where(hit => hit.Kind == C4SearchHitKind.Element))
            {
                var element = workspace.Elements.FirstOrDefault(candidate =>
                    candidate.InstanceOfId is null
                    && string.Equals(C4MermaidWriter.AliasOf(candidate.Id), hit.Alias, StringComparison.Ordinal));
                if (element is null || hit.ViewKey is null) continue;

                hits.Add(new Arc42FindHit(Arc42FindKind.Element, element.Id, hit.Label, hit.Detail, hit.ViewKey));
            }
        }

        foreach (var chapter in catalog.Documents.Where(IsChapter))
        {
            var number = C4ElementInspection.NumberOf(chapter.Path);
            var title = C4ElementInspection.WithoutNumber(chapter.Title);
            if (Matches(number, title, needle)) hits.Add(new Arc42FindHit(Arc42FindKind.Chapter, chapter.Path, number is null ? title : $"{number} {title}", "arc42 chapter"));
        }

        foreach (var record in Decisions(catalog))
        {
            if (Matches(record.Number, record.Title, needle) || Matches(null, $"ADR {record.Number}", needle))
            {
                hits.Add(new Arc42FindHit(Arc42FindKind.Decision, record.Path, $"ADR {record.Number} {record.Title}", record.Guideline ? "Guideline" : record.Proposed ? "Decision · proposed" : "Decision"));
            }
        }

        foreach (var record in Debt(catalog))
        {
            if (Matches(record.Number, record.Title, needle) || Matches(null, $"TDR {record.Number}", needle))
            {
                hits.Add(new Arc42FindHit(Arc42FindKind.Debt, record.Path, $"TDR {record.Number} {record.Title}", "Technical debt"));
            }
        }

        return [.. hits.Take(limit)];
    }

    private static bool Matches(string? number, string title, string needle) =>
        (number is not null && number.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        || title.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static Arc42SurfaceRecord DebtRecord(DevbookDocument document)
    {
        var note = string.Join(" · ", new[] { document.Severity?.ToString(), Lifecycle(document.DebtLifecycle) }.Where(part => part is not null));
        return new Arc42SurfaceRecord(
            document.Path,
            C4ElementInspection.NumberOf(document.Path) ?? Path.GetFileNameWithoutExtension(document.Path),
            C4ElementInspection.WithoutNumber(document.Title),
            false,
            false,
            note.Length == 0 ? null : note);
    }

    private static string? Lifecycle(DevbookDebtLifecycle? lifecycle) => lifecycle switch
    {
        DevbookDebtLifecycle.Identified => "identified",
        DevbookDebtLifecycle.Planned => "planned",
        DevbookDebtLifecycle.InProgress => "in progress",
        DevbookDebtLifecycle.Resolved => "resolved",
        _ => null
    };

    private static bool IsGuideline(string path) =>
        path.Replace('\\', '/').Contains("/adr/guidelines/", StringComparison.OrdinalIgnoreCase);

    private static bool SameFile(DevbookDocument document, string path) =>
        string.Equals(C4ViewEntry.FileOf(document.Path), C4ViewEntry.FileOf(path), StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> RelatedFiles(DevbookDocument document) =>
        document.Metadata.Related
            .Concat(document.Headings.SelectMany(heading => heading.Metadata.Related))
            .Select(C4ViewEntry.FileOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The heading a view's reference to the chapter names, when the chapter
    /// has one by that slug.</summary>
    private static string? SectionOf(C4ViewEntry view, DevbookDocument chapter)
    {
        var file = C4ViewEntry.FileOf(chapter.Path);
        foreach (var reference in view.Documents)
        {
            if (!string.Equals(C4ViewEntry.FileOf(reference), file, StringComparison.OrdinalIgnoreCase)) continue;

            var hash = reference.IndexOf('#', StringComparison.Ordinal);
            if (hash < 0 || hash == reference.Length - 1) continue;

            var anchor = C4Slug.Of(reference[(hash + 1)..]);
            if (chapter.Headings.FirstOrDefault(heading => heading.Level > 1
                    && string.Equals(C4Slug.Of(heading.Text), anchor, StringComparison.OrdinalIgnoreCase)) is { } heading)
            {
                return heading.Text;
            }
        }

        return null;
    }

    /// <summary>Whether an element sits, at any depth, inside one of the elements
    /// drawn — a component's debt is on the container that holds it.</summary>
    private static bool Below(C4Catalog c4, string elementId, HashSet<string> drawn)
    {
        foreach (var workspace in c4.Workspaces)
        {
            if (workspace.Workspace.Element(elementId) is null) continue;
            if (workspace.Workspace.Ancestry(elementId).Any(element => drawn.Contains(element.Id))) return true;
        }

        return false;
    }
}
