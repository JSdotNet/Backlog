using System.Reflection;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Which mutations restamp <see cref="TaskItem.UpdatedAt"/>, and which do not.
/// <para>
/// The stamp is what last-write-wins compares when the same task has been edited
/// on two of the person's machines, so a mutator that forgets to restamp is an
/// edit the other machine never learns about - and one that restamps when it
/// should not is a machine that wins with no edit to show for it. There is
/// nowhere to enforce this centrally: this context has no mediator, no change
/// tracker, and no domain events, so every mutator calls <c>Touch</c> by hand.
/// </para>
/// <para>
/// Hence an explicit inventory rather than a reflection sweep that invents
/// arguments. Half the mutators need a sub-item that exists or refuse the value
/// they are handed, so a sweep would either carry an argument table - a
/// hand-written inventory wearing a reflection costume - or silently skip what it
/// could not call, which is exactly the regression it was bought to catch. The
/// reflection assertion at the bottom does the one job reflection is good for
/// here: proving the inventory is complete, so a mutator added later cannot go in
/// without somebody deciding which list it belongs on.
/// </para>
/// <para>
/// The theory data is the case name alone rather than the delegate, so the cases
/// stay serializable and a failure names the mutator that broke.
/// </para>
/// </summary>
public sealed class TaskItemStampTests
{
    private static readonly DateTimeOffset Stamped = new(2026, 1, 5, 8, 30, 0, TimeSpan.Zero);

