namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// What bringing in a plan produced. Counts rather than a single number, because
/// the five outcomes answer different questions for the person who just ran an
/// import: a create is new work landing in the backlog, a replacement is a
/// prompt they already had rewritten from this version, an update is a prompt
/// already under way catching up, a skip is a prompt the plan mentions that has
/// already been finished and is deliberately left alone, and a removal is a
/// prompt nobody had started that this version of the plan no longer asks for.
/// <para>
/// A replacement is a create as far as storage is concerned — the entry it
/// stood in for was tombstoned and this one is new — but reporting it that way
/// would tell somebody re-importing an unchanged plan that every prompt in it
/// was created and removed at once, which is true and useless. The two are told
/// apart by <c>import_item_id</c>: a new entry carrying the id of one just
/// cleared is the same prompt, written again.
/// </para>
/// </summary>
public sealed record ImportPlanResultDto(
    int Created,
    int Replaced,
    int Updated,
    int Skipped,
    int Removed,
    IReadOnlyList<TaskItemDto> Entries);
