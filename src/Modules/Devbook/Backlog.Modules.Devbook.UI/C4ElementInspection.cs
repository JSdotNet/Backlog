using System.Text.RegularExpressions;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// What the knowledge folders say about one element of the C4 model, as the zoom tab's
/// inspector lists it (<see cref="C4ElementRecords"/>).
/// <para>
/// Three readings, none of them stored a second time:
/// </para>
/// <list type="bullet">
/// <item>"Documented in arc42" — the chapters <c>_c4/references.json</c> maps to a view
/// that shows the element. The views map is the one statement of which chapter
/// documents which view, so this is that map read through the views the element is
/// drawn on.</item>
/// <item>"Decisions" and "Technical debt" — the records whose <c>c4</c> or
/// <c>c4-touches</c> field names the element (<see cref="C4Catalog.RecordsOf"/>),
/// told apart by the folder each sits in: <c>adr/</c> or <c>tdr/</c>.</item>
/// </list>
/// </summary>
public static partial class C4ElementInspection
{
    /// <summary>The inspector's records for <paramref name="elementId"/> in
    /// <paramref name="workspace"/>. <paramref name="chapters"/> are the arc42 folder's
    /// documents, read for a chapter's title and the heading a reference names; a
    /// chapter missing from them is listed by its file name.</summary>
    public static C4ElementRecords For(
        C4Catalog catalog,
        C4WorkspaceEntry workspace,
        IReadOnlyList<DevbookDocument> chapters,
        string elementId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapters);

        if (string.IsNullOrWhiteSpace(elementId)) return C4ElementRecords.None;

