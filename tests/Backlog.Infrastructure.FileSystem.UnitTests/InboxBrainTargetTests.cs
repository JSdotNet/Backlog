using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;

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

    /// <summary>Triage asks before anybody presses To Brain, and is told the same
    /// words the route would refuse in; once a folder is there, nothing.</summary>
    [Fact]
    public void The_target_says_up_front_why_it_cannot_take_a_note()
    {
        var brain = new BrainFolderSource(() => _setting);
        var target = new InboxBrainTarget(new BrainNoteWriter(brain), brain);

        Assert.Equal(BrainFolderSource.NoBrainFolder, target.UnavailableReason);

        _setting = Brain;
        Assert.Contains("was not found", target.UnavailableReason, StringComparison.Ordinal);

        Directory.CreateDirectory(Brain);
        Assert.Null(target.UnavailableReason);
    }

    /// <summary>Queue health's count (<c>.devbook/domain/inbox/features.md#queue-health-bar</c>):
    /// every note in the inbox area a route lands in, its sub-folders included and its
    /// dot-folders and other files left out.</summary>
    [Fact]
    public async Task The_notes_in_the_inbox_area_are_counted()
    {
        var brain = new BrainFolderSource(() => _setting);
        var target = new InboxBrainTarget(new BrainNoteWriter(brain), brain, brain);
        Directory.CreateDirectory(Path.Combine(Brain, "_inbox", "later"));
        Directory.CreateDirectory(Path.Combine(Brain, "_inbox", ".obsidian"));
        Directory.CreateDirectory(Path.Combine(Brain, "projects"));
        await File.WriteAllTextAsync(Path.Combine(Brain, "_inbox", "one.md"), "# One", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Brain, "_inbox", "later", "two.md"), "# Two", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Brain, "_inbox", ".obsidian", "hidden.md"), "# Hidden", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Brain, "_inbox", "photo.png"), "", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Brain, "projects", "filed.md"), "# Filed", Cancellation);
        _setting = Brain;

        var unfiled = await target.UnfiledNotesAsync(Cancellation);

        Assert.Equal(new InboxBrainUnfiledDto("_inbox", 2), unfiled);
    }

    /// <summary>A Brain whose inbox area has no folder yet has nothing to file.</summary>
    [Fact]
    public async Task An_inbox_area_with_no_folder_yet_holds_no_note()
    {
        var brain = new BrainFolderSource(() => _setting);
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        var unfiled = await new InboxBrainTarget(new BrainNoteWriter(brain), brain, brain).UnfiledNotesAsync(Cancellation);

        Assert.Equal(new InboxBrainUnfiledDto("_inbox", 0), unfiled);
    }

    /// <summary>No Brain set, a folder that is missing, or no folder source: nothing,
    /// so the Inbox shows nothing for the Brain.</summary>
    [Fact]
    public async Task Without_a_brain_there_is_no_count()
    {
        var brain = new BrainFolderSource(() => _setting);
        var target = new InboxBrainTarget(new BrainNoteWriter(brain), brain, brain);

        Assert.Null(await target.UnfiledNotesAsync(Cancellation));

        _setting = Brain;
        Assert.Null(await target.UnfiledNotesAsync(Cancellation));

        Directory.CreateDirectory(Brain);
        Assert.Null(await new InboxBrainTarget(new BrainNoteWriter(brain), brain).UnfiledNotesAsync(Cancellation));
        Assert.Null(await ((IInboxBrainTarget)new NoBrainTarget()).UnfiledNotesAsync(Cancellation));
    }

    /// <summary>A host composed without a Brain says so up front too.</summary>
    [Fact]
    public void A_host_without_a_brain_says_there_is_none_up_front() =>
        Assert.Equal(InboxErrors.NoBrainHere, new NoBrainTarget().UnavailableReason);

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
