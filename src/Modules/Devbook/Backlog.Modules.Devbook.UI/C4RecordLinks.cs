using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>Which of the two fields a link was written in.</summary>
public enum C4LinkKind
{
    /// <summary><c>c4</c>: the record is about the element.</summary>
    About,

    /// <summary><c>c4-touches</c>: the record's consequences name the element, but its
    /// decision is not about it.</summary>
    Touches
}

/// <summary>What sort of chapter a linked record is, as the folder it sits in says.</summary>
public enum C4RecordKind
{
    /// <summary>An arc42 chapter that is none of the others.</summary>
    Chapter,

    /// <summary>A decision record, <c>arc42/adr/</c>.</summary>
    Decision,

    /// <summary>A debt record, <c>arc42/tdr/</c>.</summary>
    Debt,

    /// <summary>A building block's own file, <c>arc42/building-blocks/&lt;slug&gt;.md</c>.</summary>
    BuildingBlock,

    /// <summary>A chapter of the domain folder.</summary>
    Domain
}

/// <summary>
/// One chapter file that names the model, with what the overlays and the inspector
/// show about it.
/// </summary>
/// <param name="Path">The file, spelled relative to the repository as the chapter
/// catalogs spell it.</param>
/// <param name="Title">The file's title heading.</param>
/// <param name="Kind">What sort of record it is, read from where it sits.</param>
/// <param name="Severity">A debt record's severity; null for every other kind.</param>
/// <param name="Lifecycle">A debt record's rung on the debt ladder; null for every
/// other kind.</param>
/// <param name="History">A decision record's <c>## History</c> rows, newest first —
/// the standing choice and the ones it superseded. Empty for every other kind.</param>
public sealed record C4LinkedRecord(
    string Path,
    string Title,
    C4RecordKind Kind,
    DevbookDebtSeverity? Severity,
    DevbookDebtLifecycle? Lifecycle,
    IReadOnlyList<DevbookHistoryRow> History)
{
    /// <summary>A decision record's <c>status</c> as written — <c>proposed</c>,
    /// <c>accepted</c> — which the inspector reads its "proposed" chip from. Null for
    /// every other kind: a debt record's status is its <see cref="Lifecycle"/>.</summary>
    public string? Status { get; init; }

    /// <summary>A decision still at <c>status: proposed</c>.</summary>
    public bool Proposed =>
        Kind == C4RecordKind.Decision && string.Equals(Status?.Trim(), "proposed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One edge from a record to an element of the workspace.
/// </summary>
/// <param name="ElementId">The element's identifier as the workspace spells it, not as
/// the field did: the identifier is matched case-insensitively, as the workspace's
/// own lookup is, and stored the one way every later lookup agrees on.</param>
/// <param name="Section">The heading of the block the field sat under, when that is a
/// section rather than the file's title — a domain chapter links per feature or
/// aggregate. Null for the file's own block.</param>
public sealed record C4ElementLink(string ElementId, C4LinkKind Kind, C4LinkedRecord Record, string? Section = null);

/// <summary>
/// The chapters' <c>c4</c>, <c>c4-touches</c> and <c>code</c> fields, resolved against
/// the workspace.
/// <para>
/// devbook 1.28.0 made these the only statement of which element a record is about,
/// and of which folders a building block's code sits in: the model stores none of it,
/// and an element's records and code are read back along the fields
/// (<c>devbook-chapter-metadata.md</c>). This is that read. The fields are taken from
/// the chapter files themselves rather than from the devbook database, because the
/// database keeps the edges but not <c>severity</c> or <c>code</c>, and a branch
/// snapshot has no database at all.
/// </para>
/// <para>
/// Handed text, never a folder: the walk and the reads are
/// <see cref="C4DevbookStore"/>'s, which already reads the workspace beside them, so
/// this file stays the pure half a module's UI project may hold.
/// </para>
/// </summary>
internal static class C4RecordLinks
{
    /// <summary>One knowledge folder to read, and the root its paths are spelled
    /// against.</summary>
    public sealed record Folder(string FullPath, string? RootPath, bool IsDomain);

    /// <summary>One chapter file as the store read it: its repository-relative path,
    /// its text, and whether it is a domain chapter — or, for a file that would not
    /// read, why not.</summary>
    public sealed record Chapter(string Path, string? Text, bool IsDomain, string? ReadError = null);

    public sealed record Reading(
        IReadOnlyList<C4ElementLink> Links,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Code,
        IReadOnlyList<C4WorkspaceProblem> Problems);

    /// <summary>
    /// Every link the folders' chapters write, resolved against
    /// <paramref name="workspace"/>. An identifier the workspace does not define is a
    /// problem filed under <paramref name="workspaceFile"/> — the devbook check reports
    /// it as an error, and a panel that dropped it silently would show an element with
    /// fewer records than its chapters claim.
    /// </summary>
    public static Reading Read(C4Workspace workspace, string workspaceFile, IEnumerable<Chapter> chapters)
    {
        var links = new List<C4ElementLink>();
        var code = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<C4WorkspaceProblem>();

        foreach (var chapter in chapters)
        {
            var path = chapter.Path;
            if (chapter.Text is not { } text)
            {
                problems.Add(new C4WorkspaceProblem(workspaceFile, new C4Problem(0, path, chapter.ReadError ?? "The chapter could not be read.")));
                continue;
            }

            var headings = DevbookMarkdownParser.ReadHeadings(text);

            // Most chapters name no element; only those are worth a full parse.
            if (!headings.Any(heading => heading.Metadata.C4.Count > 0 || heading.Metadata.C4Touches.Count > 0)) continue;

            var record = Record(path, text, headings, chapter.IsDomain);
            var title = headings.FirstOrDefault(heading => heading.Level == 1);

            foreach (var heading in headings)
            {
                var section = ReferenceEquals(heading, title) ? null : heading.Text;
                Resolve(heading.Metadata.C4, C4LinkKind.About, "c4", section);
                Resolve(heading.Metadata.C4Touches, C4LinkKind.Touches, "c4-touches", section);
            }

            // A building block's file names its element in `c4` and its folders in
            // `code`, both in its top-level block; the folders are that element's.
            if (record.Kind == C4RecordKind.BuildingBlock && title is { Metadata: var meta } && meta.Code.Count > 0)
            {
                foreach (var element in meta.C4.Select(workspace.Element).OfType<C4Element>())
                {
                    if (!code.TryGetValue(element.Id, out var owned)) code[element.Id] = owned = [];
                    owned.AddRange(meta.Code.Where(entry => !owned.Contains(entry, StringComparer.Ordinal)));
                }
            }

            void Resolve(IReadOnlyList<string> identifiers, C4LinkKind kind, string field, string? section)
            {
                foreach (var identifier in identifiers)
                {
                    if (workspace.Element(identifier) is { } element)
                    {
                        links.Add(new C4ElementLink(element.Id, kind, record, section));
                    }
                    else
                    {
                        var at = section is null ? path : $"{path} ({section})";
                        problems.Add(new C4WorkspaceProblem(
                            workspaceFile,
                            new C4Problem(0, path, $"{at} names `{identifier}` in `{field}`, but {workspaceFile} defines no such element.")));
                    }
                }
            }
        }

        return new Reading(
            links,
            code.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            problems);
    }

    private static C4LinkedRecord Record(string path, string text, IReadOnlyList<DevbookHeadingSummary> headings, bool isDomain)
    {
        var kind = isDomain ? C4RecordKind.Domain
            : Arc42DevbookCatalog.IsDebtRecord(path) ? C4RecordKind.Debt
            : Arc42DevbookCatalog.IsArchitectureDecision(path) ? C4RecordKind.Decision
            : Arc42DevbookCatalog.IsBuildingBlock(path) ? C4RecordKind.BuildingBlock
            : C4RecordKind.Chapter;

        var title = headings.FirstOrDefault(heading => heading.Level == 1);
        var meta = title?.Metadata ?? DevbookMeta.Empty;

        // Only a decision record's history is read, and it takes the full parse: the
        // table is body, not a field.
        var history = kind == C4RecordKind.Decision
            ? DevbookMarkdownParser.Parse(path, text).History
            : [];

        return new C4LinkedRecord(
            path,
            title?.Text ?? Path.GetFileNameWithoutExtension(path),
            kind,
            kind == C4RecordKind.Debt ? meta.DebtSeverity : null,
            kind == C4RecordKind.Debt ? meta.DebtLifecycle : null,
            history)
        {
            Status = kind == C4RecordKind.Decision ? meta.Status : null
        };
    }
}
