using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// What one field changed across a set of rows actually did.
/// <para>
/// Three numbers rather than a bool, because a bulk edit has three honest
/// outcomes and a reader is owed all of them: rows that were written, rows that
/// were already at the value asked for, and rows the store refused. "It worked"
/// over twenty rows where nineteen were skipped would be the pane claiming work
/// it did not do.
/// </para>
/// <para>
/// A value and never an exception
/// (<c>.arc42/adr/guidelines/0004-result-objects-for-expected-failures.md</c>).
/// A refusal on row seven is a thing the caller is expected to handle — it does
/// not stop rows eight to twenty, and it does not unwind the seven already
/// saved, because there is no transaction over N saves here and pretending
/// otherwise would be worse than reporting the truth.
/// </para>
/// </summary>
/// <param name="Updated">Rows whose text was rewritten and saved.</param>
/// <param name="Unchanged">Rows already at the value asked for. Skipped rather
/// than saved again, the same skip-if-unchanged the reorder and
/// repository-reconcile handlers do.</param>
/// <param name="Failures">Rows the store would not take, one entry each.</param>
public sealed record BulkEditOutcome(
    int Updated,
    int Unchanged,
    IReadOnlyList<BulkEditFailure> Failures)
{
    /// <summary>Nothing was selected, so nothing happened. Every bulk method is
    /// safe to call over an empty selection and this is what it answers with.</summary>
    public static readonly BulkEditOutcome Nothing = new(0, 0, []);

    /// <summary>How many rows the change was asked about.</summary>
    public int Total => Updated + Unchanged + Failures.Count;
}

/// <summary>One row the store would not take, and why. The row is named by the
/// same id the list addresses it by, and its title travels along so a message
/// can say which entry it was without resolving anything.</summary>
/// <param name="Id">The row, as the list names it.</param>
/// <param name="Title">What the row is called, for a sentence a reader can act on.</param>
/// <param name="Error">Why, as data.</param>
public readonly record struct BulkEditFailure(string Id, string Title, Error Error);
