using System.Text.RegularExpressions;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>How a record bears on one element of the map.</summary>
public enum C4RecordRole
{
    /// <summary>The record names it nowhere: drawn dimmed.</summary>
    None,

    /// <summary>The record's consequences name it (<c>c4-touches</c>).</summary>
    Touches,

    /// <summary>The record is about it (<c>c4</c>).</summary>
    Decides
}

/// <summary>One container on the compact map.</summary>
/// <param name="ElementId">The container's identifier in the workspace.</param>
/// <param name="Name">Its name, as the model gives it.</param>
/// <param name="Role">How the record bears on it, or on anything inside it.</param>
public sealed record C4RecordTile(string ElementId, string Name, C4RecordRole Role);

/// <summary>
/// The container view a record lands on, drawn small: every container of the view as
/// a tile, the ones the record names marked.
/// </summary>
/// <param name="Workspace">The authored workspace the record's links resolve in.</param>
/// <param name="ViewKey">The container view the map is of, and the one "Open in C4
/// zoom" opens.</param>
/// <param name="Tiles">The view's containers, in the order the view draws them.</param>
/// <param name="Beyond">What the record names that is no container of the view — a
/// person, an external system, or the system itself — so a link is never lost for
/// having no tile.</param>
/// <param name="FocusId">The tile "Open in C4 zoom" leaves selected: the first the
/// record decides, else the first it touches, else none.</param>
public sealed record C4RecordMap(
    C4WorkspaceEntry Workspace,
    string ViewKey,
    string ViewTitle,
    IReadOnlyList<C4RecordTile> Tiles,
    IReadOnlyList<C4RecordTile> Beyond,
    string? FocusId);

/// <summary>Another decision or debt record in the chain, by its file.</summary>
/// <param name="Number">The four digits the file name opens with.</param>
/// <param name="Proposed">Whether its status is <c>proposed</c>, drawn dashed.</param>
public sealed record C4ChainRecord(string Path, string Number, string Title, bool Proposed);

/// <summary>An arc42 chapter section whose <c>related:</c> names the record.</summary>
/// <param name="Anchor">The section's slug, or null when the chapter's own top block
/// names it.</param>
/// <param name="Number">The chapter's number, read off its file name.</param>
public sealed record C4RecordCitation(string Path, string? Anchor, string Number, string Chapter, string? Section);

