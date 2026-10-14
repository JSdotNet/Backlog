using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="ISprintRepositories"/>, answered from Tasks'
/// <see cref="IRepositoryDirectory"/>: the aliases of the registered repositories, which
/// are what a series holds and what <see cref="SprintTaskSource"/> matches a task's
/// repository against. Resolved per read, for the reason <see cref="SprintTaskSource"/>
/// gives; a host with no directory has no repositories to offer.
/// </summary>
public sealed class SprintRepositories(Func<IRepositoryDirectory?> repositories) : ISprintRepositories
{
    public IReadOnlyList<string> Aliases =>
    [
        .. (repositories()?.Repositories ?? [])
            .Select(repository => repository.Alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];
}
