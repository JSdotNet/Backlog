namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>One chapter the inspector lists under "Documented in arc42".</summary>
/// <param name="Path">The chapter, as the host opens it — handed back unchanged when
/// the reader picks it.</param>
/// <param name="Number">The number its file name leads with — "03" — or null for a
/// file with none.</param>
/// <param name="Title">What the reader sees: the chapter's title, and the section the
/// reference names after a "›" when it names one.</param>
public sealed record C4InspectorChapter(string Path, string? Number, string Title);

/// <summary>Which of the two record lists an entry belongs in. The host says, by the
/// folder the record sits in; the inspector never guesses from a title.</summary>
public enum C4InspectorRecordKind
{
    /// <summary>A decision record (ADR).</summary>
    Decision,

    /// <summary>A debt record (TDR).</summary>
    Debt
}

/// <summary>One decision or debt record that names the selected element.</summary>
/// <param name="Path">The record, as the host opens it.</param>
/// <param name="Number">The number its file name leads with — "0030".</param>
/// <param name="Title">The record's title.</param>
/// <param name="Proposed">A decision whose state is <c>proposed</c>; drawn as the
/// "proposed" chip. Always false for debt.</param>
/// <param name="Severity">A debt record's severity as a reader says it — "Medium" —
/// or null.</param>
/// <param name="Lifecycle">A debt record's rung on the debt ladder as a reader says it
/// — "identified", "in progress" — or null.</param>
/// <param name="Touches">Named in the record's <c>c4-touches</c> rather than its
/// <c>c4</c>: its consequences reach the element, its choice is not about it.</param>
public sealed record C4InspectorRecord(
    string Path,
    string? Number,
    string Title,
    C4InspectorRecordKind Kind,
    bool Proposed = false,
    string? Severity = null,
    string? Lifecycle = null,
    bool Touches = false)
{
    /// <summary>A debt record not yet at the ladder's last rung, <c>resolved</c>: what
    /// the Debt overlay counts and the cross-cutting tray lists. Always false for a
    /// decision.</summary>
    public bool OpenDebt =>
        Kind == C4InspectorRecordKind.Debt && !string.Equals(Lifecycle?.Trim(), "resolved", StringComparison.OrdinalIgnoreCase);

    /// <summary>A decision whose state is <c>accepted</c>: what the inspector's
    /// "n accepted" tally counts. Always false for debt.</summary>
    public bool Accepted { get; init; }

    /// <summary>The name of the component inside the inspected element that the record
    /// names, when the record is listed on a container for a component of it — "On
    /// component Devbook". Null for a record that names the element itself.</summary>
    public string? OnComponent { get; init; }

    /// <summary>What a debt record's file says beyond its title, for the inspector's
    /// debt card; null for a decision, or a host that read no file.</summary>
    public C4InspectorDebtDetail? Detail { get; init; }
}

/// <summary>
/// What a debt record's file says, as the inspector's debt card shows it on the element
/// the record names.
/// </summary>
/// <param name="Summary">The debt in a sentence or two: the first paragraph under its
/// <c>## The debt</c> heading, or null.</param>
/// <param name="Remediation">The remediation option the record prefers, by its heading
/// — "Option C — drop the exclusion" — or null when it marks none.</param>
/// <param name="Origin">The decisions its <c>## Origin</c> names, each a way to open
/// that record.</param>
public sealed record C4InspectorDebtDetail(string? Summary, string? Remediation, IReadOnlyList<C4InspectorLink> Origin)
{
    public static C4InspectorDebtDetail Empty { get; } = new(null, null, []);
}

/// <summary>One line into or out of the inspected element on the view it is selected
/// on, as the inspector's "Relationships · n" lists it.</summary>
/// <param name="Other">The name of the element at the line's other end.</param>
/// <param name="Outgoing">Whether the line leaves the inspected element.</param>
/// <param name="Description">What the line says — "Polls" — or null.</param>
/// <param name="Technology">What it travels over — "HTTPS" — or null.</param>
public sealed record C4InspectorRelationship(string Other, bool Outgoing, string? Description, string? Technology);

/// <summary>A record named in another's text, as the inspector links to it.</summary>
/// <param name="Path">The record, as the host opens it.</param>
/// <param name="Label">What the reader sees — "ADR 0008".</param>
public sealed record C4InspectorLink(string Path, string Label);

/// <summary>Which records the zoom tab's overlay draws on the cards.</summary>
public enum C4RecordOverlay
{
    /// <summary>No records on the cards: the picture as it is drawn without one.</summary>
    None,

    /// <summary>An <c>n ADR</c> chip on each card that decision records name.</summary>
    Decisions,

    /// <summary>An <c>n TDR</c> chip, on the debt surface, on each card that open debt
    /// records name, and the cross-cutting tray of open debt that names no element.</summary>
    Debt
}

/// <summary>
/// What the knowledge folders say about one element, as the inspector shows it.
/// <para>
/// The component library has no idea what a chapter is, so the host works this out —
/// the views map for <see cref="DocumentedIn"/>, the chapters' <c>c4</c> and
/// <c>c4-touches</c> fields for the records — and hands it over per element.
/// </para>
/// </summary>
public sealed record C4ElementRecords(
    IReadOnlyList<C4InspectorChapter> DocumentedIn,
    IReadOnlyList<C4InspectorRecord> Decisions,
    IReadOnlyList<C4InspectorRecord> Debt)
{
    public static C4ElementRecords None { get; } = new([], [], []);
}