/// <summary>
/// Where an ADR or a TDR lands on the model, and where it sits among the other records
/// — the right-hand panel of the design's ADR screen (plan c4-zoom-navigation).
/// <para>
/// Every part is read from what the chapters already say, never from a file kept
/// beside the model (local ADR 0041): the map from the record's <c>c4</c> and
/// <c>c4-touches</c> fields as <see cref="C4Catalog.ElementsOf"/> resolved them, the
/// chain from the records' <c>related:</c> lists and <c>## History</c> tables, the
/// citations from the arc42 chapters' <c>related:</c> lists.
/// </para>
/// <para>
/// Supersession across records is read from the History rows below the standing
/// choice: devbook 1.28.0 keeps one record per concern and writes an earlier choice as
/// a row, and a row that opens <c>ADR 0004:</c> says the choice came from another
/// record — so this record superseded that one. Prose saying "supersedes" is not read:
/// it also says "does not supersede".
/// </para>
/// </summary>
public sealed partial record C4RecordPlacement(
    DevbookDocument Record,
    C4RecordKind Kind,
    C4RecordMap? Map,
    IReadOnlyList<C4ChainRecord> Supersedes,
    IReadOnlyList<C4ChainRecord> SupersededBy,
    IReadOnlyList<C4ChainRecord> BuildsOn,
    IReadOnlyList<C4ChainRecord> BuiltOnBy,
    IReadOnlyList<C4RecordCitation> CitedBy,
    IReadOnlyList<C4ChainRecord> Debt)
{
    /// <summary>A TDR's severity, or null for an ADR.</summary>
    public DevbookDebtSeverity? Severity => Record.Severity;

    /// <summary>A TDR's rung on the debt ladder, or null for an ADR.</summary>
    public DevbookDebtLifecycle? Lifecycle => Record.DebtLifecycle;

    /// <summary>
    /// The placement of <paramref name="record"/>, or null when it is no decision or
    /// debt record — the index beside the records included.
    /// </summary>
    public static C4RecordPlacement? For(Arc42DevbookCatalog catalog, C4Catalog c4, DevbookDocument record)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(c4);
        ArgumentNullException.ThrowIfNull(record);

        var kind = Arc42DevbookCatalog.IsDebtRecord(record.Path) ? C4RecordKind.Debt
            : Arc42DevbookCatalog.IsArchitectureDecision(record.Path) ? C4RecordKind.Decision
            : (C4RecordKind?)null;
        if (kind is not { } recordKind) return null;

        var map = MapOf(c4, record.Path);
        var self = C4ViewEntry.FileOf(record.Path);
        var cited = Citations(catalog, self);

        if (recordKind == C4RecordKind.Debt)
        {
            return new C4RecordPlacement(record, recordKind, map, [], [], [], [], cited, []);
        }

        var decisions = catalog.Documents.Where(IsLocalDecision).ToList();

        // Supersession is a local record's alone. A guideline adopted from elsewhere
        // numbers from 0001 as the local records do, so its number would be matched
        // against History rows that name a local record of the same number.
        var local = IsLocalDecision(record);
        var number = local ? NumberOf(record.Path) : null;

        List<C4ChainRecord> supersedes = !local ? [] : [.. decisions
            .Where(other => !Same(other, self) && Named(record, NumberOf(other.Path)))
            .Select(Chain)];

        List<C4ChainRecord> supersededBy = number is null
            ? []
            : [.. decisions.Where(other => !Same(other, self) && Named(other, number)).Select(Chain)];

        var mine = RelatedFiles(record);
        List<C4ChainRecord> buildsOn = [.. decisions
            .Where(other => !Same(other, self) && mine.Contains(C4ViewEntry.FileOf(other.Path)))
            .Select(Chain)
            .Where(entry => !supersedes.Any(earlier => string.Equals(earlier.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))];

        List<C4ChainRecord> builtOnBy = [.. decisions
            .Where(other => !Same(other, self) && RelatedFiles(other).Contains(self))
            .Select(Chain)
            .Where(entry => !supersededBy.Any(later => string.Equals(later.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))];

        List<C4ChainRecord> debt = [.. catalog.Documents
            .Where(other => Arc42DevbookCatalog.IsDebtRecord(other.Path) && RelatedFiles(other).Contains(self))
            .Select(Chain)];

        return new C4RecordPlacement(record, recordKind, map, supersedes, supersededBy, buildsOn, builtOnBy, cited, debt);
    }

    // ---- the map ------------------------------------------------------------------

    private static C4RecordMap? MapOf(C4Catalog c4, string recordPath)
    {
        // The fields name workspace.dsl's elements, so only an authored workspace has
        // anything to place them on.
        if (c4.Workspaces.FirstOrDefault(entry => !entry.Derived) is not { } entry) return null;

        var workspace = entry.Workspace;
        var links = c4.ElementsOf(recordPath);

        // Per element named, the strongest way the record names it.
        var roles = new Dictionary<string, C4RecordRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var role = link.Kind == C4LinkKind.About ? C4RecordRole.Decides : C4RecordRole.Touches;
            roles[link.ElementId] = Strongest(roles.GetValueOrDefault(link.ElementId), role);
        }

        // A component the record names lands on the container it sits in.
        var byContainer = new Dictionary<string, C4RecordRole>(StringComparer.OrdinalIgnoreCase);
        var beyond = new List<(string Id, C4RecordRole Role)>();
        foreach (var (id, role) in roles)
        {
            if (workspace.Ancestry(id).FirstOrDefault(element => element.Kind == C4ElementKind.Container) is { } container)
            {
                byContainer[container.Id] = Strongest(byContainer.GetValueOrDefault(container.Id), role);
            }
            else
            {
                beyond.Add((id, role));
            }
        }

        // The container view that draws the most of what the record names; the first
        // in the workspace's own order on a tie, or when it names nothing.
        var candidates = C4Exploration.OrderedViews(workspace)
            .Where(view => view.Kind == C4ViewKind.Container)
            .Select(view => (View: view, Containers: C4Exploration.Nodes(workspace, view)
                .Where(node => node.Kind == C4ElementKind.Container)
                .GroupBy(node => node.ElementId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList()))
            .Where(candidate => candidate.Containers.Count > 0)
            .ToList();

        if (candidates.Count == 0) return null;

        var best = candidates
            .Select((candidate, order) => (candidate, order, score: candidate.Containers.Count(node => byContainer.ContainsKey(node.ElementId))))
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.order)
            .First()
            .candidate;

        var tiles = best.Containers
            .Select(node => new C4RecordTile(node.ElementId, node.Name, byContainer.GetValueOrDefault(node.ElementId)))
            .ToList();

        // A container this view leaves out is still named, below the map.
        var drawn = tiles.Select(tile => tile.ElementId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var off = byContainer
            .Where(pair => !drawn.Contains(pair.Key))
            .Select(pair => (Id: pair.Key, Role: pair.Value))
            .Concat(beyond)
            .Select(item => new C4RecordTile(item.Id, workspace.Element(item.Id)?.Name ?? item.Id, item.Role))
            .OrderByDescending(tile => tile.Role)
            .ToList();

        var focus = tiles.FirstOrDefault(tile => tile.Role == C4RecordRole.Decides)
            ?? tiles.FirstOrDefault(tile => tile.Role == C4RecordRole.Touches);

        return new C4RecordMap(entry, best.View.Key, C4Exploration.Label(workspace, best.View), tiles, off, focus?.ElementId);
    }

    private static C4RecordRole Strongest(C4RecordRole a, C4RecordRole b) => a > b ? a : b;

    // ---- the chain ----------------------------------------------------------------

    /// <summary>A decision record of this repository: directly in <c>adr/</c>, so
    /// neither the index nor a guideline the repository adopted from elsewhere.</summary>
    private static bool IsLocalDecision(DevbookDocument document)
    {
        if (!Arc42DevbookCatalog.IsArchitectureDecision(document.Path)) return false;

        var path = document.Path.Replace('\\', '/');
        var folder = path.LastIndexOf("/adr/", StringComparison.OrdinalIgnoreCase);
        return folder >= 0 && !path[(folder + "/adr/".Length)..].Contains('/', StringComparison.Ordinal);
    }

    private static bool Same(DevbookDocument document, string file) =>
        string.Equals(C4ViewEntry.FileOf(document.Path), file, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a History row below the standing choice opens by naming
    /// ADR <paramref name="number"/> — an earlier choice that record made.</summary>
    private static bool Named(DevbookDocument document, string? number) =>
        number is not null
        && document.Superseded.Any(row => HistoryRecord().Match(row.Change) is { Success: true } match
            && string.Equals(match.Groups[1].Value, number, StringComparison.Ordinal));

    /// <summary>Every file the record's blocks name in <c>related:</c>, spelled the one
    /// way two references to the same file agree on.</summary>
    private static HashSet<string> RelatedFiles(DevbookDocument document) =>
        document.Metadata.Related
            .Concat(document.Headings.SelectMany(heading => heading.Metadata.Related))
            .Select(C4ViewEntry.FileOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static C4ChainRecord Chain(DevbookDocument document) => new(
        document.Path,
        NumberOf(document.Path) ?? Path.GetFileNameWithoutExtension(document.Path),
        TitleOf(document),
        string.Equals(document.Status?.Trim(), "proposed", StringComparison.OrdinalIgnoreCase));

    /// <summary>The title without its "ADR 0005:" lead, which the number beside it
    /// already says.</summary>
    private static string TitleOf(DevbookDocument document)
    {
        var title = document.Title;
        var colon = title.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && LeadingNumber().IsMatch(title[..colon]) ? title[(colon + 1)..].Trim() : title;
    }

    private static string? NumberOf(string path) =>
        FileNumber().Match(Path.GetFileName(path)) is { Success: true } match ? match.Groups[1].Value : null;

    // ---- the chapters that cite it --------------------------------------------------

    private static List<C4RecordCitation> Citations(Arc42DevbookCatalog catalog, string self)
    {
        var citations = new List<C4RecordCitation>();

        foreach (var document in catalog.Documents.Where(document => !Arc42DevbookCatalog.IsDecisionRecord(document.Path)))
        {
            var number = ChapterNumber().Match(Path.GetFileName(document.Path)) is { Success: true } match
                ? match.Groups[1].Value
                : string.Empty;

            var title = document.Headings.FirstOrDefault(heading => heading.Level == 1);
            foreach (var heading in document.Headings)
            {
                if (!heading.Metadata.Related.Any(entry => string.Equals(C4ViewEntry.FileOf(entry), self, StringComparison.OrdinalIgnoreCase))) continue;

                var top = ReferenceEquals(heading, title);
                citations.Add(new C4RecordCitation(
                    document.Path,
                    top ? null : C4ChapterViews.Slug(heading.Text),
                    number,
                    WithoutNumber(document.Title, number),
                    top ? null : heading.Text));
            }

            // A chapter whose top block carries `related:` outside any heading.
            if (title is null && document.Metadata.Related.Any(entry => string.Equals(C4ViewEntry.FileOf(entry), self, StringComparison.OrdinalIgnoreCase)))
            {
                citations.Add(new C4RecordCitation(document.Path, null, number, WithoutNumber(document.Title, number), null));
            }
        }

        return citations;
    }

    /// <summary>A chapter title without the number its file already gives — "05.
    /// Building Block View" is drawn beside "05".</summary>
    private static string WithoutNumber(string title, string number)
    {
        if (number.Length == 0 || !title.StartsWith(number, StringComparison.Ordinal)) return title;

        var rest = title[number.Length..].TrimStart('.', ' ', '-');
        return rest.Length == 0 ? title : rest;
    }

    [GeneratedRegex(@"^\s*(?:local\s+)?ADR\s+(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex HistoryRecord();

    [GeneratedRegex(@"^(\d{4})\b")]
    private static partial Regex FileNumber();

    [GeneratedRegex(@"^(\d{1,2})[-_.]")]
    private static partial Regex ChapterNumber();

    [GeneratedRegex(@"^\s*(?:ADR|TDR)\s+\d+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingNumber();
}
