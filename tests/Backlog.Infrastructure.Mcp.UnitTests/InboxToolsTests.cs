using System.Reflection;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>list_inbox_items</c>, <c>read_inbox_item</c>, <c>archive_inbox_item</c> and
/// <c>capture_to_inbox</c>: the filters, the shape of each answer, that an archive is the
/// module's own and never a delete, and that a capture is Add by hand's own.
/// The same tools over the real module and SQLite are in
/// <c>Backlog.Desktop.UI.UnitTests/InboxMcpToolsOverSqliteTests</c>.
/// </summary>
public class InboxToolsTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unprocessed_is_the_default_and_newest_comes_first()
    {
        var older = Item("older", Day);
        var newer = Item("newer", Day.AddHours(1));
        var archived = Item("archived", Day.AddHours(2)) with { Status = InboxStatus.Archived };

        var answer = await Tools(older, newer, archived).ListAsync(cancellationToken: Cancel);

        Assert.Equal(["newer", "older"], answer.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task Status_all_channel_kind_and_the_window_filter()
    {
        var items = new[]
        {
            Item("in", Day, channel: "mobile", kind: "note"),
            Item("web", Day, channel: "web", kind: "note"),
            Item("youtube", Day, channel: "mobile", kind: "youtube"),
            Item("late", Day.AddDays(2), channel: "mobile", kind: "note"),
            Item("done", Day, channel: "mobile", kind: "note") with { Status = InboxStatus.Archived },
        };

        var answer = await Tools(items).ListAsync(
            status: "all", channel: "MOBILE", kind: "note",
            from: new DateOnly(2026, 10, 27), to: new DateOnly(2026, 10, 28), cancellationToken: Cancel);

        Assert.Equal(["done", "in"], answer.Items.Select(item => item.Title).Order());
    }

    [Fact]
    public async Task A_listed_item_carries_its_body_length_a_flat_preview_and_its_attachment_count()
    {
        var body = "line one\n\nline two " + new string('x', 300);
        var answer = await Tools(Item("long", Day, body: body)).ListAsync(cancellationToken: Cancel);

        var listed = Assert.Single(answer.Items);
        Assert.Equal(body.Length, listed.BodyLength);
        Assert.StartsWith("line one line two x", listed.Preview, StringComparison.Ordinal);
        Assert.Equal(InboxTools.PreviewLength + 1, listed.Preview.Length);
        Assert.Equal(0, listed.AttachmentCount);
    }

    [Theory]
    [InlineData("processed")]
    [InlineData("1")]
    public async Task An_unknown_status_is_refused(string status)
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().ListAsync(status: status, cancellationToken: Cancel));

        Assert.Contains("inbox.unknown_status", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reversed_window_is_refused()
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().ListAsync(
            from: new DateOnly(2026, 10, 28), to: new DateOnly(2026, 10, 27), cancellationToken: Cancel));

        Assert.Contains("inbox.window_reversed", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_answers_the_body_tags_and_each_attachments_file()
    {
        var item = Item("slides", Day, body: "# Talk") with
        {
            SourceUrl = "https://example.test/talk",
            Tags = [new InboxTagDto("techorama", false)],
            Attachments = [new InboxAttachmentDto(Guid.NewGuid(), "slide.png", "image/png", 42, true, true, null) { LocalPath = @"C:\inbox\slide.png" }],
        };

        var read = await Tools(item).ReadAsync(item.Id, Cancel);

        Assert.Equal("# Talk", read.BodyMd);
        Assert.Equal("https://example.test/talk", read.SourceUrl);
        Assert.Equal(["techorama"], read.Tags);
        var photo = Assert.Single(read.Attachments);
        Assert.Equal(("slide.png", "image/png", 42L, @"C:\inbox\slide.png"), (photo.Name, photo.ContentType, photo.SizeBytes, photo.LocalPath));
    }

    [Fact]
    public async Task Read_refuses_an_unknown_id()
    {
        var refused = await Assert.ThrowsAsync<McpException>(() => Tools().ReadAsync(Guid.NewGuid(), Cancel));

        Assert.Contains(InboxErrors.ItemNotFound.Code, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archive_goes_through_the_modules_archive_and_never_deletes()
    {
        var item = Item("filed", Day);
        var inbox = FakeInbox.Over(item);

        var answer = await new InboxTools(inbox).ArchiveAsync(item.Id, Cancel);

        Assert.Equal("archived", answer.Status);
        var fake = (FakeInbox)(object)inbox;
        Assert.Equal(["ArchiveAsync"], fake.Writes);
    }

    [Fact]
    public async Task Archive_passes_the_modules_refusal_on()
    {
        var item = Item("done", Day) with { Status = InboxStatus.Archived };

        var refused = await Assert.ThrowsAsync<McpException>(() => Tools(item).ArchiveAsync(item.Id, Cancel));

        Assert.Contains("inbox.item.invalid_transition", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_goes_through_add_by_hand_as_a_manual_capture_and_answers_its_id()
    {
        var inbox = FakeInbox.Over();

        var answer = await new InboxTools(inbox).CaptureAsync(
            "Call Anna about the invoice", "Before Friday.", "https://example.test/invoice/42", Cancel);

        var fake = (FakeInbox)(object)inbox;
        var call = Assert.Single(fake.Captures);
        Assert.Equal(("Call Anna about the invoice", "Before Friday.", "manual", "https://example.test/invoice/42"), call);
        Assert.Equal(["CaptureAsync"], fake.Writes);

        var captured = Assert.Single(fake.Items);
        Assert.Equal(captured.Id, answer.Id);
        Assert.Equal("unprocessed", answer.Status);
        Assert.Equal("manual", answer.Channel);
        Assert.Equal("Call Anna about the invoice", answer.Title);
    }

    [Fact]
    public async Task Capture_with_a_title_alone_sends_no_notes_and_no_link()
    {
        var inbox = FakeInbox.Over();

        await new InboxTools(inbox).CaptureAsync("Book the dentist", cancellationToken: Cancel);

        var call = Assert.Single(((FakeInbox)(object)inbox).Captures);
        Assert.Equal(("Book the dentist", (string?)null, "manual", (string?)null), call);
    }

    [Fact]
    public async Task Capture_passes_the_modules_refusal_on_and_keeps_nothing()
    {
        var inbox = FakeInbox.Over();

        var refused = await Assert.ThrowsAsync<McpException>(() => new InboxTools(inbox).CaptureAsync("   ", cancellationToken: Cancel));

        Assert.Contains(InboxErrors.ItemNeedsTitle.Code, refused.Message, StringComparison.Ordinal);
        Assert.Empty(((FakeInbox)(object)inbox).Items);
    }

    /// <summary>The description is what an assistant chooses by: an action to clarify
    /// later comes here, a note already judged knowledge goes to <c>capture_note</c>.</summary>
    [Fact]
    public void Capture_is_described_against_capture_note()
    {
        var description = typeof(InboxTools).GetMethod(nameof(InboxTools.CaptureAsync))!
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

        Assert.Contains("clarif", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capture_note", description, StringComparison.Ordinal);
    }

    private static InboxTools Tools(params InboxItemDto[] items) => new(FakeInbox.Over(items));

    private static InboxItemDto Item(string title, DateTimeOffset at, string channel = "mobile", string kind = "note", string body = "text") =>
        new(Guid.NewGuid(), title, body, null, at, at, InboxStatus.Unprocessed, ContentKind.Note, kind, channel, null, [], [], null, null, null);

    /// <summary>
    /// The two members the tools call, answered over a list; every other member of the
    /// port throws, so a tool that reached for one — a delete above all — fails its test.
    /// </summary>
    internal class FakeInbox : DispatchProxy
    {
        private List<InboxItemDto> _items = [];

        public List<string> Writes { get; } = [];

        public List<(string Title, string? Notes, string Channel, string? SourceUrl)> Captures { get; } = [];

        public IReadOnlyList<InboxItemDto> Items => _items;

        public static IInboxItems Over(params InboxItemDto[] items)
        {
            var proxy = Create<IInboxItems, FakeInbox>();
            ((FakeInbox)(object)proxy)._items = [.. items];
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IInboxItems.GetSnapshotAsync):
                    return Task.FromResult(new InboxSnapshotDto([.. _items], [], []));

                case nameof(IInboxItems.ArchiveAsync) when args is [Guid id, ..]:
                    Writes.Add(targetMethod.Name);
                    var index = _items.FindIndex(item => item.Id == id);
                    if (index < 0) return Task.FromResult(Result.Failure(InboxErrors.ItemNotFound));
                    if (_items[index].Status == InboxStatus.Archived)
                    {
                        return Task.FromResult(Result.Failure(InboxErrors.InvalidTransition("Archived cannot become archived.")));
                    }

                    _items[index] = _items[index] with { Status = InboxStatus.Archived };
                    return Task.FromResult(Result.Success());

                case nameof(IInboxItems.CaptureAsync) when args is [string title, var notes, string channel, var source, ..]:
                    Writes.Add(targetMethod.Name);
                    Captures.Add((title, (string?)notes, channel, (string?)source));
                    if (string.IsNullOrWhiteSpace(title)) return Task.FromResult(Result.Failure<InboxItemDto>(InboxErrors.ItemNeedsTitle));

                    var made = Item(title.Trim(), DateTimeOffset.UtcNow, channel: channel, body: (string?)notes ?? string.Empty)
                        with { SourceUrl = (string?)source };
                    _items.Add(made);
                    return Task.FromResult(Result.Success(made));

                default:
                    throw new InvalidOperationException($"The Inbox tools should not call {targetMethod?.Name}.");
            }
        }
    }
}
