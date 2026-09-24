namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// One task the person ticked off, reduced to what a weekly figure is worked out from.
/// </summary>
/// <param name="CompletedOn">The day it was ticked off. A day rather than an instant,
/// because that is all the task records — and the only dated completion fact it has:
/// a status of Done carries no date.</param>
/// <param name="Effort">Its estimate in story points, or null where nobody estimated
/// it. Null and never 0 standing in for absent, on <see cref="AssistantSession.Prompts"/>'
/// rule: 0 is a real estimate meaning trivial.</param>
/// <param name="RepositoryAliases">The repositories it targets, by alias — what
/// <see cref="RepositoryFocus"/> is matched against. Empty for a task that names none.</param>
public sealed record CompletedTask(DateOnly CompletedOn, int? Effort, IReadOnlyList<string> RepositoryAliases);

/// <summary>
/// PORT — the tasks this installation has ticked off.
/// <para>
/// A horizon and no repository in the signature, on <see cref="IAssistantSessionSource"/>'s
/// pattern: the repository scope is a derivation over one read, so moving the header's
/// chips costs no read of its own. Nothing in this contract names a Tasks type; the
/// adapter that answers it may see both contexts, nothing in this module may.
/// </para>
/// </summary>
public interface ICompletedTaskSource
{
    /// <summary>Every task ticked off on or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<CompletedTask>> GetCompletedAsync(DateOnly since, CancellationToken cancellationToken = default);
}