        return new C4ElementRecords(
            DocumentedIn(workspace, chapters, elementId),
            Records(catalog, elementId, C4RecordKind.Decision, C4InspectorRecordKind.Decision),
            Records(catalog, elementId, C4RecordKind.Debt, C4InspectorRecordKind.Debt));
    }

    /// <summary>
    /// The debt records that name no element of the model — neither in <c>c4</c> nor in
    /// <c>c4-touches</c> — for the zoom tab's cross-cutting tray, in file order. Read off
    /// the arc42 folder's documents, because a record that links to nothing is in no
    /// link of <paramref name="catalog"/>; the tray lists those still open.
    /// </summary>
    public static IReadOnlyList<C4InspectorRecord> CrossCuttingDebt(C4Catalog catalog, IReadOnlyList<DevbookDocument> chapters)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(chapters);

        return
        [
            .. chapters
                .Where(document => Arc42DevbookCatalog.IsDebtRecord(document.Path) && catalog.ElementsOf(document.Path).Count == 0)
                .OrderBy(document => C4ViewEntry.FileOf(document.Path), StringComparer.OrdinalIgnoreCase)
                .Select(document => new C4InspectorRecord(
                    document.Path,
                    NumberOf(document.Path),
                    WithoutNumber(document.Title),
                    C4InspectorRecordKind.Debt,
                    Severity: document.Severity?.ToString(),
                    Lifecycle: LifecycleLabel(document.DebtLifecycle)))
        ];
    }

    /// <summary>The chapters the views map lists against any view that draws the
    /// element, each once — by file and heading — and in file order, which is the
    /// arc42 chapters' own numbered order.</summary>
    private static List<C4InspectorChapter> DocumentedIn(C4WorkspaceEntry workspace, IReadOnlyList<DevbookDocument> chapters, string elementId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new List<C4InspectorChapter>();

        foreach (var view in workspace.Workspace.Views)
        {
            var references = workspace.Documents(view.Key);
            if (references.Count == 0) continue;

            var shows = C4Exploration.Nodes(workspace.Workspace, view)
                .Any(node => string.Equals(node.ElementId, elementId, StringComparison.OrdinalIgnoreCase));
            if (!shows) continue;

            foreach (var reference in references)
            {
                var file = C4ViewEntry.FileOf(reference);
                var anchor = AnchorOf(reference);
                if (!seen.Add($"{file}#{anchor}")) continue;

                listed.Add(Chapter(reference, file, anchor, chapters));
            }
        }

        return [.. listed.OrderBy(chapter => C4ViewEntry.FileOf(chapter.Path), StringComparer.OrdinalIgnoreCase)];
    }

    private static C4InspectorChapter Chapter(string reference, string file, string? anchor, IReadOnlyList<DevbookDocument> chapters)
    {
        var document = chapters.FirstOrDefault(candidate =>
            string.Equals(C4ViewEntry.FileOf(candidate.Path), file, StringComparison.OrdinalIgnoreCase));

        var number = NumberOf(file);
        var title = document is null ? FileTitle(file) : WithoutNumber(document.Title);

        // The heading the reference names, when the chapter has one by that slug: a
        // reader is told which part of the chapter documents the view.
        if (anchor is not null
            && document?.Headings.FirstOrDefault(heading => heading.Level > 1
                && string.Equals(C4Slug.Of(heading.Text), C4Slug.Of(anchor), StringComparison.OrdinalIgnoreCase)) is { } section)
        {
            title = $"{title} › {section.Text}";
        }

        return new C4InspectorChapter(reference, number, title);
    }

    /// <summary>One kind of record that names the element, each record once: a record
    /// that is about the element is listed as such even where another of its blocks
    /// only touches it.</summary>
    private static List<C4InspectorRecord> Records(C4Catalog catalog, string elementId, C4RecordKind kind, C4InspectorRecordKind shown)
    {
        var links = catalog.RecordsOf(elementId).Where(link => link.Record.Kind == kind).ToList();

        return
        [
            .. links
                .GroupBy(link => C4ViewEntry.FileOf(link.Record.Path), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var record = group.First().Record;
                    var touches = group.All(link => link.Kind == C4LinkKind.Touches);
                    return new C4InspectorRecord(
                        record.Path,
                        NumberOf(record.Path),
                        WithoutNumber(record.Title),
                        shown,
                        Proposed: record.Proposed,
                        Severity: record.Severity?.ToString(),
                        Lifecycle: LifecycleLabel(record.Lifecycle),
                        Touches: touches);
                })
                .OrderBy(record => C4ViewEntry.FileOf(record.Path), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>A rung of the debt ladder the way a reader says it.</summary>
    private static string? LifecycleLabel(DevbookDebtLifecycle? lifecycle) => lifecycle switch
    {
        DevbookDebtLifecycle.Identified => "identified",
        DevbookDebtLifecycle.Planned => "planned",
        DevbookDebtLifecycle.InProgress => "in progress",
        DevbookDebtLifecycle.Resolved => "resolved",
        _ => null
    };

    private static string? AnchorOf(string reference)
    {
        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 || hash == reference.Length - 1 ? null : reference[(hash + 1)..];
    }

    /// <summary>The digits a file name leads with — "03", "0030" — or null.</summary>
    internal static string? NumberOf(string path)
    {
        var match = LeadingNumber().Match(Path.GetFileName(C4ViewEntry.FileOf(path)));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>A title without the number the inspector already shows beside it:
    /// "ADR 0037: C4 zoom…" reads "C4 zoom…", "03. Context and Scope" reads "Context
    /// and Scope".</summary>
    internal static string WithoutNumber(string title)
    {
        var stripped = TitleNumber().Replace(title, string.Empty, 1).Trim();
        return stripped.Length == 0 ? title : stripped;
    }

    /// <summary>A file's name as a fallback title, without the number shown beside
    /// it: "03-context-and-scope.md" reads "context-and-scope".</summary>
    private static string FileTitle(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var stripped = FileNumber().Replace(name, string.Empty, 1);
        return stripped.Length == 0 ? name : stripped;
    }

    [GeneratedRegex(@"^\d+[-_.\s]+")]
    private static partial Regex FileNumber();

    [GeneratedRegex(@"^(\d+)(?=[-_.\s])")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^\s*(?:(?:ADR|TDR)\s*)?\d+\s*[.:\-–—]?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex TitleNumber();
}