    /// <summary>Every mutation a person or an agent can make. The stamp has to move
    /// for all of them.</summary>
    private static readonly Dictionary<string, Action<TaskItem>> Edits = new(StringComparer.Ordinal)
    {
        [nameof(TaskItem.Rename)] = task => task.Rename("Renamed"),
        [nameof(TaskItem.UpdateContent)] = task => task.UpdateContent("Body"),
        [nameof(TaskItem.ChangeType)] = task => task.ChangeType(EntryType.Idea),
        [nameof(TaskItem.ChangePriority)] = task => task.ChangePriority(Priority.High),
        [nameof(TaskItem.SetOrder)] = task => task.SetOrder(7),
        [nameof(TaskItem.SetArea)] = task => task.SetArea("projects"),
        [nameof(TaskItem.SetEffort)] = task => task.SetEffort(5),
        [nameof(TaskItem.SetRepoIds)] = task => task.SetRepoIds(["JSdotNet/Backlog"]),
        [nameof(TaskItem.SetTags)] = task => task.SetTags(["urgent"]),
        [nameof(TaskItem.SetImportPlanId)] = task => task.SetImportPlanId("plan"),
        [nameof(TaskItem.SetImportItemId)] = task => task.SetImportItemId("item"),
        [nameof(TaskItem.SetDueOn)] = task => task.SetDueOn(new DateOnly(2026, 2, 1)),
        [nameof(TaskItem.SetReminder)] = task => task.SetReminder(new DateTime(2026, 2, 1, 9, 0, 0)),
        [nameof(TaskItem.SetRecurrence)] = task => task.SetRecurrence(null),
        [nameof(TaskItem.SetInMyDayOn)] = task => task.SetInMyDayOn(new DateOnly(2026, 2, 1)),
        [nameof(TaskItem.SetView)] = task => task.SetView(EntryView.Notes),
        [nameof(TaskItem.SetAttachment)] = task => task.SetAttachment(null),
        [nameof(TaskItem.SetDependsOn)] = task => task.SetDependsOn(["other"]),

        // Clearing the list is still an edit. Its own case because the null
        // argument used to leave through an early return, which made "all
        // dependencies removed" the single edit that did not restamp - and
        // therefore the single edit that never travelled.
        ["SetDependsOn(null)"] = task => task.SetDependsOn(null),

        [nameof(TaskItem.SetStatus)] = task => task.SetStatus(EntryStatus.Done),
        [nameof(TaskItem.ChangeStatus)] = task => task.ChangeStatus(EntryStatus.Ready),
        [nameof(TaskItem.MarkDeleted)] = task => task.MarkDeleted(),
        [nameof(TaskItem.AddSubItem)] = task => task.AddSubItem("Step"),
        [nameof(TaskItem.AddProjectionRef)] = task => task.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "42", "issue")),
        [nameof(TaskItem.RemoveSubItem)] = task => task.RemoveSubItem(FirstSubItem(task)),
        [nameof(TaskItem.ToggleSubItem)] = task => task.ToggleSubItem(FirstSubItem(task)),
        [nameof(TaskItem.SetSubItemStatus)] = task => task.SetSubItemStatus(FirstSubItem(task), SubItemStatus.Done),
        [nameof(TaskItem.UpdateSubItem)] = task => task.UpdateSubItem(FirstSubItem(task), "Retitled", null),
        [nameof(TaskItem.ReorderSubItem)] = task => task.ReorderSubItem(FirstSubItem(task), 0),
    };

    /// <summary>The mutations that must leave the stamp where it is, each for its
    /// own stated reason.</summary>
    private static readonly Dictionary<string, Action<TaskItem>> NonEdits = new(StringComparer.Ordinal)
    {
        // Using a task is not the task changing. A usage event is an immutable
        // audit record of a prompt being copied or used, and its only caller is
        // the desktop handing an entry to the Copilot CLI - so restamping here
        // would let that gesture on one machine beat a genuine edit made on the
        // other. The accepted cost: a usage event recorded on its own never
        // travels, and can be overwritten on its own device by a later inbound
        // document.
        [nameof(TaskItem.RecordUsage)] = task => task.RecordUsage("copilot-cli"),

        // Storage replaying what it stored. Neither is an edit, and treating one
        // as an edit would mean every task looked freshly changed the moment it
        // loaded.
        [nameof(TaskItem.LoadSubItem)] = task =>
            task.LoadSubItem(task.CreateSubItemForLoad(Guid.NewGuid(), "Loaded", SubItemStatus.Pending, null, 0)),
        [nameof(TaskItem.LoadUsageEvent)] = task => task.LoadUsageEvent(new UsageEvent(Stamped, "copilot-cli")),
        [nameof(TaskItem.LoadStamps)] = task => task.LoadStamps(Stamped, deletedAt: null),
    };

    public static TheoryData<string> EditNames => [.. Edits.Keys];

    public static TheoryData<string> NonEditNames => [.. NonEdits.Keys];

    [Theory]
    [MemberData(nameof(EditNames))]
    public void An_edit_moves_the_stamp(string mutator)
    {
        var task = Stored();

        Edits[mutator](task);

        Assert.True(
            task.UpdatedAt > Stamped,
            $"{mutator} left UpdatedAt at {task.UpdatedAt:O}. An edit that does not restamp is an edit the person's other machine never learns about.");
    }

    [Theory]
    [MemberData(nameof(NonEditNames))]
    public void A_non_edit_leaves_the_stamp_alone(string mutator)
    {
        var task = Stored();

        NonEdits[mutator](task);

        Assert.Equal(Stamped, task.UpdatedAt);
    }

    /// <summary>A task is born already stamped, and stamped from its creation
    /// rather than from the clock. Left at the default it would persist
    /// 0001-01-01 and lose every tie-break to the oldest edit on the other
    /// machine.</summary>
    [Fact]
    public void A_new_task_is_stamped_from_its_creation()
    {
        var task = Stored();

        Assert.Equal(task.CreatedAt, task.UpdatedAt);
        Assert.Null(task.DeletedAt);
    }

    /// <summary>
    /// The guard on the two lists above. It fails when a public instance method
    /// that could mutate the aggregate is added without appearing on either list,
    /// which is the real risk here: not that somebody decides wrongly, but that
    /// nobody decides at all.
    /// </summary>
    [Fact]
    public void Every_public_mutator_is_on_one_of_the_two_lists()
    {
        var declared = Edits.Keys.Concat(NonEdits.Keys)
            .Select(name => name.Split('(')[0])
            .ToHashSet(StringComparer.Ordinal);

        var undecided = typeof(TaskItem)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Where(name => !NotAMutation.Contains(name))
            .Where(name => !declared.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undecided.Count == 0,
            $"TaskItem gained {string.Join(", ", undecided)} without deciding whether it restamps UpdatedAt. "
            + "Add it to Edits or to NonEdits, and give the reason if it is the latter.");
    }

    /// <summary>Public instance methods that change nothing, so there is no stamp
    /// decision to make about them. Listed by hand and kept short on purpose: an
    /// over-broad exclusion here is how a real mutator slips past the guard
    /// above.</summary>
    private static readonly HashSet<string> NotAMutation = new(StringComparer.Ordinal)
    {
        // Asks a question about the lifecycle.
        nameof(TaskItem.CanChangeStatusTo),

        // Builds a sub-item for storage to hand straight back to LoadSubItem, and
        // touches nothing on this task on the way.
        nameof(TaskItem.CreateSubItemForLoad),

        // object's own.
        nameof(Equals),
        nameof(GetHashCode),
        nameof(ToString),
        nameof(GetType),
    };

    /// <summary>A task whose stamp is far enough in the past that "now" could
    /// never be mistaken for it, with one sub-item so the sub-item mutators have
    /// something real to act on.</summary>
    private static TaskItem Stored()
    {
        var task = new TaskItem(
            Guid.NewGuid(),
            "Stored",
            string.Empty,
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: null,
            createdAt: Stamped);

        task.LoadSubItem(task.CreateSubItemForLoad(Guid.NewGuid(), "Step", SubItemStatus.Pending, null, 0));

        return task;
    }

    private static Guid FirstSubItem(TaskItem task) => task.SubItems[0].Id;
}
