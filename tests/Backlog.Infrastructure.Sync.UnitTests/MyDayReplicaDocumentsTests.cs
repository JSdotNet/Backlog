using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// How the My Day preferences are written on the task feed (local ADR 0032, §1): one
/// constant id, the kind token <c>my-day-preferences</c>, the file's JSON verbatim as
/// the content, no My Day date of its own, and never a tombstone.
/// </summary>
public sealed class MyDayReplicaDocumentsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private const string PreferencesJson = """{ "updatedAt": "2026-10-15T08:00:00Z", "clearsOvernight": false }""";

    [Fact]
    public void The_preferences_travel_as_one_task_shaped_document()
    {
        var change = MyDayReplicaDocuments.ToChange(new MyDayPreferencesCopyDto(PreferencesJson, Morning));

        Assert.Equal(Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01"), change.Id);
        Assert.Equal(Morning, change.UpdatedAt);
        Assert.Null(change.DeletedAt);
        Assert.Equal("my-day-preferences", change.Task.Type);
        Assert.Equal("My Day preferences", change.Task.Title);
        Assert.Equal(PreferencesJson, change.Task.ContentMd);
        Assert.Equal("draft", change.Task.Status);
        Assert.Equal("medium", change.Task.Priority);
        Assert.Null(change.Task.InMyDayOn);
    }

    [Fact]
    public void A_pulled_document_is_the_copy_it_was_sent_as()
    {
        var sent = new MyDayPreferencesCopyDto(PreferencesJson, Morning);

        Assert.Equal(sent, MyDayReplicaDocuments.ToCopy(MyDayReplicaDocuments.ToChange(sent)));
    }

    [Fact]
    public void Only_the_preferences_document_is_recognised_as_one()
    {
        Assert.True(MyDayReplicaDocuments.Is(
            MyDayReplicaDocuments.ToChange(new MyDayPreferencesCopyDto(PreferencesJson, Morning))));
        Assert.False(MyDayReplicaDocuments.Is(TaskChanges.Change("Plan the day", Morning)));
    }
}
