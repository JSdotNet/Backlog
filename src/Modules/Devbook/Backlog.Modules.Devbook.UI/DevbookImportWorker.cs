using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Runs the one-time devbook import: for each registered repository this device has
/// not imported yet, brings the default branch's snapshot to the branch's head,
/// reads it, plans it
/// (<see cref="DevbookMarkdownImport"/>) and writes the plan into the subject and
/// remark stores through their import write alone.
/// <para>
/// Once per repository. A repository is marked in the
/// <see cref="IDevbookImportLedger"/> only when its import ran to the end; when the
/// default branch cannot be read, or GitHub refused a lookup the plan needed, nothing
/// is marked and the next run tries again — writing only what is still absent, since
/// <see cref="IDevbookSubjectStateStore.ImportIfAbsent"/> and
/// <see cref="IDevbookAnnotationStore.ImportIfAbsent"/> never overwrite. A decision
/// record no pull request added is passed over and reported, and does not hold the
/// mark back: GitHub would answer the same next time.
/// </para>
/// <para>
/// Archived repositories are skipped (local ADR 0029 §4: nothing is read from GitHub
/// for one), and so is the Brain, which is no registered repository. Nothing is
/// written into any repository, and no remark notification is raised — the import
/// write is not replication's <c>Apply</c>.
/// </para>
/// <para>
/// A singleton the heads resolve once after building, like the sync workers: it runs
/// once a short delay after start, and again whenever
/// <see cref="IDevbookFolderSource.Changed"/> or the feature switches say something
/// moved — a repository added, a snapshot landed — while
/// <see cref="DevbookFeatures.RepositoryDevbook"/> is on. A run already in flight
/// takes the request as one more pass rather than starting a second beside it.
/// </para>
/// </summary>
public sealed class DevbookImportWorker : IDisposable
{
    /// <summary>How long after start the first run waits, so the import does not
    /// compete with drawing the window and the sync loops' first cycles.</summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromSeconds(20);

    private readonly Func<IReadOnlyList<GitHubRepositoryRef>> _repositories;
    private readonly IDevbookFolderSource _folders;
    private readonly IDevbookSnapshotCache _snapshots;
    private readonly IGitHubClient _github;
    private readonly IDevbookImportLedger _ledger;
    private readonly IDevbookSubjectStateStore _subjects;
    private readonly IDevbookAnnotationStore _remarks;
    private readonly IAppFeatureSettings _features;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();

    private ITimer? _firstRun;
    private bool _started;
    private bool _inFlight;
    private bool _again;
    private bool _disposed;
    private Task _running = Task.CompletedTask;

    public DevbookImportWorker(
        Func<IReadOnlyList<GitHubRepositoryRef>> repositories,
        IDevbookFolderSource folders,
        IDevbookSnapshotCache snapshots,
        IGitHubClient github,
        IDevbookImportLedger ledger,
        IDevbookSubjectStateStore subjects,
        IDevbookAnnotationStore remarks,
        IAppFeatureSettings features,
        TimeProvider time,
        ILogger<DevbookImportWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(github);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(remarks);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(time);

        _repositories = repositories;
        _folders = folders;
        _snapshots = snapshots;
        _github = github;
        _ledger = ledger;
        _subjects = subjects;
        _remarks = remarks;
        _features = features;
        _time = time;
        _log = log ?? NullLogger<DevbookImportWorker>.Instance;

        _folders.Changed += OnSomethingMoved;
        _features.Changed += OnSomethingMoved;

        _firstRun = _time.CreateTimer(_ => OnFirstRun(), state: null, FirstRunDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>What the last run that read anything reported, one row per
    /// repository it read.</summary>
    public IReadOnlyList<DevbookImportReport> LastReports { get; private set; } = [];

    /// <summary>The run in flight, or a finished task. Exposed for tests, which
    /// otherwise have nothing to await.</summary>
    internal Task Running
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _firstRun?.Dispose();
            _firstRun = null;
        }

        _folders.Changed -= OnSomethingMoved;
        _features.Changed -= OnSomethingMoved;

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>
    /// One pass over every registered repository: each one that is active, not the
    /// Brain and not yet marked is imported, and reported. Nothing at all while
    /// <see cref="DevbookFeatures.RepositoryDevbook"/> is off.
    /// </summary>
    internal async Task<IReadOnlyList<DevbookImportReport>> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_features.IsEnabled(DevbookFeatures.RepositoryDevbook)) return [];

