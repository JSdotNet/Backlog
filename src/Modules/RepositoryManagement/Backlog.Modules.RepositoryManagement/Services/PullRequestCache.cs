using System.Globalization;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>
/// The one cache every reader of a pull request shares (local ADR 0042 §3): the answer
/// by number, the open list and the recently merged list of each repository, and the
/// reads still out for any of them.
/// <para>
/// On these terms, whoever asks:
/// </para>
/// <list type="bullet">
/// <item>a merged pull request cannot change again, so it is read once, kept for the
/// life of the process, and remembered across a restart through the
/// <see cref="ISettledPullRequestStore"/> — read once, lazily, at the first ask;</item>
/// <item>a number its repository answered without is not asked about again either;</item>
/// <item>any other answer — a closed one too, since GitHub can reopen it — is reused
/// while it is younger than the caller's age, and so is a read of it still out that
/// started inside that age: the caller waits for that read rather than sending a
/// second;</item>
/// <item>a repository that refused is kept nowhere, so the next read asks again;</item>
/// <item>an open list writes each of its rows into the answers by number, read at the
/// same time, so a reader asking by number after a list reads nothing;</item>
/// <item>an archived repository is never read (local ADR 0029): its numbers answer what
/// is kept, and its lists are skipped.</item>
/// </list>
/// <para>
/// Reads run on no caller's token: another caller may come to wait on one, and one
/// leaving must not cancel the answer the other is waiting for. An answer that lands
/// after a newer one for the same pull request was kept is dropped, so a slow read
/// cannot write an older state back over what a command's re-read found.
/// </para>
/// </summary>
internal sealed partial class PullRequestCache(
    IPullRequestSource source,
    IRegisteredRepositories registry,
    ISettledPullRequestStore settled,
    TimeProvider clock,
    ILogger? logger = null)
{
    /// <summary>The most numbers one read asks about, so one query stays the size the
    /// linked reads were batched to before.</summary>
    public const int ReadBatchSize = 25;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Answer> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pending<IReadOnlyDictionary<string, Answer>>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OpenRead> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pending<OpenRead>> _openPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MergedRead> _merged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pending<MergedRead>> _mergedPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _remembered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _epochs = new(StringComparer.OrdinalIgnoreCase);
    private bool _seeded;

    /// <summary>The pull requests <paramref name="pullRequests"/> name, in the order
    /// asked: from what is kept where it may be, from the source for the rest.</summary>
    public async Task<PullRequestListing> ReadAsync(
        IReadOnlyCollection<PullRequestReference> pullRequests,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        var wanted = Normalized(pullRequests);
        if (wanted.Count == 0) return PullRequestListing.Empty;

        EnsureSeeded();

        var kept = new Dictionary<string, Answer>(StringComparer.OrdinalIgnoreCase);
        var waits = new List<Task<IReadOnlyDictionary<string, Answer>>>();
        var asks = new List<(string Repository, IReadOnlyList<int> Numbers, Pending<IReadOnlyDictionary<string, Answer>> Read)>();

        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var unread = new List<PullRequestReference>();

            foreach (var pull in wanted)
            {
                var key = pull.Key;
                if (registry.IsArchived(pull.RepositoryFullName))
                {
                    if (_answers.TryGetValue(key, out var last) && last.PullRequest is not null) kept[key] = last;
                }
                else if (_answers.TryGetValue(key, out var answer) && answer.Holds(now, maxAge)) kept[key] = answer;
                else if (_pending.TryGetValue(key, out var pending) && now - pending.StartedAt < maxAge) waits.Add(pending.Read.Task);
                else unread.Add(pull);
            }

            foreach (var repository in unread.GroupBy(pull => pull.RepositoryFullName, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var batch in repository.Select(pull => pull.Number).Chunk(ReadBatchSize))
                {
                    var read = new Pending<IReadOnlyDictionary<string, Answer>>(now, EpochOf(repository.Key));
                    foreach (var number in batch) _pending[Key(repository.Key, number)] = read;
                    asks.Add((repository.Key, batch, read));
                    waits.Add(read.Read.Task);
                }
            }
        }

        if (kept.Count > 0) LogKept(_logger, kept.Count);
        foreach (var (repository, numbers, read) in asks) _ = ReadAndKeepAsync(repository, numbers, read);

        var landed = await Task.WhenAll(waits).WaitAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in landed)
        {
            foreach (var (key, answer) in batch) kept.TryAdd(key, answer);
        }

        var answers = wanted.Select(pull => kept.GetValueOrDefault(pull.Key)).OfType<Answer>().ToList();

        return new PullRequestListing(
            [.. answers.Where(answer => answer.PullRequest is not null).Select(answer => answer.PullRequest!)],
            [.. answers.Where(answer => answer.Failure is not null).Select(answer => answer.Failure!).Distinct()]);
    }

    /// <summary>What is kept for <paramref name="pullRequests"/>, however old, in the order
    /// asked, reading nothing.</summary>
    public PullRequestListing Kept(IReadOnlyCollection<PullRequestReference> pullRequests)
    {
        var wanted = Normalized(pullRequests);
        if (wanted.Count == 0) return PullRequestListing.Empty;

        EnsureSeeded();

        lock (_gate)
        {
            return new PullRequestListing(
                [.. wanted.Select(pull => _answers.GetValueOrDefault(pull.Key)?.PullRequest).OfType<PullRequestStatus>()],
                []);
        }
    }

    /// <summary>The open pull requests of each repository named — none archived — newest
    /// update first, from the list kept where it may be, from the source for the rest.</summary>
    public async Task<PullRequestListing> ListOpenAsync(IReadOnlyCollection<string> repositories, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        var reads = Start(
            repositories,
            maxAge,
            _open,
            _openPending,
            read => read.ReadAt,
            (repository, pending) => _ = ListAndKeepAsync(repository, pending));

        var landed = await Task.WhenAll(reads).WaitAsync(cancellationToken).ConfigureAwait(false);

        return new PullRequestListing(
            [.. landed.SelectMany(read => read.PullRequests).OrderByDescending(pull => pull.UpdatedAt)],
            [.. landed.Where(read => read.Failure is not null).Select(read => read.Failure!)]);
    }

    /// <summary>The recently merged pull requests of each repository named — none
    /// archived — newest merge first.</summary>
    public async Task<MergedPullRequestListing> ListMergedAsync(IReadOnlyCollection<string> repositories, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        var reads = Start(
            repositories,
            maxAge,
            _merged,
            _mergedPending,
            read => read.ReadAt,
            (repository, pending) => _ = ListMergedAndKeepAsync(repository, pending));

        var landed = await Task.WhenAll(reads).WaitAsync(cancellationToken).ConfigureAwait(false);

        return new MergedPullRequestListing(
            [.. landed.SelectMany(read => read.PullRequests).OrderByDescending(pull => pull.MergedAt)],
            [.. landed.Where(read => read.Failure is not null).Select(read => read.Failure!)],
            [.. landed.Where(read => read.Truncated).Select(read => read.Repository)]);
    }

    /// <summary>The status kept for one pull request, however old, or null for none.</summary>
    public PullRequestStatus? KeptStatus(PullRequestReference pullRequest)
    {
        EnsureSeeded();

        lock (_gate) return _answers.GetValueOrDefault(Normalized(pullRequest).Key)?.PullRequest;
    }

    /// <summary>
    /// After a command: the repository's open list read again and put in place of the
    /// one kept — and of each of its rows' answers — and its merged list dropped, since
    /// an act can have changed both. The pull request acted on is read by number as well
    /// when the list no longer holds it: a merge takes it out of the open list, and only
    /// that read says it merged.
    /// <para>
    /// A second command in the same repository may begin before this re-read lands. Its
    /// own re-read then speaks for the repository, so this one's list is not kept; but
    /// this act's pull request is still read by number, and a merge it finds is kept and
    /// remembered whatever came after — no act undoes a merge.
    /// </para>
    /// </summary>
    public async Task RereadAfterActAsync(PullRequestReference pullRequest, CancellationToken cancellationToken)
    {
        var acted = Normalized(pullRequest);
        var repository = acted.RepositoryFullName;
        var startedAt = clock.GetUtcNow();
        int epoch;

        // Everything read of the repository before the act, or still out, now says
        // what was true before it: none of it is kept or waited on from here.
        lock (_gate)
        {
            epoch = _epochs[repository] = EpochOf(repository) + 1;
            _openPending.Remove(repository);
            _mergedPending.Remove(repository);
            _open.Remove(repository);
            _merged.Remove(repository);
            foreach (var key in _pending.Keys.Where(key => key.StartsWith(repository + "#", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _pending.Remove(key);
            }
        }

        var list = await SafeListAsync(repository).WaitAsync(cancellationToken).ConfigureAwait(false);
        var listedAt = clock.GetUtcNow();

        lock (_gate)
        {
            if (EpochOf(repository) == epoch && list.Failure is null)
            {
                KeepOpen(repository, new OpenRead(repository, list.PullRequests, null, listedAt), startedAt);
            }
        }

        if (list.PullRequests.Any(pull => pull.Number == acted.Number)) return;

        var read = await SafeReadAsync(repository, [acted.Number]).WaitAsync(cancellationToken).ConfigureAwait(false);
        var answer = Answered(read, repository, acted.Number, clock.GetUtcNow());

        var merged = answer.PullRequest?.State == PullRequestState.Merged;

        lock (_gate)
        {
            if (answer.Failure is null && (merged || EpochOf(repository) == epoch)) Keep(acted.Key, answer, startedAt);
            else if (_answers.TryGetValue(acted.Key, out var last) && last.PullRequest?.State != PullRequestState.Merged) _answers.Remove(acted.Key);
        }

        Remember([answer]);
    }

    private List<Task<T>> Start<T>(
        IReadOnlyCollection<string> repositories,
        TimeSpan maxAge,
        Dictionary<string, T> kept,
        Dictionary<string, Pending<T>> pending,
        Func<T, DateTimeOffset> readAt,
        Action<string, Pending<T>> read)
    {
        var names = repositories
            .Where(repository => !string.IsNullOrWhiteSpace(repository))
            .Select(repository => repository.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(repository => !registry.IsArchived(repository))
            .ToList();

        var waits = new List<Task<T>>();
        var asks = new List<(string Repository, Pending<T> Read)>();

        lock (_gate)
        {
            var now = clock.GetUtcNow();

            foreach (var repository in names)
            {
                if (kept.TryGetValue(repository, out var answer) && now - readAt(answer) < maxAge) waits.Add(Task.FromResult(answer));
                else if (pending.TryGetValue(repository, out var inFlight) && now - inFlight.StartedAt < maxAge) waits.Add(inFlight.Read.Task);
                else
                {
                    var started = new Pending<T>(now, EpochOf(repository));
                    pending[repository] = started;
                    asks.Add((repository, started));
                    waits.Add(started.Read.Task);
                }
            }
        }

        foreach (var (repository, started) in asks) read(repository, started);

        return waits;
    }

    private async Task ReadAndKeepAsync(string repository, IReadOnlyList<int> numbers, Pending<IReadOnlyDictionary<string, Answer>> started)
    {
        var answers = new Dictionary<string, Answer>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var read = await SafeReadAsync(repository, numbers).ConfigureAwait(false);
            var readAt = clock.GetUtcNow();

            foreach (var number in numbers) answers[Key(repository, number)] = Answered(read, repository, number, readAt);
        }
        catch (OperationCanceledException)
        {
            // Nobody cancels this read, so the source gave up on its own: said below as
            // the repository's failure rather than left unobserved on a discarded task.
        }
        finally
        {
            // A read that threw before it answered — a transport that gave up, which
            // surfaces as a cancellation nobody asked for — is the repository's failure,
            // as the list reads say it, never "answered without", which is kept for good.
            var unfinished = new RepositoryReadFailure(repository, "The read did not finish.");
            foreach (var number in numbers) answers.TryAdd(Key(repository, number), new Answer(null, unfinished, started.StartedAt));

            lock (_gate)
            {
                // A read a command overtook says what was true before the act; only a
                // merge, which no act undoes, is still worth keeping from it.
                var current = EpochOf(repository) == started.Epoch;
                foreach (var (key, answer) in answers.Where(entry => entry.Value.Failure is null
                             && (current || entry.Value.PullRequest?.State == PullRequestState.Merged)))
                {
                    Keep(key, answer, started.StartedAt);
                }

                foreach (var number in numbers)
                {
                    var key = Key(repository, number);
                    if (_pending.TryGetValue(key, out var pending) && ReferenceEquals(pending, started)) _pending.Remove(key);
                }
            }

            Remember(answers.Values);
            started.Read.SetResult(answers);
        }
    }

    private async Task ListAndKeepAsync(string repository, Pending<OpenRead> started)
    {
        var answer = new OpenRead(repository, [], new RepositoryReadFailure(repository, "The read did not finish."), started.StartedAt);
        try
        {
            var read = await SafeListAsync(repository).ConfigureAwait(false);
            answer = new OpenRead(repository, read.PullRequests, read.Failure, clock.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            // The source gave up on its own: the answer stays "did not finish".
        }
        finally
        {
            lock (_gate)
            {
                if (answer.Failure is null && EpochOf(repository) == started.Epoch) KeepOpen(repository, answer, started.StartedAt);
                if (_openPending.TryGetValue(repository, out var pending) && ReferenceEquals(pending, started)) _openPending.Remove(repository);
            }

            started.Read.SetResult(answer);
        }
    }

    private async Task ListMergedAndKeepAsync(string repository, Pending<MergedRead> started)
    {
        var answer = new MergedRead(repository, [], new RepositoryReadFailure(repository, "The read did not finish."), false, started.StartedAt);
        try
        {
            MergedPullRequestSourceRead read;
            try
            {
                LogRead(_logger, "merged", repository, 0);
                read = await source.ListMergedAsync(repository, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                read = new MergedPullRequestSourceRead([], new RepositoryReadFailure(repository, exception.Message), false);
            }

            answer = new MergedRead(repository, read.PullRequests, read.Failure, read.Truncated, clock.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            // The source gave up on its own: the answer stays "did not finish".
        }
        finally
        {
            lock (_gate)
            {
                if (answer.Failure is null
                    && EpochOf(repository) == started.Epoch
                    && !(_merged.TryGetValue(repository, out var last) && last.ReadAt > started.StartedAt))
                {
                    _merged[repository] = answer;
                }

                if (_mergedPending.TryGetValue(repository, out var pending) && ReferenceEquals(pending, started)) _mergedPending.Remove(repository);
            }

            started.Read.SetResult(answer);
        }
    }

    /// <summary>The source's read by number, a throw said as the repository's failure.</summary>
    private async Task<PullRequestSourceRead> SafeReadAsync(string repository, IReadOnlyCollection<int> numbers)
    {
        try
        {
            LogRead(_logger, "by number", repository, numbers.Count);
            return await source.ReadAsync(repository, numbers, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PullRequestSourceRead([], new RepositoryReadFailure(repository, exception.Message));
        }
    }

    /// <summary>The source's open list, a throw said as the repository's failure.</summary>
    private async Task<PullRequestSourceRead> SafeListAsync(string repository)
    {
        try
        {
            LogRead(_logger, "open", repository, 0);
            return await source.ListOpenAsync(repository, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PullRequestSourceRead([], new RepositoryReadFailure(repository, exception.Message));
        }
    }

    /// <summary>One number's answer from a read of its repository: the pull request in
    /// that repository — GitHub answers under the name it was asked by, so one under
    /// another name is another repository's — the refusal that kept it unread, or
    /// neither: answered without it.</summary>
    private static Answer Answered(PullRequestSourceRead read, string repository, int number, DateTimeOffset readAt)
    {
        var pull = read.PullRequests.FirstOrDefault(candidate => candidate.Number == number && Same(candidate.RepositoryFullName, repository));
        return new Answer(pull, pull is null ? read.Failure : null, readAt);
    }

    /// <summary>Keeps an open list and its rows' answers, unless a newer list landed
    /// while this one was out. Called under the gate.</summary>
    private void KeepOpen(string repository, OpenRead read, DateTimeOffset startedAt)
    {
        if (_open.TryGetValue(repository, out var last) && last.ReadAt > startedAt) return;

        _open[repository] = read;
        foreach (var pull in read.PullRequests) Keep(pull.Reference.Key, new Answer(pull, null, read.ReadAt), startedAt);
    }

    /// <summary>Keeps one answer, unless the one kept is merged — it cannot change again —
    /// or landed after this read began. Called under the gate.</summary>
    private void Keep(string key, Answer answer, DateTimeOffset startedAt)
    {
        if (_answers.TryGetValue(key, out var last)
            && (last.PullRequest?.State == PullRequestState.Merged || last.ReadAt > startedAt))
        {
            return;
        }

        _answers[key] = answer;
    }

    /// <summary>Writes each merged answer to the settled-fact memory, once per pull
    /// request.</summary>
    private void Remember(IEnumerable<Answer> answers)
    {
        var merged = new List<PullRequestStatus>();

        lock (_gate)
        {
            foreach (var pull in answers.Select(answer => answer.PullRequest).OfType<PullRequestStatus>())
            {
                if (pull.State == PullRequestState.Merged && _remembered.Add(pull.Reference.Key)) merged.Add(pull);
            }
        }

        foreach (var pull in merged)
        {
            try
            {
                settled.Keep(new SettledPullRequest(
                    pull.RepositoryFullName.Trim(),
                    pull.Number,
                    pull.MergedAt,
                    string.IsNullOrEmpty(pull.BaseRefName) ? null : pull.BaseRefName,
                    pull.DefaultBranch,
                    pull.Title,
                    pull.Url,
                    pull.AuthorLogin)
                {
                    HeadRefName = pull.HeadRefName,
                    ClosingIssues = pull.ClosingIssues,
                    Labels = pull.Labels
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Not remembered: the next launch reads it from GitHub once more, which
                // is what it did before there was a memory at all.
                LogNotRemembered(_logger, exception, pull.Reference.Key);
            }
        }
    }

    /// <summary>Seeds the answers from the settled-fact memory, once, at the first ask.</summary>
    private void EnsureSeeded()
    {
        if (Volatile.Read(ref _seeded)) return;

        IReadOnlyList<SettledPullRequest> remembered;
        try
        {
            remembered = settled.ReadAll();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotRemembered(_logger, exception, "the settled pull requests");
            remembered = [];
        }

        lock (_gate)
        {
            if (_seeded) return;

            foreach (var pull in remembered)
            {
                var key = Normalized(pull.Reference).Key;
                _answers.TryAdd(key, new Answer(PullRequestStatus.Settled(pull), null, DateTimeOffset.MinValue));
                _remembered.Add(key);
            }

            Volatile.Write(ref _seeded, true);
        }
    }

    /// <summary>How many commands have read the repository again so far: a read begun
    /// under an earlier count was overtaken by an act. Called under the gate.</summary>
    private int EpochOf(string repository) => _epochs.GetValueOrDefault(repository.Trim());

    private static List<PullRequestReference> Normalized(IReadOnlyCollection<PullRequestReference> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        return
        [
            .. pullRequests
                .Where(pull => pull is not null && pull.Number > 0 && !string.IsNullOrWhiteSpace(pull.RepositoryFullName))
                .Select(Normalized)
                .DistinctBy(pull => pull.Key, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static PullRequestReference Normalized(PullRequestReference pullRequest) =>
        pullRequest with { RepositoryFullName = pullRequest.RepositoryFullName.Trim() };

    private static string Key(string repository, int number) =>
        $"{repository.Trim()}#{number.ToString(CultureInfo.InvariantCulture)}";

    private static bool Same(string repository, string other) =>
        string.Equals(repository.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pull requests: read {Kind} of {Repository} ({Count} asked)")]
    private static partial void LogRead(ILogger logger, string kind, string repository, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pull requests: {Count} answered from the cache")]
    private static partial void LogKept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pull requests: could not remember {PullRequest} across a restart")]
    private static partial void LogNotRemembered(ILogger logger, Exception exception, string pullRequest);

    /// <summary>One number's answer: the pull request, the refusal that kept it unread,
    /// or neither — answered without it.</summary>
    private sealed record Answer(PullRequestStatus? PullRequest, RepositoryReadFailure? Failure, DateTimeOffset ReadAt)
    {
        public bool Holds(DateTimeOffset now, TimeSpan maxAge) =>
            PullRequest is null || PullRequest.State is PullRequestState.Merged || now - ReadAt < maxAge;
    }

    /// <summary>One repository's open list as it was read.</summary>
    private sealed record OpenRead(string Repository, IReadOnlyList<PullRequestStatus> PullRequests, RepositoryReadFailure? Failure, DateTimeOffset ReadAt);

    /// <summary>One repository's recently merged list as it was read.</summary>
    private sealed record MergedRead(string Repository, IReadOnlyList<MergedPullRequest> PullRequests, RepositoryReadFailure? Failure, bool Truncated, DateTimeOffset ReadAt);

    /// <summary>A read out: when it began, under which of its repository's epochs, and
    /// what it lands with. Compared by reference, so a read that began later under the
    /// same key is never taken for it.</summary>
    private sealed class Pending<T>(DateTimeOffset startedAt, int epoch)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public int Epoch { get; } = epoch;

        public TaskCompletionSource<T> Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
