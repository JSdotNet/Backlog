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
/// <param name="Proposed">A decision still at <c>status: proposed</c>; drawn as the
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
    bool Touches = false);

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
