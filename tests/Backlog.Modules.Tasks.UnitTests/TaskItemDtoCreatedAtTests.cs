using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The DTO publishes when the entry was created.
/// <para>
/// The aggregate has stamped <see cref="TaskItem.CreatedAt"/> since the sync
/// stamps arrived, and the store has kept it in its own column just as long, but
/// the record the screens read left it out on purpose — nothing read it. A pane
/// now does, so the mapper has to carry it across, and a mapper that drops it is
/// a pane that can only say "Created" with nothing after it.
/// </para>
/// </summary>
public sealed class TaskItemDtoCreatedAtTests
{
    private static readonly DateTimeOffset Stamped = new(2026, 3, 9, 14, 22, 0, TimeSpan.Zero);

    [Fact]
    public void ToDto_carries_the_creation_stamp()
    {
        var entry = new TaskItem(
            Guid.NewGuid(),
            "Write release notes",
            "body",
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: null,
            createdAt: Stamped);

        Assert.Equal(Stamped, entry.ToDto().CreatedAt);
    }

    /// <summary>A save reloads the aggregate and applies the parsed text on top,
    /// and the stamp is a column rather than a token, so an edit cannot move it.
    /// This pins that from the DTO's side: the same entry, renamed, still says it
    /// was created when it was created.</summary>
    [Fact]
    public void Editing_the_entry_leaves_the_creation_stamp_alone()
    {
        var entry = new TaskItem(
            Guid.NewGuid(),
            "Write release notes",
            "body",
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: null,
            createdAt: Stamped);

        entry.Rename("Write the release notes");
        entry.ChangeStatus(EntryStatus.Ready);

        Assert.Equal(Stamped, entry.ToDto().CreatedAt);
        Assert.NotEqual(Stamped, entry.UpdatedAt);
    }
}
