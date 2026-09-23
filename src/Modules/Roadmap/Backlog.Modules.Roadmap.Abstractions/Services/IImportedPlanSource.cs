using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// The plans the backlog holds because somebody imported them — one per distinct
/// <c>import_plan_id</c> — and what their tasks registered.
/// <para>
/// A port on Roadmap Planning's own surface, answered by an infrastructure adapter
/// that reads Tasks, for the same reason <see cref="IRoadmapItemRollup"/> is: the
/// band may not reach into another context
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// It is what the shelf of plans not yet on the roadmap is built from, the
/// tasks-first order of ADR 0013, ruling 3.
/// </para>
/// <para>
/// Only a plan identified by a plan tag — one written with the <c>+</c> sigil — is
/// answered. A legacy plan filed under a general tag still gathers under an item
/// whose tag matches it, but the shelf does not offer it
/// (<c>.domain/roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first</c>).
/// </para>
/// </summary>
public interface IImportedPlanSource
{
    /// <summary>Every imported plan in the backlog, in the order its first task
    /// appears there.</summary>
    Task<IReadOnlyList<ImportedPlanDto>> ListAsync(CancellationToken cancellationToken = default);
}
