using Backlog.Infrastructure.GitHub;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one-time devbook import as it runs: once per registered repository, from
/// the default branch's snapshot, into the subject and remark stores through their
/// import write alone, marked only when it ran to the end.
/// <para>
/// The snapshot cache, GitHub and the ledger are faked at the boundary; the stores
/// are the session stores the panels fall back on, so what is asserted is what a
/// store holds rather than which calls were made. The snapshot is the checked-in
/// fixture <c>tests/Fixtures/devbook-import/</c>.
/// </para>
/// </summary>
public sealed class DevbookImportWorkerTests : IDisposable
{
    private const string Alias = "shop";

    private static readonly string FixtureRoot = RepositoryRoot.Directory("tests", "Fixtures", "devbook-import");

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeSnapshots _snapshots = new();
    private readonly FakeGitHub _github = new();
    private readonly MemoryLedger _ledger = new();
    private readonly SessionDevbookSubjectStateStore _subjects;
    private readonly SessionDevbookAnnotationStore _remarks;
    private readonly DevbookFlag _features = new(on: true);
    private readonly ChangingFolders _folders = new();
    private readonly List<GitHubRepositoryRef> _repositories = [new(Alias, "JSdotNet", "Shop")];
    private readonly List<DevbookImportWorker> _workers = [];
    private readonly List<string> _temporary = [];

