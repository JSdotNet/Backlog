using System.Collections.Concurrent;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A local git service whose answers a test controls and can hold back: a
/// status asked about a held folder waits until the test releases it, and a
/// clone waits on <see cref="CloneGate"/>. That is what lets a Settings test
/// look at the screen while git is still thinking - the busy button, the
/// loader, an answer arriving after the question changed.
/// </summary>
internal sealed class GatedLocalGit : ILocalGitRepositoryService
{
    private readonly ConcurrentDictionary<string, ManualResetEventSlim> _held = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _answered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _clones = new(StringComparer.Ordinal);
    private int _cloneCalls;

    /// <summary>What a clone answers once released; the default succeeds and
    /// makes the folder a clone of the repository asked for.</summary>
    public TaskCompletionSource<Exception?> CloneGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CloneCalls => Volatile.Read(ref _cloneCalls);

    /// <summary>Makes <paramref name="directory"/> a clone whose origin is
    /// <paramref name="fullName"/>.</summary>
    public void Cloned(string directory, string fullName) => _clones[directory] = fullName;

    public void Hold(string directory) => _held[directory] = new ManualResetEventSlim(false);

    public void Release(string directory) => _held[directory].Set();

    /// <summary>Completes once a status about <paramref name="directory"/> has
    /// been answered.</summary>
    public Task Answered(string directory) => _answered.GetOrAdd(directory, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public LocalGitRepositoryStatus GetStatus(GitHubRepositoryRef repository, string? cloneDirectory)
    {
        if (string.IsNullOrWhiteSpace(cloneDirectory))
        {
            return new LocalGitRepositoryStatus(null, IsCloned: false, CanClone: false, Summary: "No local clone directory configured yet.");
        }

        if (_held.TryGetValue(cloneDirectory, out var gate)) gate.Wait(TimeSpan.FromSeconds(30));

        try
        {
            if (_clones.TryGetValue(cloneDirectory, out var origin))
            {
                return string.Equals(origin, repository.FullName, StringComparison.OrdinalIgnoreCase)
                    ? new LocalGitRepositoryStatus(cloneDirectory, IsCloned: true, CanClone: false, Summary: $"Local clone is ready: {cloneDirectory}")
                    : new LocalGitRepositoryStatus(cloneDirectory, IsCloned: false, CanClone: false, Summary: $"Not cloned: {cloneDirectory} is a git clone, but its origin is not {repository.FullName}.");
            }

            return new LocalGitRepositoryStatus(cloneDirectory, IsCloned: false, CanClone: true, Summary: $"Not cloned yet: {cloneDirectory}");
        }
        finally
        {
            _answered.GetOrAdd(cloneDirectory, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        }
    }

    public async Task<LocalGitRepositoryCloneResult> CloneAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _cloneCalls);

        var failure = await CloneGate.Task;
        if (failure is not null) throw failure;

        _clones[cloneDirectory!] = repository.FullName;
        return LocalGitRepositoryCloneResult.Succeeded($"Cloned {repository.FullName}.", cloneDirectory!);
    }

    public Task<LocalGitRepositoryUpdateCheck> CheckForUpdatesAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<LocalGitRepositoryPullResult> PullAsync(
        GitHubRepositoryRef repository,
        string? cloneDirectory,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
