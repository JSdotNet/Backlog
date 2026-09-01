namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// What the import dialog hands back when somebody presses Import: the plan
/// text itself — from the paste box or the file it was read into — plus the
/// optional default repository the dialog's own field carries and whatever the
/// reader said about the repository names the plan mentions.
/// <para>
/// <see cref="DefaultRepo"/> is a UI convenience, not a grammar change — see
/// ADR 0004. It is applied only to an entry whose own text names no
/// <c>repo:</c> of its own; the token stays the per-entry override for a plan
/// that spans more than one repository.
/// </para>
/// <para>
/// <see cref="RepoMatches"/> is the multi-repository half of the same bargain.
/// A plan spanning several repositories names them however its author wrote
/// them, and the dialog offers to match each one to a repository the workspace
/// already knows. Only the names somebody actually matched travel here; a name
/// they left alone is resolved — and if nothing recognises it, registered — by
/// the module, which stays the sole authority on what a <c>repo:</c> means.
/// </para>
/// </summary>
/// <param name="RawText">The plan's raw text.</param>
/// <param name="DefaultRepo">The dialog's "Target repository" field, or null
/// when left blank.</param>
/// <param name="RepoMatches">The plan-text name mapped to the alias of the known
/// repository the reader picked for it, or null when they matched none.</param>
public sealed record ImportPlanSubmission(
    string RawText,
    string? DefaultRepo,
    IReadOnlyDictionary<string, string>? RepoMatches = null);
