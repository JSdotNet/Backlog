using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// A <see cref="IRoadmapPlanRepository"/> that follows a folder somebody can move.
/// <para>
/// The desktop lets you point the app at a different storage folder while it is
/// running. Handlers should not have to know that, and the container cannot
/// re-resolve a singleton on a settings change, so the current root is read per call
/// and the underlying repository is rebuilt only when it actually changes — the same
/// arrangement as <c>RootedFileBacklogRepository</c>.
/// </para>
/// </summary>
public sealed class RootedJsonRoadmapPlanRepository(Func<string> currentRootDirectory) : IRoadmapPlanRepository
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private string? _rootDirectory;
    private JsonRoadmapPlanRepository? _repository;

    public string PlanPath => Current.PlanPath;

    private JsonRoadmapPlanRepository Current
    {
        get
        {
            var root = _currentRootDirectory();

            if (_repository is null
                || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _rootDirectory = root;
                _repository = new JsonRoadmapPlanRepository(root);
            }

            return _repository;
        }
    }

    public Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default) =>
        Current.LoadAsync(cancellationToken);

    public Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(plan, cancellationToken);
}
