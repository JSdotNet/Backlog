using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The composite source the unkeyed registration holds, standing in as one
/// catalog.
/// </summary>
internal sealed class FakeAgentSessionSource(AgentSessionCatalog catalog) : IAgentSessionSource
{
    /// <summary>Which of the two reads the tool made. The horizon read answers a
    /// different question, and a tool that called it would be answering one.</summary>
    public int NewestReads { get; private set; }

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NewestReads++;

        return Task.FromResult(catalog);
    }

    public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(catalog);
    }
}

/// <summary>
/// The delivery surface, recording what it was asked and answering plausibly.
/// <para>
/// It refuses nothing. The refusals that matter — a stage index outside the
/// run's list, a status the vocabulary does not have, a write to a run nobody
/// started — belong to <c>LocalDeliverySurfaceLifecycle</c> and are pinned by
/// <c>DeliverySurfaceLifecycleTests</c> against the real adapter and its files. A
/// double that re-implemented them here would be a second statement of the same
/// rules, and the tools this stands in for pass their arguments through rather
/// than deciding anything.
/// </para>
/// </summary>
internal sealed class FakeDeliverySurfaceLifecycle(
    DeliverySurfaceActivation activation = DeliverySurfaceActivation.Shown,
    IReadOnlyList<DeliveryRun>? runs = null,
    DeliveryRun? run = null) : IDeliverySurfaceLifecycle
{
    /// <summary>What the last call was given, so a test can assert the tool
    /// passed its arguments on rather than interpreting them.</summary>
    public List<string> Calls { get; } = [];

    public string? Worktree { get; private set; }

    public string? RunId { get; private set; }

    public IReadOnlyList<string>? Stages { get; private set; }

    public string? ChangeKind { get; private set; }

    public string? SessionId { get; private set; }

    public int? StageIndex { get; private set; }

    public string? Status { get; private set; }

    public IReadOnlyList<DeliveryStageLink>? Links { get; private set; }

    public IReadOnlyList<DeliveryScenario>? Scenarios { get; private set; }

    public DeliveryMonitoring? Monitoring { get; private set; }

    public Task<DeliverySurfaceOpened> OpenDashboardAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.OpenDashboard);

        return Task.FromResult(new DeliverySurfaceOpened(activation, "The Sessions pane is showing."));
    }

    public Task<DeliveryRunStarted> StartRunAsync(
        string worktree,
        string skillId,
        string title,
        IReadOnlyList<string> stages,
        string? changeKind = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.StartRun);
        Worktree = worktree;
        Stages = stages;
        ChangeKind = changeKind;
        SessionId = sessionId;

        return Task.FromResult(new DeliveryRunStarted("run-1", Resumed: true, SessionTitle: title));
    }

    public Task RecordPromptAsync(
        string worktree,
        string runId,
        string prompt,
        string? kind = null,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.RecordPrompt);
        Worktree = worktree;
        RunId = runId;

        return Task.CompletedTask;
    }

    public Task SetRunContextAsync(
        string worktree,
        string runId,
        string? changeKind = null,
        string? approval = null,
        string? approvalNote = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.SetRunContext);
        Worktree = worktree;
        RunId = runId;
        ChangeKind = changeKind;

        return Task.CompletedTask;
    }

    public Task<DeliveryStageUpdated> UpdateStageAsync(
        string worktree,
        string runId,
        int stageIndex,
        string status,
        string? output = null,
        IReadOnlyList<DeliveryStageLink>? links = null,
        IReadOnlyList<DeliveryScenario>? scenarios = null,
        DeliveryMonitoring? monitoring = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.UpdateStage);
        Worktree = worktree;
        RunId = runId;
        StageIndex = stageIndex;
        Status = status;
        Links = links;
        Scenarios = scenarios;
        Monitoring = monitoring;

        return Task.FromResult(new DeliveryStageUpdated(runId, stageIndex, status, DoneCount: 2, SessionTitle: null));
    }

    public Task FinishRunAsync(
        string worktree,
        string runId,
        string status,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.FinishRun);
        Worktree = worktree;
        RunId = runId;
        Status = status;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeliveryRun>> ListRunsAsync(string worktree, CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.ListRuns);
        Worktree = worktree;

        return Task.FromResult(runs ?? []);
    }

    public Task<DeliveryRun?> GetRunAsync(string worktree, string runId, CancellationToken cancellationToken = default)
    {
        Calls.Add(DeliverySurfaceOperations.GetRun);
        Worktree = worktree;
        RunId = runId;

        return Task.FromResult(run);
    }
}

/// <summary>A run with the fields the surface tests are about and defaults for
/// the rest.</summary>
internal static class Runs
{
    internal static DeliveryRun Run(
        string id = "run-1",
        string worktree = @"D:\Repos\Backlog",
        string status = "in_progress",
        IReadOnlyList<DeliveryRunStage>? stages = null,
        IReadOnlyList<string>? sessionIds = null) =>
        new(
            id,
            Dashboard: "backlog",
            worktree,
            WorktreeName: "Backlog",
            EnvironmentId: "this-machine",
            Environment: "This machine",
            SkillId: "flow-code",
            Title: "Expose the delivery surface",
            status,
            ChangeKind: "new-functionality",
            References: [],
            StartedAt: new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero),
            stages ?? [new DeliveryRunStage("Scope Discovery", "done", 1200, 1)],
            TokenUsage: null,
            Context: null,
            InsightsByCategory: [],
            InsightsByServer: [])
        {
            SessionIds = sessionIds ?? []
        };
}

/// <summary>Sessions with the fields these tests are about and defaults for the
/// rest.</summary>
internal static class Sessions
{
    internal static AgentSession Session(
        string id,
        string? repository = null,
        string? resolvedRepository = null,
        AgentSessionKind kind = AgentSessionKind.Claude,
        AgentSessionState state = AgentSessionState.Running) =>
        new(
            id,
            kind,
            EnvironmentId: "this-machine",
            Environment: "This machine",
            Title: $"Session {id}",
            WorkingFolder: @"D:\Repos\Backlog",
            repository,
            Branch: "main",
            StartedAt: new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero),
            LastActivityAt: new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero),
            state,
            TurnCount: 12,
            AgentSessionOrigin.Local)
        {
            ResolvedRepository = resolvedRepository
        };
}

/// <summary>
/// The feature switches, as a set of keys that are on.
/// <para>
/// It throws for a key it has never heard of, the way the real store does: an
/// unknown feature is a typo in code, not a choice somebody made, and a double
/// that answered <c>false</c> would turn a misspelled key into a silently hidden
/// tool.
/// </para>
/// </summary>
internal sealed class FakeAppFeatureSettings(params string[] known) : IAppFeatureSettings
{
    private readonly HashSet<string> _known = new(known, StringComparer.OrdinalIgnoreCase);

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public AppFeatureSettings Current { get; } = new();

    public string SettingsPath => "in-memory";

    public HashSet<string> Enabled { get; } = new(known, StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string key) => _known.Contains(key)
        ? Enabled.Contains(key)
        : throw new ArgumentOutOfRangeException(nameof(key), key, "No catalog defines this feature.");

    public string? SetEnabled(string key, bool enabled)
    {
        if (enabled) Enabled.Add(key); else Enabled.Remove(key);

        return null;
    }
}
