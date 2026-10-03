using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The parse cache the knowledge stores keep between loads, and the property it
/// buys them: a folder loaded twice unchanged is parsed once, a file that changed
/// is the only file parsed again, and a folder announced changed is parsed
/// whole.
///
/// <para>Asserted by identity. The stores hand back records, and a record parsed
/// again is a different instance — which is also what the panels' own
/// reference-identity guards key off, so identity is the property that matters
/// to them rather than a proxy for it.</para>
///
/// <para>The cache itself, <c>DevbookFileCache</c>, moved to
/// <c>Backlog.Infrastructure.Devbook</c> with the index reader that also keeps one
/// (issue #738), and its own tests went with it.</para>
/// </summary>
public sealed class DevbookStoreParseCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-cache-tests", Guid.NewGuid().ToString("n"));

    /// <summary>The store the arc42 panel reads through. It is disposed with its
    /// tab and asks again on the way back, which is the load this exists for.</summary>
    [Fact]
    public async Task The_arc42_store_parses_a_chapter_once_until_its_file_or_its_folder_changes()
    {
        var first = Write(".arc42/01-first.md", "# First\n\nOne.\n");
        Write(".arc42/02-second.md", "# Second\n\nTwo.\n");
        var (store, source) = Arc42Store();

        var initial = await store.LoadAsync("backlog");
        var again = await store.LoadAsync("backlog");

        Assert.Equal(["First", "Second"], initial.Documents.Select(document => document.Title));
        Assert.Equal(initial.Documents, again.Documents, ReferenceEqualityComparer.Instance);

        // One file saved: that chapter is read fresh and its neighbour is not.
        File.WriteAllText(first, "# First, revised\n\nOne, and then some.\n");
        var afterSave = await store.LoadAsync("backlog");

        Assert.Equal("First, revised", afterSave.Documents[0].Title);
        Assert.NotSame(again.Documents[0], afterSave.Documents[0]);
        Assert.Same(again.Documents[1], afterSave.Documents[1]);

        // The folder announced changed — a pull, a move — forgets the lot.
        source.NotifyContentChanged();
        var afterChange = await store.LoadAsync("backlog");

        Assert.NotSame(afterSave.Documents[0], afterChange.Documents[0]);
        Assert.NotSame(afterSave.Documents[1], afterChange.Documents[1]);
    }

    /// <summary>The same contract on the domain store, whose lists are lazy:
    /// the context map is read on load and a context's documents when they are
    /// first asked for, and neither is read twice while its file stands.</summary>
    [Fact]
    public async Task The_domain_store_parses_a_document_once_until_its_file_changes()
    {
        Write(".domain/context-map.md", "# Context Map\n\n> The map.\n");
        var features = Write(".domain/inbox/features.md", "# Inbox\n```meta\nstatus: active\n```\n\n## Capture\n\nText.\n");
        Write(".domain/inbox/domain.md", "# Inbox\n```meta\nstatus: active\n```\n\n> Quick capture.\n");
        var (store, _) = DomainStore();

        var initial = await store.LoadAsync("backlog", TestContext.Current.CancellationToken);
        var initialDocuments = Assert.Single(initial.Contexts).Documents.ToList();
        var again = await store.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Same(initial.ContextMap, again.ContextMap);
        Assert.Equal(initialDocuments, Assert.Single(again.Contexts).Documents, ReferenceEqualityComparer.Instance);

        File.WriteAllText(features, "# Inbox\n```meta\nstatus: active\n```\n\n## Capture\n\nMore text than before.\n");
        var afterSave = await store.LoadAsync("backlog", TestContext.Current.CancellationToken);
        var revised = Assert.Single(afterSave.Contexts).Documents.ToList();

        Assert.Same(initial.ContextMap, afterSave.ContextMap);
        Assert.Equal(initialDocuments.Count, revised.Count);
        Assert.Single(revised, document => !initialDocuments.Contains(document, ReferenceEqualityComparer.Instance));
    }

    private (Arc42DevbookStore Store, DevbookFolderSource Source) Arc42Store()
    {
        var source = Source();
        return (new Arc42DevbookStore(source), source);
    }

    private (DomainDevbookStore Store, DevbookFolderSource Source) DomainStore()
    {
        var source = Source();
        return (new DomainDevbookStore(source), source);
    }

    /// <summary>A folder source over one configured repository whose clone is the
    /// test root — the composition the technology panel tests already use.</summary>
    private DevbookFolderSource Source()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(settings.SetRepositories(repositories));
        settings.SetCloneDirectory("backlog", _root);

        return new DevbookFolderSource(settings);
    }

    private string Write(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
