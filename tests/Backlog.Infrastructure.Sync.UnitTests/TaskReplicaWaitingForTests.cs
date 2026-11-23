using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A task's waiting-for rides the replica document as two plain members of its
/// own, because the <c>waiting:</c> and <c>followup:</c> tokens live on the
/// metadata line, which never crosses the wire (local ADR 0054 §4).
/// </summary>
public sealed class TaskReplicaWaitingForTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_waiting_for_crosses_the_wire_and_an_older_document_reads_as_waiting_on_nobody()
    {
        var waiting = TaskChanges.Task("Fix the roof", Noon);
        waiting.SetWaitingFor(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));
        waiting.LoadStamps(Noon, null);

        var payload = TaskReplicaMerge.ToPayload(waiting);
        Assert.Equal("Jansen Roofing", payload.WaitingFor);
        Assert.Equal(new DateOnly(2026, 11, 20), payload.FollowUpOn);

        var restored = TaskReplicaMerge.ToTaskItem(TaskReplicaMerge.ToChange(waiting));
        Assert.Equal(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)), restored.WaitingFor);
        Assert.Equal(Noon, restored.UpdatedAt);

        var older = TaskReplicaMerge.ToTaskItem(new TaskChange(waiting.Id, Noon, null, payload with { WaitingFor = null, FollowUpOn = null }));
        Assert.Null(older.WaitingFor);
    }

    /// <summary>Through the JSON the service stores and back: the same waiting-for on
    /// the other device.</summary>
    [Fact]
    public void A_waiting_for_survives_the_json_document()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var waiting = TaskChanges.Task("Fix the roof", Noon);
        waiting.SetWaitingFor(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));
        waiting.LoadStamps(Noon, null);

        var json = JsonSerializer.Serialize(TaskReplicaMerge.ToPayload(waiting), options);
        var read = JsonSerializer.Deserialize<TaskPayload>(json, options)!;
        var restored = TaskReplicaMerge.ToTaskItem(new TaskChange(waiting.Id, Noon, null, read));

        Assert.Equal(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)), restored.WaitingFor);
    }

    /// <summary>A blank name in a hand-edited document costs the waiting-for, not the
    /// task.</summary>
    [Fact]
    public void A_document_naming_nobody_reads_as_waiting_on_nobody()
    {
        var task = TaskChanges.Task("Fix the roof", Noon);
        var payload = TaskReplicaMerge.ToPayload(task) with { WaitingFor = "  ", FollowUpOn = new DateOnly(2026, 11, 20) };

        Assert.Null(TaskReplicaMerge.ToTaskItem(new TaskChange(task.Id, Noon, null, payload)).WaitingFor);
    }
}
