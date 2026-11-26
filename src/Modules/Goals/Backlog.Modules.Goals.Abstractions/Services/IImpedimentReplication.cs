using Backlog.Modules.Goals.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Goals.Abstractions.Services;

/// <summary>
/// The port the sync client hands replicated impediments to, and reads them from: one
/// whole <c>impediment</c> document per impediment, the newer write winning per document
/// (<c>.devbook/domain/goals/features.md#syncing-goals-and-impediments</c>, local ADR
/// 0035 §9) — the terms <see cref="IGoalReplication"/> gives a goal.
/// </summary>
public interface IImpedimentReplication : IWholeDocumentReplication<ImpedimentReplicaCopyDto>;
