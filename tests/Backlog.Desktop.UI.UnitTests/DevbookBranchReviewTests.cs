using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Compare;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Reviewing the branch the Devbook pane reads: the whole branch against the
/// default branch, and each of its commits against the one before, mapped to
/// the compare components' vocabulary — with only the devbook's own chapters
/// compared, and everything GitHub refuses said rather than thrown.
/// </summary>
public sealed class DevbookBranchReviewTests : IDisposable
{
    private const string Alias = "backlog";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private const string IntroBefore = "# Intro\n\nFirst paragraph.\n\n## Scope\n\nOld scope.\n";
    private const string IntroAfter = "# Intro\n\nFirst paragraph.\n\n## Scope\n\nNew scope.\n";
    private const string ReviewAfter = "# Review\n\nA chapter nobody had written.\n";
    private const string OldBefore = "# Old\n\nGone now.\n";
    private const string WasBefore = "# Design\n\nThe same words.\n";
    private const string NewAfter = "# Design\n\nThe same words, and more.\n";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-branch-review-tests-" + Guid.NewGuid().ToString("N"));

    private readonly StubBranchCatalog _catalog = new()
    {
        Heads =
        {
            [string.Empty] = new GitHubBranchHead("main", "mainsha"),
            ["feature"] = new GitHubBranchHead("feature", "headsha")
        }
    };

    private readonly StubBranchComparison _comparison = new();
    private readonly StubTreeClient _trees = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public DevbookBranchReviewTests()
    {
        Directory.CreateDirectory(_root);

        _comparison.Comparison = new GitHubComparison(
            "base0",
            AheadBy: 2,
            BehindBy: 5,
            TotalCommits: 2,
            Commits:
            [
                new GitHubComparedCommit("c1aaaaaaaaaa", "base0", "Add the review chapter", "Job", Now.AddHours(-2)),
                new GitHubComparedCommit("c2bbbbbbbbbb", "c1aaaaaaaaaa", "Edit the intro", "Ada", Now.AddMinutes(-30))
            ],
            Files:
            [
                new GitHubChangedFile(".devbook/arc42/intro.md", null, GitHubFileStatus.Modified, "intro-after"),
                new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "review-after"),
                new GitHubChangedFile(".devbook/arc42/old.md", null, GitHubFileStatus.Removed, null),
                new GitHubChangedFile(".devbook/design/new.md", ".devbook/design/was.md", GitHubFileStatus.Renamed, "new-after"),
                new GitHubChangedFile("src/Program.cs", null, GitHubFileStatus.Modified, "cs"),
                new GitHubChangedFile(".devbook/arc42/diagram.png", null, GitHubFileStatus.Added, "png"),
                new GitHubChangedFile("README.md", null, GitHubFileStatus.Modified, "readme")
            ]);

        _comparison.Commits["c2bbbbbbbbbb"] = new GitHubCommitChange(
            _comparison.Comparison.Commits[1],
            [new GitHubChangedFile(".devbook/arc42/intro.md", null, GitHubFileStatus.Modified, "intro-after")]);

        _trees.Trees["base0"] = new(StringComparer.Ordinal)
        {
            [".devbook/arc42/intro.md"] = "intro-before",
            [".devbook/arc42/old.md"] = "old-before",
            [".devbook/design/was.md"] = "was-before",
            ["src/Program.cs"] = "cs-before"
        };
        _trees.Trees["c1aaaaaaaaaa"] = new(StringComparer.Ordinal)
        {
            [".devbook/arc42/intro.md"] = "intro-before"
        };

