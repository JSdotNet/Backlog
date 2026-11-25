using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The refusal every task write answering a <see cref="Result"/> gives when the
/// store holds another version of the entry than the one the write was made on
/// (<see cref="TaskEntryErrorCodes.StaleWrite"/>).
/// <para>
/// Published rather than kept in the module, because a write the caller pins to the
/// version it read (<see cref="ITaskItems.GetUpdatedAtAsync"/>) is refused by the
/// port's own default as well as by the module — one wording for both.
/// </para>
/// </summary>
public static class StaleTaskWrites
{
    /// <summary>A single-entry write wrote nothing; a write over many entries — an
    /// import, a repository rename — stopped at that entry, with the ones before it
    /// written, as any failure part-way through those always has.</summary>
    public static readonly Error StaleWrite = Error.Conflict(
        TaskEntryErrorCodes.StaleWrite,
        "This entry changed somewhere else while the change was being made, so nothing was written. "
        + "Read the entry again, then retry the change on the version you read.");
}
