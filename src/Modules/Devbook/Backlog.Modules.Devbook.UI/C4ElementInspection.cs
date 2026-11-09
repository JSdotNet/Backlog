using System.Text.RegularExpressions;
using Backlog.UI.Components.Diagrams.C4;
using Backlog.UI.Components.Markdown;

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
    /// chapter missing from them is listed by its file name. <paramref name="states"/>
    /// are the records' stored workflow states, which stand in for what a record's
    /// file says (<see cref="C4RecordStates"/>); none when omitted.</summary>
    public static C4ElementRecords For(
        C4Catalog catalog,
        C4WorkspaceEntry workspace,
        IReadOnlyList<DevbookDocument> chapters,
        string elementId,
        C4RecordStates? states = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapters);

        if (string.IsNullOrWhiteSpace(elementId)) return C4ElementRecords.None;

        var stored = states ?? C4RecordStates.None;

        return new C4ElementRecords(
            DocumentedIn(workspace, chapters, elementId),
            Records(catalog, elementId, C4RecordKind.Decision, C4InspectorRecordKind.Decision, stored),
            Debt(catalog, workspace.Workspace, chapters, elementId, stored));
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

                // The views map may list another folder's chapter against a view (the
                // domain's context map beside the container view) or a record; the
                // section says "arc42", so it lists the arc42 folder's chapters only.
                if (!Arc42DevbookCatalog.IsArc42Path(reference) || Arc42DevbookCatalog.IsDecisionRecord(reference)) continue;

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
    private static List<C4InspectorRecord> Records(C4Catalog catalog, string elementId, C4RecordKind kind, C4InspectorRecordKind shown, C4RecordStates states)
    {
        var links = catalog.RecordsOf(elementId).Where(link => link.Record.Kind == kind).ToList();

        return
        [
            .. links
                .GroupBy(link => C4ViewEntry.FileOf(link.Record.Path), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var record = states.Apply(group.First().Record);
                    var touches = group.All(link => link.Kind == C4LinkKind.Touches);
                    return new C4InspectorRecord(
                        record.Path,
                        NumberOf(record.Path),
                        WithoutNumber(record.Title),
                        shown,
                        Proposed: record.Proposed,
                        Severity: record.Severity?.ToString(),
                        Lifecycle: LifecycleLabel(record.Lifecycle),
                        Touches: touches)
                    {
                        Accepted = record.Accepted
                    };
                })
                .OrderBy(record => C4ViewEntry.FileOf(record.Path), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The debt that names the element, and on a container the debt that names a
    /// component inside it: the design's Containers screen lists the Devbook
    /// component's debt on Desktop App, "On component Devbook". A record that names the
    /// container itself is listed as the container's. Each carries what its file says
    /// (<see cref="DebtDetail"/>) for the debt card.
    /// </summary>
    private static List<C4InspectorRecord> Debt(
        C4Catalog catalog,
        C4Workspace workspace,
        IReadOnlyList<DevbookDocument> chapters,
        string elementId,
        C4RecordStates states)
    {
        var own = Records(catalog, elementId, C4RecordKind.Debt, C4InspectorRecordKind.Debt, states);
        var listed = new HashSet<string>(own.Select(record => C4ViewEntry.FileOf(record.Path)), StringComparer.OrdinalIgnoreCase);
        var rolledUp = new Dictionary<string, (C4InspectorRecord Record, List<string> Components)>(StringComparer.OrdinalIgnoreCase);

        if (workspace.Element(elementId) is { Kind: C4ElementKind.Container } container)
        {
            var components = workspace.Elements
                .Where(element => element.Kind == C4ElementKind.Component
                    && string.Equals(element.ParentId, container.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(element => element.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var component in components)
            {
                foreach (var record in Records(catalog, component.Id, C4RecordKind.Debt, C4InspectorRecordKind.Debt, states))
                {
                    var file = C4ViewEntry.FileOf(record.Path);
                    if (listed.Contains(file)) continue;

                    if (rolledUp.TryGetValue(file, out var entry)) entry.Components.Add(component.Name);
                    else rolledUp[file] = (record, [component.Name]);
                }
            }
        }

        return
        [
            .. own
                .Concat(rolledUp.Values.Select(entry => entry.Record with { OnComponent = string.Join(", ", entry.Components) }))
                .Select(record => record with { Detail = DebtDetail(record.Path, chapters) })
                .OrderBy(record => C4ViewEntry.FileOf(record.Path), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// What a debt record's file says beyond its title, read off the sections the
    /// repository's debt records carry: the first paragraph under <c>## The debt</c>;
    /// the option under <c>## Remediation options</c> whose heading marks it preferred
    /// now (the last such, since a record that changed its mind marks the earlier one
    /// "preferred before ..."); and the decisions <c>## Origin</c> names. A record the
    /// folder does not hold has nothing to read: null.
    /// </summary>
    private static C4InspectorDebtDetail? DebtDetail(string path, IReadOnlyList<DevbookDocument> chapters)
    {
        var file = C4ViewEntry.FileOf(path);
        var document = chapters.FirstOrDefault(candidate =>
            string.Equals(C4ViewEntry.FileOf(candidate.Path), file, StringComparison.OrdinalIgnoreCase));
        if (document is null) return null;

        string? section = null;
        string? summary = null;
        string? remediation = null;
        var origin = new List<string>();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case DevbookHeadingBlock { Level: <= 2 } heading:
                    section = heading.Level == 2 ? heading.Text.Trim() : null;
                    break;

                case DevbookHeadingBlock { Level: 3 } option
                    when Is(section, "Remediation options") && PreferredNow(option.Text):
                    remediation = OptionName(option.Text);
                    break;

                case DevbookParagraphBlock paragraph when Is(section, "The debt") && summary is null:
                    summary = MarkdownRender.PlainText(paragraph.Content).Trim();
                    break;

                case DevbookParagraphBlock paragraph when Is(section, "Origin"):
                    origin.Add(MarkdownRender.PlainText(paragraph.Content));
                    break;

                case DevbookListBlock list when Is(section, "Origin"):
                    origin.AddRange(list.Items.Select(MarkdownRender.PlainText));
                    break;
            }
        }

        return new C4InspectorDebtDetail(
            string.IsNullOrWhiteSpace(summary) ? null : summary,
            remediation,
            OriginDecisions(document, chapters, string.Join(" ", origin)));
    }

    private static bool Is(string? section, string heading) =>
        string.Equals(section, heading, StringComparison.OrdinalIgnoreCase);

    /// <summary>An option heading that marks it preferred now: "(preferred)" or
    /// "(preferred since ...)", not "(preferred before ...)".</summary>
    private static bool PreferredNow(string heading)
    {
        var marked = OptionMark().Match(heading);
        return marked.Success
            && marked.Groups[1].Value.Contains("preferred", StringComparison.OrdinalIgnoreCase)
            && !marked.Groups[1].Value.Contains("before", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An option heading without its parenthesised mark and code ticks:
    /// "Option C — drop the exclusion".</summary>
    private static string OptionName(string heading) =>
        OptionMark().Replace(heading, string.Empty).Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>The decisions an origin text names ("local ADR 0008"), each once, as
    /// the record's <c>related</c> field or the folder spells its file. A number that
    /// matches no local decision record is left out rather than linked to nothing.</summary>
    private static List<C4InspectorLink> OriginDecisions(DevbookDocument document, IReadOnlyList<DevbookDocument> chapters, string text)
    {
        var links = new List<C4InspectorLink>();

        foreach (var number in AdrMention().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal))
        {
            var path = document.Metadata.Related
                .Select(related => related.Split('#')[0])
                .FirstOrDefault(related => IsLocalDecision(related) && NumberOf(related) == number)
                ?? chapters
                    .Select(chapter => chapter.Path)
                    .FirstOrDefault(candidate => IsLocalDecision(candidate) && NumberOf(candidate) == number);

            if (path is not null) links.Add(new C4InspectorLink(path, $"ADR {number}"));
        }

        return links;
    }

    /// <summary>A local decision record: the guidelines number from 0001 as well, and
    /// an origin's "ADR 0008" is the repository's own.</summary>
    private static bool IsLocalDecision(string path) =>
        Arc42DevbookCatalog.IsArchitectureDecision(path)
        && !path.Replace('\\', '/').Contains("/guidelines/", StringComparison.OrdinalIgnoreCase);

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
    /// "ADR 0046: C4 zoom…" reads "C4 zoom…", "03. Context and Scope" reads "Context
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

    [GeneratedRegex(@"\s*\(([^()]*)\)\s*$")]
    private static partial Regex OptionMark();

    [GeneratedRegex(@"(?<!guideline\s)\bADR\s+(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AdrMention();

    [GeneratedRegex(@"^\d+[-_.\s]+")]
    private static partial Regex FileNumber();

    [GeneratedRegex(@"^(\d+)(?=[-_.\s])")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^\s*(?:(?:ADR|TDR)\s*)?\d+\s*[.:\-–—]?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex TitleNumber();
}
