using Backlog.Desktop.UI.Knowledge;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the knowledge stores and the chapter writer do when the folder they were
/// handed came from a repository branch.
/// <para>
/// The panels hide the controls, and these are the backstops behind them. A
/// write that reached a branch snapshot would succeed, look like it worked, and
/// be gone at the next fetch — silently discarding somebody's work is the one
/// outcome worth throwing over, so it is asserted rather than assumed.
/// </para>
/// </summary>
public sealed class KnowledgeReadOnlyBranchTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // --- The stores refuse ----------------------------------------------------

    [Fact]
    public async Task A_domain_view_read_from_a_branch_is_not_editable()
    {
        var view = await new DomainKnowledgeStore(BranchSource(WithDomain()))
            .LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(view.Error);
        Assert.False(view.CanEdit);
    }

    [Fact]
    public async Task A_domain_view_read_from_a_clone_is_editable()
    {
        var view = await new DomainKnowledgeStore(LocalSource(WithDomain()))
            .LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(view.Error);
        Assert.True(view.CanEdit);
    }

    [Fact]
    public async Task Setting_a_domain_status_on_a_branch_is_refused()
    {
        var store = new DomainKnowledgeStore(BranchSource(WithDomain()));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateStatusAsync("backlog", ".domain/inbox/domain.md", "agreed", TestContext.Current.CancellationToken));

        Assert.Contains("read-only", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("branch snapshot", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clearing_a_domain_status_on_a_branch_is_refused()
    {
        var store = new DomainKnowledgeStore(BranchSource(WithDomain()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ClearStatusAsync("backlog", ".domain/inbox/domain.md", TestContext.Current.CancellationToken));
    }

    /// <summary>The refusal must come before the file is touched, not after.</summary>
    [Fact]
    public async Task A_refused_status_change_leaves_the_file_alone()
    {
        var root = WithDomain();
        var chapter = Path.Combine(root, ".domain", "inbox", "domain.md");
        var before = File.ReadAllText(chapter);

        var store = new DomainKnowledgeStore(BranchSource(root));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateStatusAsync("backlog", ".domain/inbox/domain.md", "agreed", TestContext.Current.CancellationToken));

        Assert.Equal(before, File.ReadAllText(chapter));
    }

    // --- The chapter resolver and writer --------------------------------------

    /// <summary>A chapter under a branch snapshot still resolves — the reader
    /// wants to read it — and is marked unwritable rather than hidden. Resolving
    /// to null would take the content away along with the edit.</summary>
    [Fact]
    public void A_chapter_under_a_branch_resolves_but_is_not_editable()
    {
        var root = WithDomain();
        var location = BranchSource(root).Resolve(".domain", "backlog");

        var chapter = KnowledgeChapterResolver.TryResolve("domain", location, ".domain/inbox/domain.md");

        Assert.NotNull(chapter);
        Assert.False(chapter.CanEdit);
        Assert.True(File.Exists(chapter.FullPath));
    }

    [Fact]
    public void A_chapter_under_a_clone_resolves_editable()
    {
        var root = WithDomain();
        var location = LocalSource(root).Resolve(".domain", "backlog");

        var chapter = KnowledgeChapterResolver.TryResolve("domain", location, ".domain/inbox/domain.md");

        Assert.NotNull(chapter);
        Assert.True(chapter.CanEdit);
    }

    /// <summary>A ref is a plain record anybody can construct, so the writer
    /// checks again — the same last-place reasoning it already applies to
    /// containment.</summary>
    [Fact]
    public async Task The_writer_refuses_a_chapter_marked_read_only()
    {
        var root = WithDomain();
        var chapter = new KnowledgeChapterRef(
            "domain",
            Path.Combine(root, ".domain"),
            "inbox/domain.md",
            CanEdit: false);

        var before = File.ReadAllText(chapter.FullPath);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KnowledgeChapterWriter().WriteAsync(chapter, "# Replaced", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("read-only", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(chapter.FullPath));
    }

    [Fact]
    public async Task The_writer_still_writes_an_editable_chapter()
    {
        var root = WithDomain();
        var chapter = new KnowledgeChapterRef("domain", Path.Combine(root, ".domain"), "inbox/domain.md");

        await new KnowledgeChapterWriter().WriteAsync(chapter, "# Replaced\n", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("# Replaced", File.ReadAllText(chapter.FullPath), StringComparison.Ordinal);
    }

    // --- Instructions, the second gate ----------------------------------------

    [Fact]
    public void Instructions_read_from_a_branch_are_found_but_not_editable()
    {
        var root = WithDomain();
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# Project instructions");

        var repository = Repository(root, branch: true);
        var view = Assert.Single(new InstructionSourceDiscovery().Discover([repository], BranchSource(root)));

        Assert.False(view.CanEdit);
        Assert.NotEmpty(view.Documents);
    }

    [Fact]
    public void Instructions_read_from_a_clone_stay_editable()
    {
        var root = WithDomain();
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# Project instructions");

        var repository = Repository(root, branch: false);
        var view = Assert.Single(new InstructionSourceDiscovery().Discover([repository], LocalSource(root)));

        Assert.True(view.CanEdit);
        Assert.NotEmpty(view.Documents);
    }

    // --- Fixtures -------------------------------------------------------------

    private static GitHubRepositoryRef Repository(string root, bool branch) =>
        new("backlog", "JSdotNet", "Backlog")
        {
            CloneDirectory = branch ? null : root,
            UseLocalKnowledgeFolder = !branch
        };

    /// <summary>A source whose repository has no clone, so every area resolves to
    /// the snapshot at <paramref name="root"/>.</summary>
    private IKnowledgeFolderSource BranchSource(string root) => new StubFolderSource(root, KnowledgeSourceKind.Branch);

    private IKnowledgeFolderSource LocalSource(string root) => new StubFolderSource(root, KnowledgeSourceKind.LocalFolder);

    private string WithDomain()
    {
        var root = TempDir();
        var domain = Path.Combine(root, ".domain");
        var inbox = Path.Combine(domain, "inbox");
        Directory.CreateDirectory(inbox);

        File.WriteAllText(Path.Combine(domain, "context-map.md"), "# Context Map: Test\n");
        File.WriteAllText(Path.Combine(inbox, "domain.md"), "# Inbox\n\n```meta\nstatus: draft\n```\n");

        return root;
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "knowledge-read-only-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Resolves every area against one root and stamps the source on it.
    /// <para>
    /// Deliberately not the real <c>KnowledgeFolderSource</c> over a real
    /// settings pair: what these tests are about is what a store does with a
    /// location it was handed, and building the settings that produce one would
    /// put the resolution under test in every assertion about the writer.
    /// </para>
    /// </summary>
    private sealed class StubFolderSource(string root, KnowledgeSourceKind source) : IKnowledgeFolderSource
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void NotifyContentChanged()
        {
        }

        public string StorageDirectory => root;

        public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) => KnowledgeFolderSetting.Defaults();

        public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var folder = KnowledgeFolderSetting.Defaults()
                .First(setting => string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));

            var full = Path.Combine(root, folder.EffectivePath);

            return Directory.Exists(full)
                ? new KnowledgeFolderLocation(key, true, null, "JSdotNet/Backlog", folder, full, root, "JSdotNet/Backlog", "backlog", source)
                : KnowledgeFolderLocation.Unavailable(key, $"No {key}.", source: source);
        }
    }
}
