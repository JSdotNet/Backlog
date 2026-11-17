using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// A task's Devbook references that name a note in the owner's Brain,
/// <c>#brain:&lt;path&gt;[#slug]</c> (local ADR 0053 §2 and §3): answered from this
/// device's Brain, marked as Brain notes, and never looked up in a repository — the
/// repository resolver only ever sees them come back unverified. With no Brain they
/// read unverified, never as an error.
/// </summary>
[Collection(CorpusRegistryCollection.Name)]
public sealed class BrainDevbookReferenceResolverTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-task-refs", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    public BrainDevbookReferenceResolverTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n");
        Write("projects/sync.md", "# Sync rework\n\nThe plan.\n\n## Later\n\nAfterwards.\n");
    }

    [Fact]
    public async Task The_owner_resolves_a_brain_note_and_its_chapters_from_their_own_brain()
    {
        Build();
        var resolver = new BrainDevbookReferenceResolver(new RepositorySide(), () => Brain, DatabaseFor);

        var answers = await resolver.ResolveAsync(
            "JSdotNet/Backlog",
            ["#brain:projects/sync.md", "#brain:projects/sync.md#later", "#brain:projects/gone.md", "#brain:projects/sync.md#nowhere", ".devbook/domain/tasks/domain.md"],
            TestContext.Current.CancellationToken);

        Assert.Collection(
            answers,
            note =>
            {
                Assert.True(note.IsBrainNote);
                Assert.Equal(DevbookReferenceState.Page, note.State);
                Assert.Equal("projects/sync.md", note.Path);
                Assert.Null(note.Anchor);
                Assert.Equal("Sync rework", note.Title);
                Assert.Null(note.Folder);
            },
            chapter =>
            {
                Assert.True(chapter.IsBrainNote);
                Assert.Equal(DevbookReferenceState.Chapter, chapter.State);
                Assert.Equal("later", chapter.Anchor);
                Assert.Equal("Later", chapter.Title);
            },
            missing =>
            {
                Assert.True(missing.IsBrainNote);
                Assert.Equal(DevbookReferenceState.UnknownPage, missing.State);
                Assert.True(missing.IsBroken);
            },
            heading =>
            {
                Assert.True(heading.IsBrainNote);
                Assert.Equal(DevbookReferenceState.UnknownHeading, heading.State);
            },
            chapterOfRepository =>
            {
                Assert.False(chapterOfRepository.IsBrainNote);
                Assert.Equal(DevbookReferenceState.Page, chapterOfRepository.State);
            });
    }

    [Fact]
    public async Task With_no_brain_on_this_device_a_brain_note_reads_unverified_and_nothing_fails()
    {
        var resolver = new BrainDevbookReferenceResolver(new RepositorySide(), brain: null);

        var answer = Assert.Single(await resolver.ResolveAsync(
            "JSdotNet/Backlog",
            ["#brain:projects/sync.md"],
            TestContext.Current.CancellationToken));

        Assert.True(answer.IsBrainNote);
        Assert.Equal(DevbookReferenceState.Unverified, answer.State);
        Assert.False(answer.IsBroken);
        Assert.Equal("projects/sync.md", answer.Path);
    }

    private string? DatabaseFor(string root) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root);

    private void Build() =>
        Assert.True(DevbookDatabaseBuilder.Build(Brain, DatabaseFor(Brain)!, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The repository side as the real resolver answers a Brain address —
    /// unverified, marked as a Brain note — and a found page for anything else.</summary>
    private sealed class RepositorySide : IDevbookReferenceResolver
    {
        public Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
            string? repositoryAlias,
            IReadOnlyList<string> references,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResolvedDevbookReference>>(
            [
                .. references.Select(reference => Backlog.SharedKernel.BrainNoteAddress.Is(reference)
                    ? ResolvedDevbookReference.BrainNote(reference, DevbookReferenceState.Unverified)
                    : new ResolvedDevbookReference(reference, reference, null, DevbookReferenceState.Page, reference, null, "domain"))
            ]);

        public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
            string? repositoryAlias,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DevbookReferenceTarget>>([]);
    }
}
