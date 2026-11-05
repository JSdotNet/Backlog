using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The workflow state a decision or debt record is in, as Backlog's subject store
/// holds it, laid over the record as its file reads.
/// <para>
/// Local ADR 0044 moved a record's workflow state out of the chapter and into the
/// subject store; the file header draws it from there (§5). The C4 surface says the
/// same thing in more places — a debt record's rung in the left navigation, on the
/// record panel's ladder, in the inspector and the cross-cutting tray, a decision's
/// "proposed" mark on each of those — so it reads the same store, and the record's
/// authored <c>status</c> only where the store holds nothing (local ADR 0045). From
/// devbook's contract 38 a record states none, so that fallback is the resting read,
/// no "proposed" mark and the <c>identified</c> rung; a repository on an older
/// contract still states one.
/// </para>
/// <para>
/// A debt record is keyed by its path. A decision record is keyed by its path and the
/// pull request it is decided in, so the state shown is the one most recently set for
/// that path under any pull request — what the header shows too. A path is matched as
/// the store spells it, exactly, as the header matches it.
/// </para>
/// </summary>
public sealed class C4RecordStates
{
    private readonly IReadOnlyDictionary<string, string> _byPath;

    private C4RecordStates(IReadOnlyDictionary<string, string> byPath) => _byPath = byPath;

    /// <summary>No state stored for any record: every record reads as its file says.</summary>
    public static C4RecordStates None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Whether any record has a stored state.</summary>
    public bool IsEmpty => _byPath.Count == 0;

    /// <summary>The decision and debt states <paramref name="states"/> holds, the most
    /// recently set one per record path.</summary>
    public static C4RecordStates From(IEnumerable<DevbookSubjectState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var latest = states
            .Where(state => state.Subject.Kind is DevbookSubjectKind.Debt or DevbookSubjectKind.Decision
                && !string.IsNullOrWhiteSpace(state.State))
            .GroupBy(state => state.Subject.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(state => state.UpdatedAt)!.State.Trim(),
                StringComparer.Ordinal);

        return latest.Count == 0 ? None : new C4RecordStates(latest);
    }

    /// <summary>Whether <paramref name="other"/> says the same state for the same
    /// records — a store change about something else, a proposal or another
    /// repository, leaves the surface's reads as they were.</summary>
    public bool SameAs(C4RecordStates other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return ReferenceEquals(this, other)
            || (_byPath.Count == other._byPath.Count
                && _byPath.All(pair => other._byPath.TryGetValue(pair.Key, out var state) && string.Equals(state, pair.Value, StringComparison.Ordinal)));
    }

    /// <summary>The stored state of the record at <paramref name="path"/>, or null when
    /// the store holds none and the file's own <c>status</c> stands.</summary>
    public string? StateOf(string path) =>
        _byPath.TryGetValue(DevbookChapterKey.Normalize(path), out var state) ? state : null;

    /// <summary>The record with its stored state in place of its authored
    /// <c>status</c>; the record itself where nothing is stored, or it is no decision
    /// or debt record.</summary>
    public DevbookDocument Apply(DevbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!IsRecord(document.Path) || StateOf(document.Path) is not { } state) return document;

        return document with { Metadata = document.Metadata with { Status = state } };
    }

    /// <summary><see cref="Apply(DevbookDocument)"/> over a list; the list itself
    /// when no record in it has a stored state.</summary>
    public IReadOnlyList<DevbookDocument> Apply(IReadOnlyList<DevbookDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return IsEmpty ? documents : [.. documents.Select(Apply)];
    }

    /// <summary>The folder with each record's stored state applied; the folder itself
    /// when nothing is stored.</summary>
    public Arc42DevbookCatalog Apply(Arc42DevbookCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return IsEmpty ? catalog : catalog with { Documents = Apply(catalog.Documents) };
    }

    /// <summary>A record as the model's links carry it, with its stored state in
    /// place of the one its file states: a debt record's rung, a decision's
    /// status.</summary>
    public C4LinkedRecord Apply(C4LinkedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (StateOf(record.Path) is not { } state) return record;

        return record.Kind switch
        {
            C4RecordKind.Debt => record with { Lifecycle = new DevbookMeta(state, []).DebtLifecycle },
            C4RecordKind.Decision => record with { Status = state },
            _ => record
        };
    }

    private static bool IsRecord(string path) =>
        Arc42DevbookCatalog.IsDebtRecord(path) || Arc42DevbookCatalog.IsArchitectureDecision(path);
}
