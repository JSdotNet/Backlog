using Backlog.Modules.Repositories.Abstractions.Environments;
using Backlog.Modules.Repositories.Abstractions.Services;

namespace Backlog.Modules.Repositories.Services;

/// <summary>
/// The environments a host with no per-device file keeps for the process only. A host
/// with one replaces it after <c>AddRepositoriesModule()</c>.
/// </summary>
internal sealed class InMemoryEnvironmentStore : IEnvironmentStore
{
    private readonly Lock _gate = new();
    private IReadOnlyList<StoredEnvironment> _environments = [];

    public event Action? Changed;

    public IReadOnlyList<StoredEnvironment>? Load()
    {
        lock (_gate) return _environments;
    }

    public string? Save(IReadOnlyList<StoredEnvironment> environments)
    {
        ArgumentNullException.ThrowIfNull(environments);
        lock (_gate) _environments = [.. environments];
        return null;
    }

    /// <summary>Replaces the list as another writer would, and says so.</summary>
    public void Replace(IReadOnlyList<StoredEnvironment> environments)
    {
        Save(environments);
        Changed?.Invoke();
    }
}

/// <summary>The command runner of a host that cannot start a process: every run is
/// refused with the reason.</summary>
internal sealed class NoEnvironmentCommandRunner : IEnvironmentCommandRunner
{
    public const string Unsupported = "Running a command isn't available here.";

    public string? Run(string commandLine, string? workingDirectory) => Unsupported;
}
