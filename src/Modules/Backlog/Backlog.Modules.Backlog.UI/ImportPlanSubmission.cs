namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// What the import dialog hands back when somebody presses Import: the plan
/// text itself — from the paste box or the file it was read into — plus the
/// optional default repository the dialog's own field carries.
/// <para>
/// <see cref="DefaultRepo"/> is a UI convenience, not a grammar change — see
/// ADR 0004. It is applied only to an entry whose own text names no
/// <c>repo:</c> of its own; the token stays the per-entry override for a plan
/// that spans more than one repository.
/// </para>
/// </summary>
/// <param name="RawText">The plan's raw text.</param>
/// <param name="DefaultRepo">The dialog's "Target repository" field, or null
/// when left blank.</param>
public sealed record ImportPlanSubmission(string RawText, string? DefaultRepo);
