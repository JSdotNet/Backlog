using Backlog.Infrastructure.Brain;
using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The adapter behind Events' <see cref="IEventTalkNotes"/> (local ADR 0073 §8), over the real
/// Inbox module and its <c>backlog.db</c> and a real Brain folder: a Talk note taken on the phone
/// becomes a note in the Brain's inbox with its words unchanged, and is then archived in the
/// Inbox; with no Brain set it stays where it is.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class EventsTalkNotesOverSqliteTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Photo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly Guid _photoId = Guid.NewGuid();

    private readonly TemporaryDirectory _temp = new("events-talk-notes");
    private readonly ServiceProvider _provider;
    private string? _brainSetting;

    public EventsTalkNotesOverSqliteTests()
    {
        var store = new SqliteInboxRepository(_temp.Path);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Day1.AddDays(5)));
        services.AddSingleton<IInboxItemRepository>(store);
        services.AddSingleton<IInboxOrganizerRepository>(store);
        services.AddSingleton<IInboxAttachmentFiles>(new WorkspaceInboxAttachmentFiles(() => _temp.Combine("_attachments")));
        services.AddSingleton<IInboxAttachmentSource>(new PhotoSource(_photoId));
        services.AddSingleton(Unused<IInboxBacklogTarget>());
        services.AddInboxModule();
        _provider = services.BuildServiceProvider();
    }

    private string Brain => _temp.Combine("Brain");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    [Fact]
    public async Task A_talk_note_is_written_into_the_brains_inbox_with_its_words_unchanged_and_then_archived()
    {
        UseBrain();
        var noteId = await TalkNoteAsync("Keynote: Aspire in production", "Dashboards first.\n\n- traces over logs");
        var scope = _provider.CreateScope();
        var talkNotes = Over(scope);

        var filed = await talkNotes.FileIntoBrainAsync(noteId, Cancellation);

        Assert.NotNull(filed);
        Assert.Equal("Keynote: Aspire in production", filed.Title);
        Assert.StartsWith("_inbox/", filed.Path, StringComparison.Ordinal);
        Assert.Equal(
            BrainNoteText.Compose("Keynote: Aspire in production", "Dashboards first.\n\n- traces over logs", BrainNoteSource.Inbox),
            await File.ReadAllTextAsync(Path.Combine(Brain, filed.Path), Cancellation));
        Assert.Equal(InboxStatus.Unprocessed, (await ItemAsync(scope, noteId)).Status);

        Assert.IsType<Ok>((await talkNotes.ArchiveAsync(noteId, Cancellation)).Value);
        Assert.Equal(InboxStatus.Archived, (await ItemAsync(scope, noteId)).Status);
    }

    [Fact]
    public async Task With_no_brain_set_the_talk_note_stays_in_the_inbox()
    {
        var noteId = await TalkNoteAsync("Keynote: Aspire in production", "Dashboards first.");
        var scope = _provider.CreateScope();

        Assert.Null(await Over(scope).FileIntoBrainAsync(noteId, Cancellation));
        Assert.Equal(InboxStatus.Unprocessed, (await ItemAsync(scope, noteId)).Status);
        Assert.False(Directory.Exists(Brain));
    }

    [Fact]
    public async Task An_archived_talk_note_is_not_written_again()
    {
        UseBrain();
        var noteId = await TalkNoteAsync("Keynote: Aspire in production", "Dashboards first.");
        var scope = _provider.CreateScope();
        Assert.IsType<Ok>((await scope.ServiceProvider.GetRequiredService<IInboxItems>().ArchiveAsync(noteId, Cancellation)).Value);

        Assert.Null(await Over(scope).FileIntoBrainAsync(noteId, Cancellation));
        Assert.Empty(Directory.EnumerateFiles(Brain, "*.md", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_talk_note_with_a_photo_stays_in_the_inbox_with_it()
    {
        UseBrain();
        var noteId = Guid.NewGuid();
        var intake = _provider.CreateScope().ServiceProvider.GetRequiredService<IInboxIntake>();
        await intake.ReceiveAsync(
            new InboxCaptureDto(noteId, "Keynote: Aspire in production", "mobile", Day1, Day1, null, BodyMd: "See the slide.", Kind: "note",
                Attachments: [new InboxCaptureAttachmentDto(_photoId, "slide.png", "image/png", Photo.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Photo)))]),
            Cancellation);
        var scope = _provider.CreateScope();
        Assert.Single((await ItemAsync(scope, noteId)).Attachments);

        Assert.Null(await Over(scope).FileIntoBrainAsync(noteId, Cancellation));
        Assert.Equal(InboxStatus.Unprocessed, (await ItemAsync(scope, noteId)).Status);
        Assert.Empty(Directory.EnumerateFiles(Brain, "*.md", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_note_the_inbox_does_not_hold_is_not_written()
    {
        UseBrain();

        Assert.Null(await Over(_provider.CreateScope()).FileIntoBrainAsync(Guid.NewGuid(), Cancellation));
    }

    private void UseBrain()
    {
        Directory.CreateDirectory(Brain);
        _brainSetting = Brain;
    }

    private EventsTalkNotes Over(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<IInboxItems>(),
        new BrainNoteWriter(new BrainFolderSource(() => _brainSetting)));

    private static async Task<InboxItemDto> ItemAsync(IServiceScope scope, Guid id) =>
        (await scope.ServiceProvider.GetRequiredService<IInboxItems>().GetSnapshotAsync(Cancellation)).Items.Single(item => item.Id == id);

    /// <summary>A note taken on the phone, as the intake receives it.</summary>
    private async Task<Guid> TalkNoteAsync(string title, string body)
    {
        var id = Guid.NewGuid();
        var intake = _provider.CreateScope().ServiceProvider.GetRequiredService<IInboxIntake>();
        await intake.ReceiveAsync(new InboxCaptureDto(id, title, "mobile", Day1, Day1, null, BodyMd: body, Kind: "note"), Cancellation);
        return id;
    }

    private sealed class PhotoSource(Guid id) : IInboxAttachmentSource
    {
        public Task<Result<byte[]>> FetchAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attachmentId == id
                ? Result.Success(Photo)
                : Result.Failure<byte[]>(Error.NotFound("test.attachment", "No such attachment.")));
    }

    private static T Unused<T>() where T : class => System.Reflection.DispatchProxy.Create<T, Throwing>();

    public class Throwing : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Filing a Talk note should not reach {targetMethod?.Name}.");
    }
}
