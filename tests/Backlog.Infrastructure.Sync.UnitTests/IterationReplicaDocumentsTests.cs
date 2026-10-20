using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// How the iteration settings are written on the task feed (local ADR 0036, §1): one
/// constant id, the kind token <c>iteration-settings</c>, the file's JSON verbatim as the
/// content, no My Day date of its own, and never a tombstone.
/// </summary>
public sealed class IterationReplicaDocumentsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private const string SettingsJson = """{ "updatedAt": "2026-10-20T08:00:00Z", "length": "two-weeks", "twoWeekStart": "2026-10-19" }""";

    [Fact]
    public void The_settings_travel_as_one_task_shaped_document()
    {
        var change = IterationReplicaDocuments.ToChange(new IterationSettingsCopyDto(SettingsJson, Morning));

        Assert.Equal(Guid.Parse("7c3e1f5a-9d2b-4a6e-b8f0-3e5d7a9c1b36"), change.Id);
        Assert.Equal(Morning, change.UpdatedAt);
        Assert.Null(change.DeletedAt);
        Assert.Equal("iteration-settings", change.Task.Type);
        Assert.Equal("Iteration settings", change.Task.Title);
        Assert.Equal(SettingsJson, change.Task.ContentMd);
        Assert.Equal("draft", change.Task.Status);
        Assert.Equal("medium", change.Task.Priority);
        Assert.Null(change.Task.InMyDayOn);
    }

    [Fact]
    public void A_pulled_document_is_the_copy_it_was_sent_as()
    {
        var sent = new IterationSettingsCopyDto(SettingsJson, Morning);

        Assert.Equal(sent, IterationReplicaDocuments.ToCopy(IterationReplicaDocuments.ToChange(sent)));
    }

    [Fact]
    public void Only_the_settings_document_is_recognised_as_one()
    {
        Assert.True(IterationReplicaDocuments.Is(
            IterationReplicaDocuments.ToChange(new IterationSettingsCopyDto(SettingsJson, Morning))));
        Assert.False(IterationReplicaDocuments.Is(TaskChanges.Change("Plan the iteration", Morning)));
        Assert.False(IterationReplicaDocuments.Is(
            MyDayReplicaDocuments.ToChange(new Backlog.Modules.Tasks.Abstractions.DataTransferObjects.MyDayPreferencesCopyDto("{}", Morning))));
    }
}