        var reports = new List<DevbookImportReport>();

        foreach (var repository in _repositories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (repository.Archived || BrainScope.Is(repository.Alias) || string.IsNullOrWhiteSpace(repository.Alias)) continue;
            if (_ledger.HasImported(repository.Alias)) continue;

            reports.Add(await ImportAsync(repository, cancellationToken).ConfigureAwait(false));
        }

        if (reports.Count > 0) LastReports = reports;

        return reports;
    }

    private async Task<DevbookImportReport> ImportAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken)
    {
        var alias = repository.Alias;
        var folders = repository.DevbookFolders;

        // The index first, brought to the default branch's head. EnsureAsync fills
        // files at whatever commit the index on disk describes, and that may be a
        // snapshot a panel fetched weeks ago; the import is read once and marked for
        // good, so planning from an old commit would leave the stores on states the
        // branch has since left, with nothing to correct them. FetchAsync lists the
        // commit only when the head moved. Its message is no failure signal — "Up to
        // date" is a message too — and a failed refresh still carries the old
        // snapshot, so IsFailure is what is asked.
        var head = await _snapshots.FetchAsync(repository, branch: null, cancellationToken).ConfigureAwait(false);

        if (head.IsFailure || head.Snapshot is null)
        {
            return Waits(repository, head.Message);
        }

        var read = await _snapshots.EnsureAsync(
            repository,
            branch: null,
            DevbookMarkdownImport.Selection(folders),
            cancellationToken).ConfigureAwait(false);

        // EnsureAsync answers a message only when something did not land.
        if (read.IsFailure || read.Snapshot is null || read.Message is not null)
        {
            return Waits(repository, read.Message);
        }

        var plan = DevbookMarkdownImport.Plan(alias, _snapshots.SnapshotPath(repository, branch: null), folders);
        var skipped = new List<DevbookImportSkip>(plan.Skipped);
        var additions = new Dictionary<string, GitHubPathAdditionRead>(StringComparer.Ordinal);
        var complete = true;

        var states = new List<DevbookSubjectState>();
        foreach (var subject in plan.Subjects)
        {
            if (!subject.NeedsPullRequest && !subject.NeedsCommitTime)
            {
                states.Add(subject.ToState(alias, committedAt: null));
                continue;
            }

            var addition = await AdditionAsync(repository, subject.Path, additions, cancellationToken).ConfigureAwait(false);

            if (Unusable(subject.Path, addition, subject.NeedsPullRequest) is { } skip)
            {
                skipped.Add(skip);
                complete &= addition.IsRead;
                continue;
            }

            states.Add(subject.ToState(alias, addition.Addition!.CommittedAt, addition.Addition.PullRequest));
        }

        var remarks = new List<DevbookAnnotation>();
        foreach (var note in plan.Notes)
        {
            if (!note.NeedsCommitTime)
            {
                remarks.Add(note.ToAnnotation(alias, committedAt: null));
                continue;
            }

            var addition = await AdditionAsync(repository, note.Path, additions, cancellationToken).ConfigureAwait(false);

            if (Unusable(note.Path, addition, needsPullRequest: false) is { } skip)
            {
                skipped.Add(skip);
                complete &= addition.IsRead;
                continue;
            }

            remarks.Add(note.ToAnnotation(alias, addition.Addition!.CommittedAt));
        }

        var subjectsWritten = _subjects.ImportIfAbsent(states);
        var remarksWritten = _remarks.ImportIfAbsent(remarks);

        if (complete) _ledger.MarkImported(alias, _time.GetUtcNow());

        foreach (var skip in skipped)
        {
            _log.LogInformation("The devbook import of {Repository} passed over {Path}: {Reason}", repository.FullName, skip.Path, skip.Reason);
        }

        _log.LogInformation(
            "The devbook import of {Repository} wrote {States} state(s) and {Remarks} remark(s){Outcome}.",
            repository.FullName,
            subjectsWritten,
            remarksWritten,
            complete ? string.Empty : "; a lookup GitHub refused leaves it to run again");

        return new DevbookImportReport(alias, complete, subjectsWritten, remarksWritten, skipped, Failure: null);
    }

    /// <summary>One path's addition, asked of GitHub once per run however many rows
    /// need it.</summary>
    private async Task<GitHubPathAdditionRead> AdditionAsync(
        GitHubRepositoryRef repository,
        string path,
        Dictionary<string, GitHubPathAdditionRead> asked,
        CancellationToken cancellationToken)
    {
        if (asked.TryGetValue(path, out var known)) return known;

        var read = await _github.FindPathAdditionAsync(repository, path, cancellationToken).ConfigureAwait(false);
        asked[path] = read;

        return read;
    }

    /// <summary>Why a looked-up row cannot be imported, or null when it
    /// can.</summary>
    private static DevbookImportSkip? Unusable(string path, GitHubPathAdditionRead read, bool needsPullRequest) =>
        !read.IsRead ? new DevbookImportSkip(path, $"GitHub could not say when it was added: {read.Failure}")
        : read.Addition is null ? new DevbookImportSkip(path, read.Unanswered ?? "The default branch has no history for it.")
        : needsPullRequest && read.Addition.PullRequest is null
            ? new DevbookImportSkip(path, $"No pull request added it (commit {read.Addition.CommitSha}), so there is none to key the decision by.")
            : null;

    /// <summary>The default branch could not be read at its head: nothing is
    /// written and nothing marked, and the next run asks again.</summary>
    private DevbookImportReport Waits(GitHubRepositoryRef repository, string? message)
    {
        var reason = message ?? $"The default branch of {repository.FullName} could not be read.";
        _log.LogInformation("The devbook import of {Repository} waits for its default branch: {Reason}", repository.FullName, reason);

        return DevbookImportReport.Failed(repository.Alias, reason);
    }

    private void OnFirstRun()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _started = true;
        }

        Request();
    }

    private void OnSomethingMoved()
    {
        lock (_gate)
        {
            if (!_started) return;
        }

        Request();
    }

    /// <summary>Starts a run, or asks the one in flight for one more pass.</summary>
    private void Request()
    {
        lock (_gate)
        {
            if (_disposed || !_features.IsEnabled(DevbookFeatures.RepositoryDevbook)) return;

            if (_inFlight)
            {
                _again = true;
                return;
            }

            _inFlight = true;
            _running = Task.Run(LoopAsync, CancellationToken.None);
        }
    }

    private async Task LoopAsync()
    {
        while (true)
        {
            lock (_gate) _again = false;

            try
            {
                await RunOnceAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (ObjectDisposedException)
            {
                // Disposed mid-run.
            }
            catch (Exception ex)
            {
                // A run that failed outside what it reports must not end the
                // process; the next start, or the next change, runs it again.
                _log.LogError(ex, "The devbook import failed unexpectedly.");
            }

            lock (_gate)
            {
                if (!_again || _disposed)
                {
                    _inFlight = false;
                    return;
                }
            }
        }
    }
}

/// <summary>
/// What the import did for one repository.
/// </summary>
/// <param name="Completed">Whether it ran to the end and the repository is marked.
/// False when the default branch could not be read (<see cref="Failure"/>) or a
/// lookup was refused (named in <see cref="Skipped"/>).</param>
/// <param name="Skipped">What it read and did not import, and why.</param>
public sealed record DevbookImportReport(
    string RepositoryAlias,
    bool Completed,
    int SubjectsWritten,
    int RemarksWritten,
    IReadOnlyList<DevbookImportSkip> Skipped,
    string? Failure)
{
    public static DevbookImportReport Failed(string repositoryAlias, string reason) =>
        new(repositoryAlias, false, 0, 0, [], reason);
}
