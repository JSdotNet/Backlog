using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The port the sync client hands replicated goals to, and reads them from: one whole
/// <c>goal</c> document per goal, the newer write winning per goal
/// (<c>.devbook/domain/progress/features.md#syncing-goals-and-impediments</c>, local ADR
/// 0035 §9) — the terms the roadmap's plan travels on (local ADR 0018).
/// <para>
/// A goal is the unit because it is the consistency boundary for its attachments. The
/// client routes by kind and never looks inside; which copy wins, and whether a copy can
/// be read at all, is decided here. A deleted goal travels as its tombstone.
/// </para>
/// </summary>
public interface IGoalReplication : IWholeDocumentReplication<GoalReplicaCopyDto>;