        _trees.Blobs["intro-before"] = IntroBefore;
        _trees.Blobs["intro-after"] = IntroAfter;
        _trees.Blobs["review-after"] = ReviewAfter;
        _trees.Blobs["old-before"] = OldBefore;
        _trees.Blobs["was-before"] = WasBefore;
        _trees.Blobs["new-after"] = NewAfter;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private GitHubSettingsStore Settings(string? branch = "feature", bool local = false, List<DevbookFolderSetting>? folders = null)
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => Path.Combine(_root, "workspace"));
        var repository = new GitHubRepositoryRef(Alias, "JSdotNet", "Backlog");
        if (folders is not null) repository = repository with { DevbookFolders = folders };
        Assert.Null(store.SetRepositories([repository]));

        if (local)
        {
            var clone = Path.Combine(_root, "clone");
            Directory.CreateDirectory(clone);
            Assert.Null(store.SetCloneDirectory(Alias, clone));
        }

        Assert.Null(store.SetDevbookSource(Alias, branch, local));
        return store;
    }

    private DevbookBranchReview Review(GitHubSettingsStore? settings = null) =>
        new(settings ?? Settings(), _catalog, _comparison, _trees, _clock);

    // --- When review applies --------------------------------------------------

    [Fact]
    public void A_branch_source_can_be_reviewed_and_the_local_clone_cannot()
    {
        Assert.True(Review().CanReview(Alias));
        Assert.False(Review(Settings(local: true)).CanReview(Alias));
        Assert.False(Review().CanReview(null));
        Assert.False(Review().CanReview("unknown"));
    }

    // --- The outline: the whole branch, then its commits ----------------------

    [Fact]
    public async Task The_branch_is_compared_with_the_default_branch_by_their_heads()
    {
        var outline = await Review().LoadAsync(Alias, Token);

        Assert.Null(outline.Error);
        Assert.NotNull(outline.Review);
        Assert.Equal(("mainsha", "headsha"), Assert.Single(_comparison.Compared));

        var scope = Assert.Single(outline.Scopes);
        Assert.Equal(DevbookBranchReview.BranchScopeId, scope.Id);
        Assert.Equal("All changes vs main", scope.Label);
        Assert.Equal(4, scope.FileCount);
    }

    /// <summary>Newest first, the order a reviewer walks back through; the age
    /// written from the clock, so it never goes stale inside the component.</summary>
    [Fact]
    public async Task Commits_are_listed_newest_first_with_short_sha_subject_and_age()
    {
        var outline = await Review().LoadAsync(Alias, Token);

        Assert.Equal(
            [
                new ChangeCommit("c2bbbbbbbbbb", "c2bbbbb", "Edit the intro", "30m ago"),
                new ChangeCommit("c1aaaaaaaaaa", "c1aaaaa", "Add the review chapter", "2h ago")
            ],
            outline.Commits);
        Assert.Null(outline.Notice);
    }

    [Fact]
    public async Task A_truncated_commit_list_says_how_many_are_shown()
    {
        _comparison.Comparison = _comparison.Comparison! with { TotalCommits = 300 };

        var outline = await Review().LoadAsync(Alias, Token);

        Assert.Equal("Showing 2 of 300 commits.", outline.Notice);
    }

    [Fact]
    public async Task A_branch_that_is_gone_is_said_rather_than_thrown()
    {
        var outline = await Review(Settings(branch: "deleted")).LoadAsync(Alias, Token);

        Assert.Equal("The branch deleted is not on GitHub any more.", outline.Error);
        Assert.Empty(outline.Scopes);
        Assert.Null(outline.Review);
        Assert.Empty(_comparison.Compared);
    }

    [Fact]
    public async Task A_refused_comparison_is_said_rather_than_thrown()
    {
        _comparison.Failure = "GitHub refused the comparison.";

        var outline = await Review().LoadAsync(Alias, Token);

        Assert.Equal("GitHub refused the comparison.", outline.Error);
        Assert.Empty(outline.Commits);
    }

    /// <summary>A refusal is not kept: the next load asks again.</summary>
    [Fact]
    public async Task A_refused_comparison_is_asked_again_on_the_next_load()
    {
        var review = Review();
        _comparison.Failure = "GitHub refused the comparison.";
        await review.LoadAsync(Alias, Token);

        _comparison.Failure = null;
        var outline = await review.LoadAsync(Alias, Token);

        Assert.Null(outline.Error);
        Assert.Equal(2, _comparison.Compared.Count);
    }

    [Fact]
    public async Task Loading_again_at_the_same_heads_asks_github_for_the_comparison_once()
    {
        var review = Review();

        await review.LoadAsync(Alias, Token);
        await review.LoadAsync(Alias, Token);

        Assert.Single(_comparison.Compared);
    }

    /// <summary>One service serves every window. A window that loads the branch
    /// again — after it moved, say — must not change what another window that
    /// loaded it earlier is reviewing.</summary>
    [Fact]
    public async Task A_second_load_leaves_the_first_windows_review_alone()
    {
        var review = Review();
        var first = await review.LoadAsync(Alias, Token);

        _comparison.Comparison = _comparison.Comparison! with
        {
            Files = [new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "review-after")]
        };
        _catalog.Heads["feature"] = new GitHubBranchHead("feature", "movedsha");
        var second = await review.LoadAsync(Alias, Token);

        var firstFiles = await review.FilesAsync(first.Review, DevbookBranchReview.BranchScopeId, Token);
        var secondFiles = await review.FilesAsync(second.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Equal(4, firstFiles.Files.Count);
        Assert.Single(secondFiles.Files);
    }

    // --- The files of a scope -------------------------------------------------

    /// <summary>Only the devbook's own chapters are compared. Code, images and a
    /// root README are counted in one line instead.</summary>
    [Fact]
    public async Task The_whole_branch_lists_its_devbook_chapters_and_counts_the_rest()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Null(files.Error);
        Assert.Null(files.Notice);
        Assert.Equal(
            [".devbook/arc42/intro.md", ".devbook/arc42/old.md", ".devbook/arc42/review.md", ".devbook/design/new.md"],
            files.Files.Select(file => file.Path));
        Assert.Equal(3, files.OtherFiles);
        Assert.Equal("+3 other files not shown", files.OtherFilesNote);
        Assert.Equal("main", files.BeforeLabel);
        Assert.Equal("feature", files.AfterLabel);

        var intro = files.Files[0];
        Assert.Equal("intro.md", intro.Name);
        Assert.Equal(".devbook/arc42", intro.Directory);
        Assert.Equal(ChangeKind.Changed, intro.Kind);
        Assert.Equal(ChangeKind.Removed, files.Files[1].Kind);
        Assert.Equal(ChangeKind.Added, files.Files[2].Kind);
        Assert.Equal(ChangeKind.Changed, files.Files[3].Kind);
    }

    /// <summary>The counts are the comparison's own, read from the merge base and
    /// the branch — a renamed chapter compared against its old path, and its old
    /// path said beside it.</summary>
    [Fact]
    public async Task Each_chapter_counts_what_moved_between_the_merge_base_and_the_branch()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        AssertCounts((0, 0, 1), files.Files.Single(file => file.Name == "intro.md"));
        AssertCounts((2, 0, 0), files.Files.Single(file => file.Name == "review.md"));
        AssertCounts((0, 2, 0), files.Files.Single(file => file.Name == "old.md"));

        var renamed = files.Files.Single(file => file.Name == "new.md");
        AssertCounts((0, 0, 1), renamed);
        Assert.Equal(".devbook/design · renamed from .devbook/design/was.md", renamed.Directory);

        Assert.Equal(["base0"], _trees.TreesListed);
    }

    /// <summary>Renamed to somewhere the devbook does not read, a chapter is gone
    /// as far as the devbook is concerned: removed, under the path it had.</summary>
    [Fact]
    public async Task A_chapter_renamed_out_of_the_devbook_reads_as_removed()
    {
        _comparison.Comparison = _comparison.Comparison! with
        {
            Files = [new GitHubChangedFile("docs/old.md", ".devbook/arc42/old.md", GitHubFileStatus.Renamed, "old-before")]
        };

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        var file = Assert.Single(files.Files);
        Assert.Equal(".devbook/arc42/old.md", file.Path);
        Assert.Equal(ChangeKind.Removed, file.Kind);
        Assert.Equal(".devbook/arc42 · moved out to docs/old.md", file.Directory);
        AssertCounts((0, 2, 0), file);
        Assert.Equal(0, files.OtherFiles);
        Assert.Equal(1, Assert.Single(outline.Scopes).FileCount);
    }

    [Fact]
    public async Task A_commit_is_compared_with_its_parent()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        var files = await review.FilesAsync(outline.Review, "c2bbbbbbbbbb", Token);

        Assert.Null(files.Error);
        var intro = Assert.Single(files.Files);
        AssertCounts((0, 0, 1), intro);
        Assert.Equal(["c2bbbbbbbbbb"], _comparison.CommitsRead);
        Assert.Equal(["c1aaaaaaaaaa"], _trees.TreesListed);
        Assert.Equal("c1aaaaa", files.BeforeLabel);
        Assert.Equal("c2bbbbb", files.AfterLabel);
        Assert.Equal(0, files.OtherFiles);
        Assert.Null(files.OtherFilesNote);
    }

    /// <summary>A root commit has nothing before it: every chapter is new, and
    /// there is no parent tree to list.</summary>
    [Fact]
    public async Task A_root_commit_adds_every_chapter_and_lists_no_parent_tree()
    {
        var root = new GitHubComparedCommit("rootcccccccc", null, "Initial commit", "Job", Now.AddDays(-3));
        _comparison.Comparison = _comparison.Comparison! with { Commits = [root] };
        _comparison.Commits["rootcccccccc"] = new GitHubCommitChange(
            root,
            [new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "review-after")]);

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        Assert.Equal("3d ago", Assert.Single(outline.Commits).Age);

        var files = await review.FilesAsync(outline.Review, "rootcccccccc", Token);

        var file = Assert.Single(files.Files);
        Assert.Equal(ChangeKind.Added, file.Kind);
        AssertCounts((2, 0, 0), file);
        Assert.Empty(_trees.TreesListed);
    }

    [Fact]
    public async Task A_folder_pointed_elsewhere_is_where_its_chapters_are_looked_for()
    {
        var folders = DevbookFolderSetting.Defaults()
            .Select(folder => folder.Key == ".arc42" ? folder with { Path = "docs/architecture" } : folder)
            .ToList();
        _comparison.Comparison = _comparison.Comparison! with
        {
            Files =
            [
                new GitHubChangedFile("docs/architecture/intro.md", null, GitHubFileStatus.Added, "review-after"),
                new GitHubChangedFile(".github/instructions/csharp.instructions.md", null, GitHubFileStatus.Added, "review-after"),
                new GitHubChangedFile("docs/other.md", null, GitHubFileStatus.Added, "review-after")
            ]
        };

        var review = Review(Settings(folders: folders));
        var outline = await review.LoadAsync(Alias, Token);
        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Equal(
            [".github/instructions/csharp.instructions.md", "docs/architecture/intro.md"],
            files.Files.Select(file => file.Path));
        Assert.Equal(1, files.OtherFiles);
    }

    [Fact]
    public async Task Choosing_a_scope_again_reads_no_blob_twice()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);
        var read = _trees.BlobsRead.Count;
        await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);
        await review.FilesAsync(outline.Review, "c2bbbbbbbbbb", Token);

        Assert.Equal(read, _trees.BlobsRead.Count);
    }

    // --- What GitHub left out ------------------------------------------------

    [Fact]
    public async Task A_full_file_list_says_chapters_may_be_missing()
    {
        _comparison.Comparison = _comparison.Comparison! with
        {
            Files = [.. Enumerable.Range(0, GitHubBranchComparison.FileListLimit)
                .Select(i => new GitHubChangedFile($"src/File{i}.cs", null, GitHubFileStatus.Added, "cs"))]
        };

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Equal(
            "GitHub lists at most 300 changed files, so chapters past them are not shown.",
            files.Notice);
    }

    [Fact]
    public async Task A_cut_short_earlier_tree_says_a_chapter_may_read_as_new()
    {
        _trees.TruncatedTrees.Add("base0");

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Equal(
            "GitHub cut the earlier version's file list short, so a chapter it left out reads as wholly new.",
            files.Notice);
    }

    // --- Refusals and cancellations ------------------------------------------

    [Fact]
    public async Task A_blob_github_refuses_is_said_rather_than_thrown()
    {
        _trees.Blobs.Remove("review-after");

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        var files = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        Assert.Equal("No blob review-after.", files.Error);
        Assert.Empty(files.Files);
    }

    /// <summary>
    /// A read two scopes share runs on no one's token. When one scope fails, its
    /// other reads stop being waited for — and the shared read they were waiting
    /// on goes on, so the next scope that needs it is not handed a cancellation.
    /// </summary>
    [Fact]
    public async Task A_failure_in_one_scope_does_not_fail_another_sharing_its_reads()
    {
        _trees.Blobs.Remove("old-before");
        var slow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _trees.Gates["intro-after"] = slow;

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        var branch = await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);
        Assert.Equal("No blob old-before.", branch.Error);

        slow.SetResult();
        var commit = await review.FilesAsync(outline.Review, "c2bbbbbbbbbb", Token);

        Assert.Null(commit.Error);
        AssertCounts((0, 0, 1), Assert.Single(commit.Files));
        Assert.Single(_trees.BlobsRead, blob => blob == "intro-after");
    }

    /// <summary>A reader who moves on cancels their own wait; the answer is a
    /// sentence, never an exception, and the read itself is kept for the next
    /// ask.</summary>
    [Fact]
    public async Task A_cancelled_wait_is_said_and_the_read_goes_on_for_the_next_ask()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _comparison.Gates["c2bbbbbbbbbb"] = gate;

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        using var moved = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = review.FilesAsync(outline.Review, "c2bbbbbbbbbb", moved.Token);
        await moved.CancelAsync();

        Assert.Equal(DevbookBranchReview.CancelledMessage, (await waiting).Error);

        gate.SetResult();
        var files = await review.FilesAsync(outline.Review, "c2bbbbbbbbbb", Token);

        Assert.Null(files.Error);
        Assert.Single(files.Files);
        Assert.Single(_comparison.CommitsRead);
    }

    /// <summary>A read that fails after everyone waiting on it has left is
    /// dropped when it fails: the next ask reads again rather than being handed
    /// the old failure.
    /// <para>
    /// "When it fails" is taken at its word: the ask comes the moment the read
    /// itself has finished failing, with no grace period after it. The test used
    /// to wait a hundred milliseconds there, which stood in for the cache dropping
    /// the failure a thread-pool hop later; under a loaded suite the hop took
    /// longer and the next ask was handed the old failure.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_read_that_fails_with_no_one_waiting_is_not_served_again()
    {
        var gate = new TaskCompletionSource();
        _comparison.Gates["c2bbbbbbbbbb"] = gate;

        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        using var moved = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = review.FilesAsync(outline.Review, "c2bbbbbbbbbb", moved.Token);
        await moved.CancelAsync();
        Assert.Equal(DevbookBranchReview.CancelledMessage, (await waiting).Error);

        _comparison.Failure = "GitHub refused the commit.";
        gate.SetResult();
        await Assert.ThrowsAsync<GitHubException>(() => Assert.Single(_comparison.Reads));

        _comparison.Failure = null;
        _comparison.Gates.Remove("c2bbbbbbbbbb");
        var files = await review.FilesAsync(outline.Review, "c2bbbbbbbbbb", Token);

        Assert.Null(files.Error);
        Assert.Single(files.Files);
        Assert.Equal(2, _comparison.CommitsRead.Count);
    }

    [Fact]
    public async Task Files_with_no_review_loaded_are_an_error_not_an_exception()
    {
        var files = await Review().FilesAsync(null, DevbookBranchReview.BranchScopeId, Token);

        Assert.NotNull(files.Error);
        Assert.Empty(files.Files);
    }

    // --- One chapter's comparison --------------------------------------------

    [Fact]
    public async Task A_chosen_chapter_is_compared_section_by_section()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);
        await review.FilesAsync(outline.Review, DevbookBranchReview.BranchScopeId, Token);

        var comparison = await review.CompareAsync(outline.Review, DevbookBranchReview.BranchScopeId, ".devbook/arc42/intro.md", Token);

        Assert.Null(comparison.Error);
        var section = Assert.IsType<ComparedSection>(comparison.Section);
        Assert.False(section.IsWhollyUnchanged);
        Assert.Equal("Intro", Assert.Single(section.Children).Heading);
    }

    [Fact]
    public async Task A_chapter_the_scope_does_not_hold_is_an_error()
    {
        var review = Review();
        var outline = await review.LoadAsync(Alias, Token);

        var comparison = await review.CompareAsync(outline.Review, "c2bbbbbbbbbb", "src/Program.cs", Token);

        Assert.Null(comparison.Section);
        Assert.NotNull(comparison.Error);
    }

    /// <summary>Added, removed and changed sections and blocks, as the list row
    /// shows them.</summary>
    private static void AssertCounts((int Added, int Removed, int Changed) expected, ChangedFile file) =>
        Assert.Equal(expected, (file.AddedSections, file.RemovedSections, file.ChangedSections));
}
