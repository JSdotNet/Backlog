namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// The error codes a host has to recognize by name, rather than merely show.
/// <para>
/// Guideline 0004 gives an <c>Error</c> a stable machine-readable code precisely
/// so a host can branch on it, and this is the published half of that contract:
/// the handlers that raise these live in the implementation project, which the
/// desktop side may not see (<c>ModuleSurfaceTests</c>). Without a shared
/// constant the branch would be a literal typed twice, in two projects, with
/// nothing to fail when one of them was edited.
/// </para>
/// <para>
/// Only codes a host must act on differently belong here. A code it merely
/// displays needs no constant — the message is already in the error.
/// </para>
/// </summary>
public static class TaskEntryErrorCodes
{
    /// <summary>
    /// Entry text whose type word is <c>plan</c> reached a path that saves a
    /// task. Import is the only way a roadmap item may come in (ADR 0013 ruling
    /// 2), so this is a refusal rather than the half-typed state most failed
    /// saves are — and the editor, which holds those quietly, has to tell the
    /// person about this one.
    /// </summary>
    public const string PlanBelongsToImport = "entry.plan_belongs_to_import";
}