    public DevbookImportWorkerTests()
    {
        _subjects = new SessionDevbookSubjectStateStore(_time);
        _remarks = new SessionDevbookAnnotationStore(_time);

        _github.Answer(".devbook/arc42/adr/0001-pay-by-card.md", new GitHubPathAddition("c1", new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), 1611));
        _github.Answer(".devbook/arc42/adr/0005-trace-every-order.md", new GitHubPathAddition("c5", new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), null));
        _github.Answer(".devbook/arc42/tdr/0001-paging.md", new GitHubPathAddition("c7", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), 1500));
        _github.Answer("openspec/changes/split-payments/proposal.md", new GitHubPathAddition("c9", new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero), 1502));
    }

    private DevbookImportWorker Worker(IDevbookImportLedger? ledger = null)
    {
        var worker = new DevbookImportWorker(
            () => _repositories,
            _folders,
            _snapshots,
            _github,
            ledger ?? _ledger,
            _subjects,
            _remarks,
            _features,
            _time);

        _workers.Add(worker);
        return worker;
    }

    [Fact]
    public async Task A_repository_is_imported_from_its_default_branch_and_marked()
    {
        var reports = await Worker().RunOnceAsync(TestContext.Current.CancellationToken);

        var report = Assert.Single(reports);
        Assert.True(report.Completed);
        Assert.True(_ledger.HasImported(Alias));
        Assert.All(_snapshots.Asked, asked => Assert.Null(asked.Branch));
        Assert.Contains("openspec/changes/", _snapshots.Asked[0].Selection);

        Assert.Equal(
            [
                ("debt", ".devbook/arc42/tdr/0001-paging.md", (int?)null, "planned"),
                ("decision", ".devbook/arc42/adr/0001-pay-by-card.md", 1611, "proposed"),
                ("proposal", "pay-by-card", null, "accepted"),
                ("proposal", "split-payments", null, "draft"),
            ],
            _subjects.List(Alias)
                .Select(state => (DevbookSubjects.KindName(state.Subject.Kind), state.Subject.Key, state.Subject.PullRequest, state.State))
                .OrderBy(row => row.Item1, StringComparer.Ordinal)
                .ThenBy(row => row.Key, StringComparer.Ordinal));

        var debt = _subjects.Get(Alias, DevbookSubject.Debt(".devbook/arc42/tdr/0001-paging.md"))!;
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), debt.UpdatedAt);
        Assert.Equal(DevbookMarkdownImport.ImportAuthor, debt.By);

        Assert.Equal(3, _remarks.List(Alias, ".arc42/01-introduction.md").Count);
        Assert.Equal(2, _remarks.List(Alias, ".domain/shop/domain.md").Count);
        Assert.Single(_remarks.List(Alias, "openspec/changes/split-payments/proposal.md"));
    }

    /// <summary>A decision record keyed by no pull request cannot be stored: it is
    /// passed over and named in the report, and does not hold the mark back — the
    /// answer will be the same next time.</summary>
    [Fact]
    public async Task A_decision_with_no_pull_request_is_skipped_and_reported()
    {
        var report = Assert.Single(await Worker().RunOnceAsync(TestContext.Current.CancellationToken));

        var skip = Assert.Single(report.Skipped, skip => skip.Path == ".devbook/arc42/adr/0005-trace-every-order.md");
        Assert.Contains("pull request", skip.Reason);
        Assert.Contains(report.Skipped, skip => skip.Path == ".devbook/arc42/tdr/0002-flaky-tests.md");
        Assert.True(report.Completed);
    }

    [Fact]
    public async Task A_second_run_reads_nothing_writes_nothing_and_raises_no_changed()
    {
        var worker = Worker();
        await worker.RunOnceAsync(TestContext.Current.CancellationToken);
        var asked = _snapshots.Asked.Count;
        var changed = 0;
        _subjects.Changed += () => changed++;
        _remarks.Changed += () => changed++;

        var reports = await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(reports);
        Assert.Equal(asked, _snapshots.Asked.Count);
        Assert.Equal(0, changed);
    }

    /// <summary>With the mark gone — another device's first run, or a lost file —
    /// the import runs again and writes nothing over what the stores hold: a state
    /// a person moved on, a remark they deleted.</summary>
    [Fact]
    public async Task An_import_run_again_never_overwrites_what_the_stores_hold()
    {
        await Worker().RunOnceAsync(TestContext.Current.CancellationToken);
        var debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-paging.md");
        _subjects.SetState(Alias, debt, "resolved", "DEV-TOWER");
        var deleted = _remarks.List(Alias, ".domain/shop/domain.md")[0];
        _remarks.Delete(deleted.Id);
        var changed = 0;
        _subjects.Changed += () => changed++;
        _remarks.Changed += () => changed++;

        var report = Assert.Single(await Worker(new MemoryLedger()).RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.True(report.Completed);
        Assert.Equal(0, report.SubjectsWritten);
        Assert.Equal(0, report.RemarksWritten);
        Assert.Equal(0, changed);
        Assert.Equal("resolved", _subjects.Get(Alias, debt)!.State);
        Assert.DoesNotContain(_remarks.List(Alias, ".domain/shop/domain.md"), remark => remark.Id == deleted.Id);
    }

    [Fact]
    public async Task When_the_default_branch_cannot_be_read_nothing_is_written_or_marked_and_the_next_run_retries()
    {
        _snapshots.Failure = "Could not reach GitHub.";
        var worker = Worker();

        var failed = Assert.Single(await worker.RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.False(failed.Completed);
        Assert.Contains("Could not reach GitHub.", failed.Failure);
        Assert.False(_ledger.HasImported(Alias));
        Assert.Empty(_subjects.List(Alias));
        Assert.Empty(_remarks.List(Alias, ".arc42/01-introduction.md"));

        _snapshots.Failure = null;
        var retried = Assert.Single(await worker.RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.True(retried.Completed);
        Assert.True(_ledger.HasImported(Alias));
    }

    /// <summary>A lookup GitHub refused may answer next time, so the repository is
    /// left unmarked; everything that did not need it is imported now, and the
    /// retry writes only what is still absent.</summary>
    [Fact]
    public async Task A_refused_pull_request_lookup_leaves_the_repository_unmarked_and_imports_the_rest()
    {
        _github.Refuse(".devbook/arc42/adr/0001-pay-by-card.md", "API rate limit exceeded");

        var report = Assert.Single(await Worker().RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.False(report.Completed);
        Assert.False(_ledger.HasImported(Alias));
        Assert.Contains(report.Skipped, skip => skip.Path == ".devbook/arc42/adr/0001-pay-by-card.md" && skip.Reason.Contains("API rate limit exceeded", StringComparison.Ordinal));
        Assert.DoesNotContain(_subjects.List(Alias), state => state.Subject.Kind == DevbookSubjectKind.Decision);
        Assert.Contains(_subjects.List(Alias), state => state.Subject.Key == "pay-by-card");
        Assert.Equal(3, _remarks.List(Alias, ".arc42/01-introduction.md").Count);
    }

    [Fact]
    public async Task Archived_repositories_and_the_brain_are_never_read()
    {
        _repositories.Clear();
        _repositories.Add(new GitHubRepositoryRef("old-shop", "JSdotNet", "OldShop") { Archived = true });
        _repositories.Add(new GitHubRepositoryRef(BrainScope.Value, "JSdotNet", "Brain"));

        var reports = await Worker().RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(reports);
        Assert.Empty(_snapshots.Asked);
        Assert.False(_ledger.HasImported("old-shop"));
    }

    [Fact]
    public async Task It_runs_once_after_a_short_delay_and_again_when_the_folders_change()
    {
        var worker = Worker();
        Assert.Empty(_snapshots.Asked);

        _time.Advance(DevbookImportWorker.FirstRunDelay);
        await worker.Running;

        Assert.True(_ledger.HasImported(Alias));

        _repositories.Add(new GitHubRepositoryRef("tower", "JSdotNet", "Tower"));
        _folders.Raise();
        await worker.Running;

        Assert.True(_ledger.HasImported("tower"));
    }

    [Fact]
    public async Task Nothing_is_read_while_the_devbook_is_switched_off()
    {
        _features.On = false;
        var worker = Worker();

        _time.Advance(DevbookImportWorker.FirstRunDelay);
        await worker.Running;
        _folders.Raise();
        await worker.Running;

        Assert.Empty(_snapshots.Asked);
        Assert.Empty(await worker.RunOnceAsync(TestContext.Current.CancellationToken));

        _features.On = true;
        _features.Raise();
        await worker.Running;

        Assert.True(_ledger.HasImported(Alias));
    }

    /// <summary>The snapshot on disk may be one a panel fetched weeks ago. The
    /// import is read once and marked for good, so the index is brought to the
    /// default branch's head first, and what is planned is the head's.</summary>
    [Fact]
    public async Task An_index_behind_the_head_is_brought_to_the_head_before_anything_is_read()
    {
        _snapshots.Trees["old"] = StaleTree();
        _snapshots.Index = "old";

        var report = Assert.Single(await Worker().RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.True(report.Completed);
        Assert.Equal(["fetch", "ensure"], _snapshots.Calls);
        Assert.Equal("head", _snapshots.Index);
        Assert.Equal("planned", _subjects.Get(Alias, DevbookSubject.Debt(".devbook/arc42/tdr/0001-paging.md"))!.State);
        Assert.True(_ledger.HasImported(Alias));
    }

    /// <summary>A head that cannot be checked leaves an old snapshot on disk; it
    /// is not read, nothing is written, and the repository waits for its default
    /// branch rather than being marked from a commit it has moved past.</summary>
    [Fact]
    public async Task When_the_head_cannot_be_checked_an_old_snapshot_is_not_read_and_nothing_is_marked()
    {
        _snapshots.Trees["old"] = StaleTree();
        _snapshots.Index = "old";
        _snapshots.FetchFailure = "API rate limit exceeded";

        var report = Assert.Single(await Worker().RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.False(report.Completed);
        Assert.Contains("API rate limit exceeded", report.Failure);
        Assert.Equal(["fetch"], _snapshots.Calls);
        Assert.Empty(_subjects.List(Alias));
        Assert.False(_ledger.HasImported(Alias));
    }

    /// <summary>GitHub answered, and the answer settles nothing — a history too
    /// long to read to its first commit. It will answer the same next time, so
    /// the record is passed over and reported, and the mark is not held back.</summary>
    [Fact]
    public async Task A_lookup_github_answers_but_cannot_settle_is_skipped_and_does_not_hold_the_mark_back()
    {
        _github.CannotSettle(".devbook/arc42/adr/0001-pay-by-card.md", "It has more than 2000 commits on the default branch.");

        var report = Assert.Single(await Worker().RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.True(report.Completed);
        Assert.Contains(report.Skipped, skip => skip.Path == ".devbook/arc42/adr/0001-pay-by-card.md" && skip.Reason.Contains("2000", StringComparison.Ordinal));
        Assert.True(_ledger.HasImported(Alias));
    }

    /// <summary>A tree as an older commit of the default branch had it: the debt
    /// record still <c>identified</c>.</summary>
    private string StaleTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "devbook-import-stale-" + Guid.NewGuid().ToString("N"));
        var debt = Path.Combine(root, ".devbook", "arc42", "tdr");
        Directory.CreateDirectory(debt);
        File.WriteAllText(Path.Combine(debt, "0001-paging.md"), "# TDR 0001: Orders are not paged\n\n```meta\nstatus: identified\n```\n");
        _temporary.Add(root);

        return root;
    }

    public void Dispose()
    {
        foreach (var worker in _workers) worker.Dispose();

        foreach (var root in _temporary)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder left behind is not a test failure.
            }
        }
    }

    /// <summary>The default branch: a head, an index on disk that may lag it, and
    /// the tree each commit has — the fixture at the head.</summary>
    private sealed class FakeSnapshots : IDevbookSnapshotCache
    {
        public string Head { get; set; } = "head";

        /// <summary>The commit the index on disk describes.</summary>
        public string Index { get; set; } = "head";

        public Dictionary<string, string> Trees { get; } = new(StringComparer.Ordinal) { ["head"] = FixtureRoot };

        /// <summary>Why the head cannot be checked, or null.</summary>
        public string? FetchFailure { get; set; }

        /// <summary>Why the files cannot be fetched, or null.</summary>
        public string? Failure { get; set; }

        public List<string> Calls { get; } = [];

        public List<(string Repository, string? Branch, IReadOnlyCollection<string> Selection)> Asked { get; } = [];

        private DevbookSnapshot Snapshot => new("main", Index, Now);

        public string SnapshotPath(GitHubRepositoryRef repository, string? branch) => Trees[Index];

        public DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch) => Snapshot;

        public DevbookSnapshotIndex? TryReadIndex(GitHubRepositoryRef repository, string? branch) => null;

        /// <summary>As the cache answers: a refusal keeps the old index and says
        /// why, a current one says so, a behind one moves to the head — each with
        /// a message, as the cache gives one either way.</summary>
        public Task<DevbookSnapshotResult> FetchAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add("fetch");

            if (FetchFailure is not null) return Task.FromResult(DevbookSnapshotResult.Failed(Snapshot, FetchFailure));

            if (Index == Head) return Task.FromResult(new DevbookSnapshotResult(Snapshot, false, false, "Up to date with main."));

            Index = Head;
            return Task.FromResult(new DevbookSnapshotResult(Snapshot, true, false, "Updated to the latest main."));
        }

        public Task<DevbookSnapshotResult> CheckAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The import fetches; it never only checks.");

        public Task<DevbookSnapshotResult> EnsureAsync(GitHubRepositoryRef repository, string? branch, IReadOnlyCollection<string> selection, CancellationToken cancellationToken = default)
        {
            lock (Calls)
            {
                Calls.Add("ensure");
                Asked.Add((repository.Alias, branch, selection));
            }

            return Task.FromResult(Failure is null
                ? new DevbookSnapshotResult(Snapshot, true, false, null)
                : DevbookSnapshotResult.Failed(null, Failure));
        }
    }

    /// <summary>GitHub's answer per path: an addition, or a refusal; any other path
    /// has no history.</summary>
    private sealed class FakeGitHub : IGitHubClient
    {
        private readonly Dictionary<string, GitHubPathAdditionRead> _answers = new(StringComparer.Ordinal);

        public void Answer(string path, GitHubPathAddition addition) => _answers[path] = GitHubPathAdditionRead.Found(addition);

        public void Refuse(string path, string reason) => _answers[path] = GitHubPathAdditionRead.Failed(reason);

        public void CannotSettle(string path, string reason) => _answers[path] = GitHubPathAdditionRead.Unsettled(reason);

        public Task<GitHubPathAdditionRead> FindPathAdditionAsync(GitHubRepositoryRef repository, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_answers.GetValueOrDefault(path, GitHubPathAdditionRead.NotFound));

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryLedger : IDevbookImportLedger
    {
        private readonly HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);

        public bool HasImported(string repositoryAlias)
        {
            lock (_marked) return _marked.Contains(repositoryAlias);
        }

        public void MarkImported(string repositoryAlias, DateTimeOffset at)
        {
            lock (_marked) _marked.Add(repositoryAlias);
        }
    }

    /// <summary>The <c>repository-devbook</c> switch, and true for every other
    /// key.</summary>
    private sealed class DevbookFlag(bool on) : IAppFeatureSettings
    {
        private bool _on = on;

        public event Action? Changed;

        public bool On
        {
            get => Volatile.Read(ref _on);
            set => Volatile.Write(ref _on, value);
        }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(in memory)";

        public bool IsEnabled(string key) => !string.Equals(key, DevbookFeatures.RepositoryDevbook, StringComparison.Ordinal) || On;

        public string? SetEnabled(string key, bool enabled) => null;

        public void Raise() => Changed?.Invoke();
    }

    /// <summary>A folder source whose only use here is the news that something
    /// moved.</summary>
    private sealed class ChangingFolders : IDevbookFolderSource
    {
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public void NotifyContentChanged() => Changed?.Invoke();

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => DevbookFolderSetting.Defaults();

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            DevbookFolderLocation.Unavailable(key, "Not resolved here.");
    }
}
