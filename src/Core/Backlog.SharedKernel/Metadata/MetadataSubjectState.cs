namespace Backlog.SharedKernel.Metadata;

/// <summary>
/// A devbook subject's state as a record's headline draws it: a decision record's
/// or a debt record's state, who set it and on which day.
///
/// <para>Not part of <see cref="MetadataState"/>, which is what the block in the
/// file says. Under devbook's branch-true decision a subject's workflow state is
/// kept by the tool rather than in git, so it never comes from the
/// <c>meta</c> fence: a panel reads it from the subject store and hands it down,
/// already in words. That keeps the component library ignorant of the store,
/// which it cannot reference, and keeps the "you" a panel says for its own host
/// the panel's business.</para>
///
/// <para>Every value is display text. <see cref="Options"/> is what turns the
/// state into a choice: empty for a state that is shown and not set here — a
/// decision record's, whose key needs the pull request it was decided in.</para>
/// </summary>
/// <param name="Kind">The subject's kind as the contract names it —
/// <c>decision</c>, <c>debt</c> — which leads the statement.</param>
/// <param name="State">The state, or null for a subject nobody has set one
/// on.</param>
/// <param name="By">Who set it, as the reader should see them.</param>
/// <param name="At">The day it was set, <c>YYYY-MM-DD</c>.</param>
public sealed record MetadataSubjectState(string Kind, string? State, string? By = null, string? At = null)
{
    /// <summary>The states a reader may pick, in workflow order; empty where the
    /// state is read-only.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Whether a state has been set at all.</summary>
    public bool HasState => !string.IsNullOrWhiteSpace(State);

    /// <summary>Whether the state may be changed where it is drawn.</summary>
    public bool IsSelectable => Options.Count > 0;

}
