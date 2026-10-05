using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>A connector that answers whatever the test put in <see cref="Items"/>,
/// and remembers every fetch it was asked for.</summary>
internal sealed class StubTaskConnector(string id = StubTaskConnector.Id) : ITaskConnector
{
    public const string Id = "stub";

    public List<SourceItem> Items { get; } = [];

    /// <summary>Thrown by the next fetch instead of answering, when set.</summary>
    public Exception? Failure { get; set; }

    public List<(string Target, DateTimeOffset? Since)> Fetches { get; } = [];

    /// <summary>Run inside the fetch, for a test that changes something while one
    /// is in flight.</summary>
    public Action? DuringFetch { get; set; }

    public TaskConnectorDescriptor Descriptor { get; } = new(id, "Stub", "stub", "--color-stub");

    public TaskConnectorCapabilities Capabilities { get; } = new(HasEffort: true);

    public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        Fetches.Add((target, since));
        DuringFetch?.Invoke();
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<SourceItem>>([.. Items]);
    }

    public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("me");
}

/// <summary>The connected targets, held in a list.</summary>
internal sealed class InMemoryConnectedTargets(params IEnumerable<ConnectedTarget> targets) : IConnectedTargets
{
    private readonly List<ConnectedTarget> _targets = [.. targets];

    public event Action? Changed;

    public IReadOnlyList<ConnectedTarget> List() => [.. _targets];

    public ConnectedTarget? Get(string connectorId, string target) =>
        _targets.FirstOrDefault(candidate => candidate.Is(connectorId, target));

    public string? Save(ConnectedTarget target)
    {
        var index = _targets.FindIndex(candidate => candidate.Is(target.ConnectorId, target.Target));
        if (index >= 0) _targets[index] = target;
        else _targets.Add(target);
        Changed?.Invoke();
        return null;
    }

    /// <summary>How many times <see cref="Update"/> was called.</summary>
    public int Updates { get; private set; }

    public string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change)
    {
        Updates++;
        var index = _targets.FindIndex(candidate => candidate.Is(connectorId, target));
        if (index < 0) return null;
        _targets[index] = change(_targets[index]);
        Changed?.Invoke();
        return null;
    }

    public string? Remove(string connectorId, string target)
    {
        if (_targets.RemoveAll(candidate => candidate.Is(connectorId, target)) > 0) Changed?.Invoke();
        return null;
    }
}

/// <summary>A clock the test sets, in UTC so "today" is the UTC date, whose timers
/// are recorded and never fire.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public List<(TimeSpan DueTime, TimeSpan Period)> Timers { get; } = [];

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Timers.Add((dueTime, period));
        return new InertTimer();
    }

    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>The sync handler as the worker sees it, recording what it was asked
/// to run and answering a failure for the targets the test names.</summary>
internal sealed class RecordingSyncHandler : ICommandHandler<SyncLinkedTasksCommand, Result<LinkedTaskSyncSummary>>
{
    public List<SyncLinkedTasksCommand> Commands { get; } = [];

    public HashSet<string> Failing { get; } = [];

    public Task<Result<LinkedTaskSyncSummary>> Handle(SyncLinkedTasksCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        Result<LinkedTaskSyncSummary> result = Failing.Contains(command.Target)
            ? Error.Unexpected("linked_tasks.fetch_failed", "down")
            : new LinkedTaskSyncSummary(0, 0, 0, 0, 0, 0);
        return Task.FromResult(result);
    }
}

/// <summary>A scope factory whose every scope answers the one handler, so the
/// worker can be driven without a container.</summary>
internal sealed class HandlerScopeFactory(object handler) : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public IServiceProvider ServiceProvider => this;

    public IServiceScope CreateScope() => this;

    public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(handler) ? handler : null;

    public void Dispose()
    {
    }
}
