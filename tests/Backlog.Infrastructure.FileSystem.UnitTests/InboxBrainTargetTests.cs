using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// To Brain's writer: an inbox item routed To Brain becomes a note through the
/// same <see cref="BrainNoteWriter"/> the Devbook pane's Capture uses
/// (<c>.devbook/domain/inbox/requirements.md#route-to-brain</c>), over a real
/// Brain folder.
/// </summary>
[Collection(BrainRootCollection.Name)]
public sealed class InboxBrainTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "inbox-brain-target-tests-" + Guid.NewGuid().ToString("N"));

    private string? _setting;

    private string Brain => Path.Combine(_root, "Brain");

    private InboxBrainTarget Target() => new(new BrainNoteWriter(new BrainFolderSource(() => _setting)));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The requirement's second scenario: an Inbox note.</summary>
    [Fact]
    public async Task An_inbox_note_becomes_a_brain_note_with_its_title_and_body()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        var written = await Target().WriteNoteAsync(new InboxBrainNoteRequestDto(Guid.CreateVersion7(), "Standup", "- shipped sync", null), Cancellation);

        Assert.True(written.IsSuccess);
        Assert.Equal("_inbox/standup.md", written.Value);
        Assert.Equal(
            BrainNoteText.Compose("Standup", "- shipped sync", BrainNoteSource.Inbox),
            await File.ReadAllTextAsync(Path.Combine(Brain, "_inbox", "standup.md"), Cancellation));
    }

    /// <summary>The guard holds on this writer too: the item's text is the
    /// person's, and a fence typed on the phone stays text.</summary>
    [Fact]
    public async Task A_typed_fence_in_an_inbox_note_stays_text()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        var written = await Target().WriteNoteAsync(new InboxBrainNoteRequestDto(Guid.CreateVersion7(), "Odd", "```meta\ntype: x\n```\n# Heading", null), Cancellation);

        var text = await File.ReadAllTextAsync(Path.Combine(Brain, written.Value), Cancellation);
        Assert.Equal(1, text.Split("\n```meta\n").Length - 1);
        Assert.Contains("\n\u200B```meta\n", text, StringComparison.Ordinal);
        Assert.Contains("\n\u200B# Heading\n", text, StringComparison.Ordinal);
    }

    /// <summary>The requirement's first scenario: a capture keeps its body, and
    /// the link it came from.</summary>
    [Fact]
    public async Task A_captured_link_keeps_its_address()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        var written = await Target().WriteNoteAsync(
            new InboxBrainNoteRequestDto(Guid.CreateVersion7(), "Event sourcing in practice", "Why the log is the truth.", "https://example.com/es"),
            Cancellation);

        var text = await File.ReadAllTextAsync(Path.Combine(Brain, written.Value), Cancellation);
        Assert.EndsWith("\nWhy the log is the truth.\n\nSource: <https://example.com/es>\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "https://x.test/a", "Source: <https://x.test/a>")]
    [InlineData("See https://x.test/a", "https://x.test/a", "See https://x.test/a")]
    [InlineData("Body\n", null, "Body")]
    [InlineData("", "https://x.test/a b>c", "Source: <https://x.test/a%20b%3Ec>")]
    public void The_link_is_added_once(string body, string? link, string expected)
    {
        Assert.Equal(expected, InboxBrainTarget.BodyOf(new InboxBrainNoteRequestDto(Guid.Empty, "T", body, link)));
    }

    /// <summary>The requirement's third: no Brain folder, no note, the reason in words.</summary>
    [Fact]
    public async Task No_brain_folder_is_refused_with_the_brain_s_own_words()
    {
        var written = await Target().WriteNoteAsync(new InboxBrainNoteRequestDto(Guid.CreateVersion7(), "Lost", "", null), Cancellation);

        Assert.True(written.IsFailure);
        Assert.Equal(InboxErrors.BrainUnavailableCode, written.Error.Code);
        Assert.Equal(BrainFolderSource.NoBrainFolder, written.Error.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
