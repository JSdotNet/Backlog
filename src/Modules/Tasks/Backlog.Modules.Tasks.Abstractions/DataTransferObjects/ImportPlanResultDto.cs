namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// What bringing in a plan produced. Counts rather than a single number, because
/// the three outcomes mean different things to the person who just ran an
/// import: a create is new work landing in the backlog, an update is a plan
/// they already had catching up to a later version, and a skip is a prompt the
/// plan mentions that has already been finished and is deliberately left alone.
/// </summary>
public sealed record ImportPlanResultDto(
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<TaskItemDto> Entries);
