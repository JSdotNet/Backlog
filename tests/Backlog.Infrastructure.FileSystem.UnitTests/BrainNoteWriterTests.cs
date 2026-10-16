using System.Text;

using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Writing a new note into the Brain's inbox area — the writer the Devbook pane's
/// Capture, Inbox triage's To Brain and the capture tool share
/// (<c>.devbook/domain/devbook/requirements.md#capture-into-the-brain</c>). The
/// text itself is <c>BrainNoteTextTests</c>'; these are the file: where it lands,
/// that it never overwrites, and what an unusable Brain answers.
/// </summary>
[Collection(BrainRootCollection.Name)]
public sealed class BrainNoteWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "brain-note-writer-tests-" + Guid.NewGuid().ToString("N"));

    private string? _setting;

    private string Brain => Path.Combine(_root, "Brain");

    private (BrainNoteWriter Writer, BrainFolderSource Source) Create()
    {
        var source = new BrainFolderSource(() => _setting);
        return (new BrainNoteWriter(source), source);
    }

    private void UseBrain()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;
    }

    /// <summary>The requirement's scenario: the PARA default.</summary>
    [Fact]
    public async Task A_capture_lands_in_inbox_under_the_para_default()
    {
        UseBrain();
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Ideas for the sync", "Keep the outbox.", BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.True(written.Written);
        Assert.Equal("_inbox/ideas-for-the-sync.md", written.RelativePath);

        var text = await File.ReadAllTextAsync(Path.Combine(Brain, "_inbox", "ideas-for-the-sync.md"), TestContext.Current.CancellationToken);
        Assert.Equal(BrainNoteText.Compose("Ideas for the sync", "Keep the outbox.", BrainNoteSource.Manual), text);
    }

    [Fact]
    public async Task The_file_is_utf8_with_no_byte_order_mark()
    {
        UseBrain();
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Été", "Ça va", BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(Brain, written.RelativePath!), TestContext.Current.CancellationToken);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("Ça va", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capture_lands_in_the_area_the_conventions_name_the_inbox()
    {
        UseBrain();
        File.WriteAllText(Path.Combine(Brain, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Work | work |\n| Inbox | incoming |\n");
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Call back", null, BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.Equal("incoming/call-back.md", written.RelativePath);
        Assert.True(File.Exists(Path.Combine(Brain, "incoming", "call-back.md")));
    }

    [Fact]
    public async Task A_taken_name_gets_the_next_free_one_and_nothing_is_overwritten()
    {
        UseBrain();
        Directory.CreateDirectory(Path.Combine(Brain, "_inbox"));
        File.WriteAllText(Path.Combine(Brain, "_inbox", "standup.md"), "mine");
        File.WriteAllText(Path.Combine(Brain, "_inbox", "standup-2.md"), "mine too");
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Standup", "New", BrainNoteSource.Inbox), TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/standup-3.md", written.RelativePath);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Brain, "_inbox", "standup.md")));
        Assert.Equal("mine too", File.ReadAllText(Path.Combine(Brain, "_inbox", "standup-2.md")));
    }

    /// <summary>
    /// Routing an item To Brain, undoing it and routing it again lands on the note
    /// the first route wrote, while nobody has edited it — no <c>-2</c> copy beside
    /// it (<c>.devbook/domain/inbox/features.md#route-to-brain</c>).
    /// </summary>
    [Fact]
    public async Task Routing_the_same_item_again_reuses_the_untouched_note_it_wrote()
    {
        UseBrain();
        var (writer, _) = Create();
        var draft = new BrainNoteDraft("Standup", "Said in the standup.", BrainNoteSource.Inbox);

        var first = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);
        var second = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/standup.md", first.RelativePath);
        Assert.Equal(first.RelativePath, second.RelativePath);
        Assert.Equal(["standup.md"], Directory.GetFiles(Path.Combine(Brain, "_inbox")).Select(Path.GetFileName));
    }

    /// <summary>A sync client that rewrote the line endings has not edited the note.</summary>
    [Fact]
    public async Task A_note_differing_only_in_line_endings_is_still_reused()
    {
        UseBrain();
        var (writer, _) = Create();
        var draft = new BrainNoteDraft("Standup", "Said in the standup.", BrainNoteSource.Inbox);
        var first = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);
        var path = Path.Combine(Brain, "_inbox", "standup.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));

        var second = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);

        Assert.Equal(first.RelativePath, second.RelativePath);
    }

    /// <summary>Once somebody has edited the note, it is theirs: a second route
    /// writes a new one and leaves the edit alone.</summary>
    [Fact]
    public async Task An_edited_note_is_not_reused_and_is_left_as_it_is()
    {
        UseBrain();
        var (writer, _) = Create();
        var draft = new BrainNoteDraft("Standup", "Said in the standup.", BrainNoteSource.Inbox);
        await writer.WriteAsync(draft, TestContext.Current.CancellationToken);
        var path = Path.Combine(Brain, "_inbox", "standup.md");
        File.AppendAllText(path, "\nMy own line.\n");
        var edited = File.ReadAllText(path);

        var second = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/standup-2.md", second.RelativePath);
        Assert.Equal(edited, File.ReadAllText(path));
    }

    /// <summary>Capture is a new thought each time: two captures with the same
    /// words are two notes, as they always were.</summary>
    [Fact]
    public async Task A_capture_never_reuses_a_note()
    {
        UseBrain();
        var (writer, _) = Create();
        var draft = new BrainNoteDraft("Standup", "Said in the standup.", BrainNoteSource.Manual);

        await writer.WriteAsync(draft, TestContext.Current.CancellationToken);
        var second = await writer.WriteAsync(draft, TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/standup-2.md", second.RelativePath);
    }

    [Fact]
    public async Task A_folder_with_the_note_s_name_is_skipped_like_a_file()
    {
        UseBrain();
        Directory.CreateDirectory(Path.Combine(Brain, "_inbox", "standup.md"));
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Standup", null, BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/standup-2.md", written.RelativePath);
    }

    /// <summary>A write that fails once the file is created takes the file away:
    /// no empty note is left, and no numbered one after it.</summary>
    [Fact]
    public async Task A_cancelled_write_leaves_no_file_behind()
    {
        UseBrain();
        var (writer, _) = Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.WriteAsync(new BrainNoteDraft("Gone", "Body", BrainNoteSource.Manual), cancelled.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Brain, "_inbox")));
    }

    [Fact]
    public async Task A_written_note_tells_the_brain_its_content_changed()
    {
        UseBrain();
        var (writer, source) = Create();
        var changes = 0;
        source.Changed += () => changes++;

        await writer.WriteAsync(new BrainNoteDraft("One", null, BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.Equal(1, changes);
    }

    /// <summary>The route's third requirement, from the writer's side: no Brain
    /// folder, no note, and words that say to set one up.</summary>
    [Fact]
    public async Task No_brain_folder_writes_nothing_and_says_to_set_one_up()
    {
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Lost", null, BrainNoteSource.Inbox), TestContext.Current.CancellationToken);

        Assert.False(written.Written);
        Assert.Null(written.RelativePath);
        Assert.Equal(BrainFolderSource.NoBrainFolder, written.Error);
    }

    [Fact]
    public async Task A_missing_brain_folder_is_not_created_by_a_capture()
    {
        _setting = Brain;
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft("Lost", null, BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.False(written.Written);
        Assert.Contains("not found", written.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Brain));
    }

    [Fact]
    public async Task A_blank_title_writes_nothing()
    {
        UseBrain();
        var (writer, _) = Create();

        var written = await writer.WriteAsync(new BrainNoteDraft(" \n ", "Body only", BrainNoteSource.Manual), TestContext.Current.CancellationToken);

        Assert.False(written.Written);
        Assert.Equal(BrainNoteWriter.NoTitle, written.Error);
        Assert.False(Directory.Exists(Path.Combine(Brain, "_inbox")));
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
