using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The backup loop: armed from the settings, fired by the clock, and one
/// consistent copy of the database committed each time. Driven through a
/// <see cref="FakeTimeProvider"/> so a slot is reached by moving the clock
/// rather than waiting for it, and through a fake client so nothing here talks
/// to GitHub.
/// </summary>
public sealed class BackupWorkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-backup-worker-tests", Guid.NewGuid().ToString("n"));

    // Tuesday 15 September 2026, 09:00. FakeTimeProvider's local zone is UTC
    // unless told otherwise, so local and UTC agree throughout.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));

    private readonly RecordingGitHubClient _gitHub = new();

    private readonly WorkspaceSettingsStore _settings;
    private readonly FileBackupStateStore _state;

    public BackupWorkerTests()
    {
        _settings = new WorkspaceSettingsStore(_root, Path.Combine(_root, "settings.json"), _ => null);
        _state = new FileBackupStateStore(Path.Combine(_root, "backup-state.json"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private BackupWorker Worker() => new(_settings, _gitHub, _state, _time);

    private static readonly BackupSchedule DailyAtSix = BackupSchedule.Off with { Cadence = BackupCadence.Daily, At = new TimeOnly(18, 0) };

    private async Task SeedDatabaseAsync()
    {
        await new SqliteTaskRepository(_settings.RootDirectory)
            .SaveAsync(new TaskItem("Back me up", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Waits for the run in flight to end. The worker raises
    /// <c>Changed</c> at both ends; the end is the one with nothing running.</summary>
    private static async Task RunToEndAsync(BackupWorker worker, Func<Task> trigger)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (!worker.IsRunning && worker.Last.AttemptedAt is not null) ended.TrySetResult();
        }

        worker.Changed += OnChanged;
        try
        {
            await trigger();
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            worker.Changed -= OnChanged;
        }
    }

    [Fact]
    public void Nothing_is_armed_until_there_is_a_repository_and_a_schedule()
    {
        using var worker = Worker();
        Assert.Null(worker.NextDue);
        Assert.False(worker.IsConfigured);

        _settings.TrySetRepository("JSdotNet/Notes");
        Assert.Null(worker.NextDue);
        Assert.True(worker.IsConfigured);

        _settings.SetBackupSchedule(DailyAtSix);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero), worker.NextDue);

        _settings.ClearRepository();
        Assert.Null(worker.NextDue);
    }

    [Fact]
    public async Task The_slot_fires_and_commits_one_copy_of_the_database()
    {
        await SeedDatabaseAsync();
        _settings.TrySetRepository("JSdotNet/Notes");
        _settings.SetBackupSchedule(DailyAtSix);
        using var worker = Worker();

        await RunToEndAsync(worker, () =>
        {
            _time.Advance(TimeSpan.FromHours(9) + TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        });

        var commit = Assert.Single(_gitHub.Commits);
        Assert.Equal("JSdotNet/Notes", commit.Repository.FullName);
        Assert.Equal(BackupWorker.RepositoryPath, commit.Path);
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(commit.Content, 0, 16));

        Assert.True(worker.Last.Committed);
        Assert.Null(worker.Last.Error);
        Assert.NotNull(worker.Last.SucceededAt);

        // Re-armed for tomorrow, not for the slot that just fired.
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 18, 0, 0, TimeSpan.Zero), worker.NextDue);
    }

    /// <summary>A slot that went by while the app was closed is owed, and taken
    /// shortly after start rather than at the next slot.</summary>
    [Fact]
    public async Task A_slot_missed_while_closed_is_caught_up_shortly_after_start()
    {
        await SeedDatabaseAsync();
        _settings.TrySetRepository("JSdotNet/Notes");
        _settings.SetBackupSchedule(DailyAtSix);
        _state.Save(new BackupState(_time.GetUtcNow().AddDays(-2), _time.GetUtcNow().AddDays(-2), true, null));
        using var worker = Worker();

        Assert.Equal(_time.GetLocalNow() + BackupWorker.CatchUpDelay, worker.NextDue);

        await RunToEndAsync(worker, () =>
        {
            _time.Advance(BackupWorker.CatchUpDelay + TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        });

        Assert.Single(_gitHub.Commits);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero), worker.NextDue);
    }

    /// <summary>A machine that already took yesterday's slot owes nothing this
    /// morning.</summary>
    [Fact]
    public void A_slot_already_taken_is_not_owed_again()
    {
        _settings.TrySetRepository("JSdotNet/Notes");
        _settings.SetBackupSchedule(DailyAtSix);
        _state.Save(new BackupState(_time.GetUtcNow().AddHours(-15), _time.GetUtcNow().AddHours(-15), true, null));
        using var worker = Worker();

        Assert.Equal(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero), worker.NextDue);
    }

    [Fact]
    public async Task The_button_works_without_a_schedule_and_an_unchanged_database_is_reported_as_such()
    {
        await SeedDatabaseAsync();
        _settings.TrySetRepository("JSdotNet/Notes");
        _gitHub.AnswerCommitted = false;
        using var worker = Worker();

        await RunToEndAsync(worker, worker.RequestBackupAsync);

        Assert.Single(_gitHub.Commits);
        Assert.False(worker.Last.Committed);
        Assert.Null(worker.Last.Error);
        Assert.NotNull(worker.Last.SucceededAt);
        Assert.Null(worker.NextDue);
    }

    [Fact]
    public async Task A_refusal_is_reported_in_githubs_words_and_the_last_success_is_kept()
    {
        await SeedDatabaseAsync();
        _settings.TrySetRepository("JSdotNet/Notes");
        var earlier = _time.GetUtcNow().AddDays(-1);
        _state.Save(new BackupState(earlier, earlier, true, null));
        _gitHub.Refusal = new GitHubException("GitHub rejected the token — check it hasn't expired.");
        using var worker = Worker();

        await RunToEndAsync(worker, worker.RequestBackupAsync);

        Assert.Equal("GitHub rejected the token — check it hasn't expired.", worker.Last.Error);
        Assert.Equal(earlier, worker.Last.SucceededAt);
        Assert.Equal(_time.GetUtcNow(), worker.Last.AttemptedAt);
    }

    [Fact]
    public async Task A_backlog_with_no_database_yet_is_said_rather_than_thrown()
    {
        _settings.TrySetRepository("JSdotNet/Notes");
        using var worker = Worker();

        await RunToEndAsync(worker, worker.RequestBackupAsync);

        Assert.Empty(_gitHub.Commits);
        Assert.Equal("There is no backlog database to back up yet.", worker.Last.Error);
    }

    [Fact]
    public async Task A_request_with_no_repository_does_nothing()
    {
        await SeedDatabaseAsync();
        using var worker = Worker();

        await worker.RequestBackupAsync();

        Assert.Empty(_gitHub.Commits);
        Assert.Null(worker.Last.AttemptedAt);
    }

    /// <summary>Pressing the button twice is one backup.</summary>
    [Fact]
    public async Task A_request_during_a_run_starts_no_second_one()
    {
        await SeedDatabaseAsync();
        _settings.TrySetRepository("JSdotNet/Notes");
        _gitHub.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = Worker();

        var first = worker.RequestBackupAsync();
        await _gitHub.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(worker.IsRunning);

        await worker.RequestBackupAsync();

        _gitHub.Hold.SetResult();
        await first;

        Assert.Single(_gitHub.Commits);
        Assert.False(worker.IsRunning);
    }

    [Fact]
    public void The_state_survives_a_restart_and_an_unreadable_file_reads_as_none()
    {
        var path = Path.Combine(_root, "state", "backup-state.json");
        var at = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);
        new FileBackupStateStore(path).Save(new BackupState(at, at, true, null));

        var reopened = new FileBackupStateStore(path);
        Assert.Equal(at, reopened.Current.SucceededAt);
        Assert.True(reopened.Current.Committed);

        File.WriteAllText(path, "not json");
        Assert.Equal(BackupState.None, new FileBackupStateStore(path).Current);
    }

    private sealed class RecordingGitHubClient : IGitHubClient
    {
        public List<(GitHubRepositoryRef Repository, string Path, byte[] Content, string Message)> Commits { get; } = [];

        public bool AnswerCommitted { get; set; } = true;

        public GitHubException? Refusal { get; set; }

        /// <summary>When set, a commit waits on this before answering, so a
        /// test can hold a run open.</summary>
        public TaskCompletionSource? Hold { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Hold is not null) await Hold.Task.WaitAsync(cancellationToken);
            if (Refusal is not null) throw Refusal;

            Commits.Add((repository, path, content, commitMessage));
            return new GitHubCommittedFile(path, "sha", AnswerCommitted);
        }

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
