using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// How the two single settings documents are written on the task feed — the iteration
/// settings (local ADR 0036, §1) and the My Day preferences (local ADR 0032, §1): one
/// constant id each, their kind token, the file's JSON verbatim as the content, no My Day
/// date of its own, and never a tombstone. The id, token and title are persisted on every
/// replica, so they are spelled out here rather than read back from the code.
/// </summary>
public sealed class SettingsReplicaDocumentsTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    /// <summary>One settings document's wire half, as each document class spells it.</summary>
    public sealed record Document(
        Guid Id,
        string Type,
        string Title,
        string Json,
        Func<string, DateTimeOffset, TaskChange> ToChange,
        Func<TaskChange, (string Content, DateTimeOffset UpdatedAt)> ToCopy,
        Func<TaskChange, bool> Is);

    private static readonly Dictionary<string, Document> Documents = new()
    {
        ["iteration-settings"] = new(
            Guid.Parse("7c3e1f5a-9d2b-4a6e-b8f0-3e5d7a9c1b36"),
            "iteration-settings",
            "Iteration settings",
            """{ "updatedAt": "2026-10-20T08:00:00Z", "length": "two-weeks", "twoWeekStart": "2026-10-19" }""",
            (json, at) => IterationReplicaDocuments.ToChange(new IterationSettingsCopyDto(json, at)),
            change => (IterationReplicaDocuments.ToCopy(change).Content, IterationReplicaDocuments.ToCopy(change).UpdatedAt),
            IterationReplicaDocuments.Is),
        ["my-day-preferences"] = new(
            Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01"),
            "my-day-preferences",
            "My Day preferences",
            """{ "updatedAt": "2026-10-15T08:00:00Z", "clearsOvernight": false }""",
            (json, at) => MyDayReplicaDocuments.ToChange(new MyDayPreferencesCopyDto(json, at)),
            change => (MyDayReplicaDocuments.ToCopy(change).Content, MyDayReplicaDocuments.ToCopy(change).UpdatedAt),
            MyDayReplicaDocuments.Is),
    };

    public static TheoryData<string> Kinds => [.. Documents.Keys];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_settings_travel_as_one_task_shaped_document(string kind)
    {
        var document = Documents[kind];

        var change = document.ToChange(document.Json, Morning);

        Assert.Equal(document.Id, change.Id);
        Assert.Equal(Morning, change.UpdatedAt);
        Assert.Null(change.DeletedAt);
        Assert.Equal(document.Type, change.Task.Type);
        Assert.Equal(document.Title, change.Task.Title);
        Assert.Equal(document.Json, change.Task.ContentMd);
        Assert.Equal("draft", change.Task.Status);
        Assert.Equal("medium", change.Task.Priority);
        Assert.Null(change.Task.InMyDayOn);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_pulled_document_is_the_copy_it_was_sent_as(string kind)
    {
        var document = Documents[kind];

        Assert.Equal((document.Json, Morning), document.ToCopy(document.ToChange(document.Json, Morning)));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Only_its_own_document_is_recognised_as_one(string kind)
    {
        var document = Documents[kind];

        Assert.True(document.Is(document.ToChange(document.Json, Morning)));
        Assert.False(document.Is(TaskChanges.Change("Plan the iteration", Morning)));

        foreach (var other in Documents.Where(pair => pair.Key != kind).Select(pair => pair.Value))
        {
            Assert.False(document.Is(other.ToChange("{}", Morning)));
        }
    }
}
