using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.Mcp;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox MCP tools over the real Inbox module and its real <c>backlog.db</c>: the
/// conference-notes case they were made for — thirteen notes from the phone over two days,
/// one with a slide photo — listed, read with the photo's file, and archived once filed.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class InboxMcpToolsOverSqliteTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Photo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "inbox-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteInboxRepository _store;
    private readonly ServiceProvider _provider;
    private readonly Guid _photoId = Guid.NewGuid();

    public InboxMcpToolsOverSqliteTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SqliteInboxRepository(_root);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Day1.AddDays(5)));
        services.AddSingleton<IInboxItemRepository>(_store);
        services.AddSingleton<IInboxOrganizerRepository>(_store);
        services.AddSingleton<IInboxAttachmentFiles>(new WorkspaceInboxAttachmentFiles(() => Path.Combine(_root, "_inbox")));
        services.AddSingleton<IInboxAttachmentSource>(new PhotoSource(_photoId));
        services.AddSingleton(Unused<IInboxBacklogTarget>());
        services.AddInboxModule();
        _provider = services.BuildServiceProvider();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Thirteen_conference_notes_are_listed_read_with_their_photo_and_archived()
    {
        var ids = await SeedAsync();
        var tools = Tools();

        var listed = await tools.ListAsync(from: new DateOnly(2026, 10, 27), to: new DateOnly(2026, 10, 28), cancellationToken: Cancellation);

        Assert.Equal(13, listed.Items.Count);
        Assert.All(listed.Items, item => Assert.Equal("mobile", item.Channel));
        Assert.All(listed.Items, item => Assert.Equal("unprocessed", item.Status));
        Assert.Equal(1, listed.Items.Single(item => item.Id == ids[0]).AttachmentCount);

        var read = await tools.ReadAsync(ids[0], Cancellation);
        var photo = Assert.Single(read.Attachments);
        Assert.Equal("slide.png", photo.Name);
        Assert.Equal("image/png", photo.ContentType);
        Assert.Equal(Photo.Length, photo.SizeBytes);
        Assert.NotNull(photo.LocalPath);
        Assert.Equal(Photo, await File.ReadAllBytesAsync(photo.LocalPath, Cancellation));
        Assert.Contains("Session 0", read.BodyMd, StringComparison.Ordinal);

        var archived = await tools.ArchiveAsync(ids[1], Cancellation);
        Assert.Equal("archived", archived.Status);

        var after = await tools.ListAsync(from: new DateOnly(2026, 10, 27), to: new DateOnly(2026, 10, 28), cancellationToken: Cancellation);
        Assert.Equal(12, after.Items.Count);
        Assert.DoesNotContain(after.Items, item => item.Id == ids[1]);

        // Archived, not deleted: still in the store, still listed under its status.
        Assert.NotNull(await _store.GetAsync(ids[1], Cancellation));
        var archivedList = await tools.ListAsync(status: "archived", cancellationToken: Cancellation);
        Assert.Equal(ids[1], Assert.Single(archivedList.Items).Id);

        var again = await Assert.ThrowsAsync<McpException>(() => tools.ArchiveAsync(ids[1], Cancellation));
        Assert.Contains("inbox.item.invalid_transition", again.Message, StringComparison.Ordinal);
    }

    private InboxTools Tools() => new(_provider.CreateScope().ServiceProvider.GetRequiredService<IInboxItems>());

    private async Task<Guid[]> SeedAsync()
    {
        var intake = _provider.CreateScope().ServiceProvider.GetRequiredService<IInboxIntake>();
        var ids = new Guid[13];

        for (var i = 0; i < 13; i++)
        {
            ids[i] = Guid.NewGuid();
            var at = Day1.AddHours(i * 3);
            IReadOnlyList<InboxCaptureAttachmentDto>? attachments = i == 0
                ? [new InboxCaptureAttachmentDto(_photoId, "slide.png", "image/png", Photo.Length, Sha256(Photo))]
                : null;

            await intake.ReceiveAsync(
                new InboxCaptureDto(ids[i], $"Techorama session {i}", "mobile", at, at, null, BodyMd: $"Session {i} notes", Kind: "note", Attachments: attachments),
                Cancellation);
        }

        // Outside the window: the day before, and the day after.
        foreach (var at in new[] { Day1.AddDays(-1), Day1.AddDays(2) })
        {
            await intake.ReceiveAsync(new InboxCaptureDto(Guid.NewGuid(), "Elsewhere", "mobile", at, at, null, BodyMd: "x", Kind: "note"), Cancellation);
        }

        return ids;
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

    /// <summary>A port the Inbox tools never reach — routing to the backlog — that the
    /// module's composition still asks for. It throws if called.</summary>
    private static T Unused<T>() where T : class => System.Reflection.DispatchProxy.Create<T, Throwing>();

    public class Throwing : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"The Inbox tools should not reach {targetMethod?.Name}.");
    }

    private sealed class PhotoSource(Guid id) : IInboxAttachmentSource
    {
        public Task<Result<byte[]>> FetchAsync(Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attachmentId == id
                ? Result.Success(Photo)
                : Result.Failure<byte[]>(Error.NotFound("test.attachment", "No such attachment.")));
    }
}
