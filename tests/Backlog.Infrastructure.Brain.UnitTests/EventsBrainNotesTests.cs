using Backlog.Modules.Events.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// Events' read of the Brain (<c>.devbook/domain/events/requirements.md#events-pane</c>, "The note
/// was moved"): a linked path with a note behind it now resolves with the note's title, and one
/// with none — moved, renamed, outside the Brain, or no Brain at all — is left out, so the pane
/// shows it as missing. Never a throw. In the non-parallel collection, because it clears every
/// SQLite pool to delete its scratch database.
/// </summary>
[Collection(CorpusRegistryCollection.Name)]
public sealed class EventsBrainNotesTests : IDisposable
{
    private const string Techorama = "areas/learning/techorama.md";
    private const string Aspire = "areas/learning/aspire.md";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-events-brain-notes", Guid.NewGuid().ToString("N"));

    public EventsBrainNotesTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Areas | areas |\n");
        Write(Techorama, "# Techorama takeaways\n\nWhat I learned.\n");
        Write(Aspire, "# Aspire in production\n\nNotes.\n");
        Write("outside.md", "# Outside\n", root: _scratch);
    }

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Database => DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), Brain)!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_note_at_its_path_resolves_with_its_title()
    {
        Build();

        var found = await Notes().ResolveAsync([Techorama], Token);

        Assert.Equal("Techorama takeaways", found[Techorama].Title);
        Assert.Equal(Techorama, found[Techorama].Path);
    }

    [Fact]
    public async Task A_note_that_moved_is_left_out_though_the_database_still_names_it()
    {
        Build();
        File.Move(Full(Techorama), Full("areas/techorama.md"));

        var found = await Notes().ResolveAsync([Techorama, Aspire], Token);

        Assert.Equal([Aspire], found.Keys);
    }

    [Fact]
    public async Task A_note_written_since_the_last_build_resolves_by_its_file_name()
    {
        Build();
        Write("areas/learning/new-note.md", "# A new note\n");

        var found = await Notes().ResolveAsync(["areas/learning/new-note.md"], Token);

        Assert.Equal("new-note", found["areas/learning/new-note.md"].Title);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("areas/../../outside.md")]
    [InlineData("areas/learning")]
    public async Task A_path_that_leaves_the_brain_or_names_no_file_is_left_out(string path)
    {
        Build();

        var found = await Notes().ResolveAsync([path], Token);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Without_a_brain_every_note_is_left_out_and_the_reason_is_said()
    {
        var none = new EventsBrainNotes(null, () => null, _ => Database);

        Assert.Empty(await none.ResolveAsync([Techorama], Token));
        Assert.Empty(await none.CandidatesAsync(Token));
        Assert.Equal(BrainFolderSource.NoBrainFolder, none.UnavailableReason);
    }

    [Fact]
    public async Task A_brain_folder_that_is_gone_says_so()
    {
        var gone = Path.Combine(_scratch, "Gone");
        var notes = new EventsBrainNotes(null, () => gone, _ => Database);

        Assert.Equal($"The Brain folder was not found at {gone}.", notes.UnavailableReason);
        Assert.Empty(await notes.ResolveAsync([Techorama], Token));
    }

    [Fact]
    public async Task A_brain_without_a_database_still_resolves_its_notes_by_file_name()
    {
        var notes = new EventsBrainNotes(null, () => Brain, _ => Path.Combine(_scratch, "missing", "devbook.db"));

        var found = await notes.ResolveAsync([Techorama], Token);

        Assert.Null(notes.UnavailableReason);
        Assert.Equal("techorama", found[Techorama].Title);
    }

    [Fact]
    public async Task The_picker_offers_the_notes_there_in_title_order_without_the_conventions()
    {
        Build();
        File.Delete(Full(Techorama));

        var candidates = await Notes().CandidatesAsync(Token);

        Assert.Equal([new EventBrainNoteDto(Aspire, "Aspire in production")], candidates);
    }

    [Fact]
    public void A_moved_or_rebuilt_brain_is_heard()
    {
        var source = new ChangingSource();
        var notes = new EventsBrainNotes(source, () => Brain, _ => Database);
        var heard = 0;
        void Listen() => heard++;

        notes.Changed += Listen;
        source.Raise();
        notes.Changed -= Listen;
        source.Raise();

        Assert.Equal(1, heard);
    }

    private EventsBrainNotes Notes() => new(null, () => Brain, _ => Database);

    private void Build()
    {
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Database, BrainCorpusProfile.Instance, Token));
        SqliteConnection.ClearAllPools();
    }

    private string Full(string relativePath, string? root = null) =>
        Path.Combine(root ?? Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relativePath, string text, string? root = null)
    {
        var path = Full(relativePath, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class ChangingSource : IBrainFolderSource
    {
        public string? BrainRoot => null;

        public bool BrainFolderPresent() => false;

        public IReadOnlyList<BrainArea> Areas() => [];

        public IReadOnlyList<BrainArea> HiddenAreas() => [];

        public IReadOnlyList<BrainConflictCopy> ConflictCopies() => [];

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }
}
