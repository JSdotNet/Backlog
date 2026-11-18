using System.Text.Json.Nodes;
using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The signal an open Sessions pane re-reads its runs on, and the lifecycle that raises
/// it (<c>.devbook/domain/sessions/features.md#re-read-on-request</c>).
/// <para>
/// The property is one raise per write that reached a run file, and none for anything
/// else. A read that raised would have the pane re-read on its own re-read's behalf; a
/// refused call that raised would announce a change nobody can find; and a write that
/// raised twice would cost the pane a second read for nothing.
/// </para>
/// </summary>
public sealed class DeliveryRunChangeSignalTests : IDisposable
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\change-signal-5c1e2a";

    private static readonly string[] Stages = ["Scope Discovery", "Implementation", "Summary"];

    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-delivery-run-signal-tests",
        Guid.NewGuid().ToString("n"));

    private readonly DeliveryRunChangeSignal _signal = new();

    private int _raised;

    public DeliveryRunChangeSignalTests() => _signal.Changed += () => _raised++;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void A_raise_reaches_every_listener()
    {
        var signal = new DeliveryRunChangeSignal();
        var first = 0;
        var second = 0;
        signal.Changed += () => first++;
        signal.Changed += () => second++;

        signal.Raise();

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void A_raise_with_nobody_listening_is_harmless()
    {
        var signal = new DeliveryRunChangeSignal();

        signal.Raise();
    }

    [Fact]
    public async Task Starting_a_new_run_raises_once()
    {
        await Surface().StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: Cancellation);

        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task A_session_resuming_the_run_raises_once_for_the_session_it_adds()
    {
        var surface = Surface();
        await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-a", cancellationToken: Cancellation);
        _raised = 0;

        var resumed = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-b", cancellationToken: Cancellation);

        Assert.True(resumed.Resumed);
        Assert.Equal(1, _raised);
    }

    /// <summary>A resume that adds nothing writes nothing, so it has nothing to say: the
    /// pane would re-read the same run.</summary>
    [Fact]
    public async Task A_resume_that_changes_nothing_raises_nothing()
    {
        var surface = Surface();
        await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-a", cancellationToken: Cancellation);
        _raised = 0;

        var resumed = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-a", cancellationToken: Cancellation);

        Assert.True(resumed.Resumed);
        Assert.Equal(0, _raised);
    }

    [Fact]
    public async Task Recording_a_prompt_raises_once()
    {
        var (surface, run) = await Started();

        await surface.RecordPromptAsync(Worktree, run, "Make the pane live.", cancellationToken: Cancellation);

        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task Setting_the_run_context_raises_once()
    {
        var (surface, run) = await Started();

        await surface.SetRunContextAsync(Worktree, run, changeKind: "feature", runContext: new JsonObject { ["tier"] = "standard" }, cancellationToken: Cancellation);

        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task Updating_a_stage_raises_once()
    {
        var (surface, run) = await Started();

        await surface.UpdateStageAsync(Worktree, run, 0, "in_progress", "Reading the scope.", cancellationToken: Cancellation);

        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task Finishing_the_run_raises_once()
    {
        var (surface, run) = await Started();

        await surface.FinishRunAsync(Worktree, run, "done", "Shipped.", cancellationToken: Cancellation);

        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task Reading_and_opening_the_dashboard_raise_nothing()
    {
        var (surface, run) = await Started();

        await surface.OpenDashboardAsync(Cancellation);
        await surface.ListRunsAsync(Worktree, Cancellation);
        await surface.GetRunAsync(Worktree, run, Cancellation);

        Assert.Equal(0, _raised);
    }

    /// <summary>Every write that is refused before it reaches the file — an unknown run, a
    /// blank id, a status or stage the contract does not have — raises nothing.</summary>
    [Fact]
    public async Task A_refused_write_raises_nothing()
    {
        var (surface, run) = await Started();

        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.UpdateStageAsync(Worktree, "run-unknown", 0, "in_progress", cancellationToken: Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.RecordPromptAsync(Worktree, "run-unknown", "Hello.", cancellationToken: Cancellation));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => surface.SetRunContextAsync(Worktree, " ", changeKind: "feature", cancellationToken: Cancellation));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => surface.FinishRunAsync(Worktree, run, "in_progress", cancellationToken: Cancellation));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => surface.UpdateStageAsync(Worktree, run, 99, "in_progress", cancellationToken: Cancellation));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => surface.StartRunAsync(Worktree, " ", "Run", Stages, cancellationToken: Cancellation));

        Assert.Equal(0, _raised);
    }

    [Fact]
    public async Task A_lifecycle_composed_without_the_signal_still_writes()
    {
        var surface = new LocalDeliverySurfaceLifecycle(_home, "machine", "Workshop PC");

        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, started.RunId, 0, "done", cancellationToken: Cancellation);

        Assert.Single(await surface.ListRunsAsync(Worktree, Cancellation));
        Assert.Equal(0, _raised);
    }

    /// <summary>
    /// One signal for the process, and the lifecycle holds it: the MCP listener writes
    /// through the lifecycle on a request thread, and every window's pane listens on what
    /// the container hands it. Two instances would be a pane that never hears a run.
    /// </summary>
    [Fact]
    public void The_registration_composes_one_signal_and_hands_it_to_the_lifecycle()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(new StubDeviceIdentity());
        services.AddAgentSessionSource();

        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var signal = provider.GetRequiredService<IDeliveryRunChangeSignal>();
        Assert.IsType<DeliveryRunChangeSignal>(signal);
        Assert.Same(signal, first.ServiceProvider.GetRequiredService<IDeliveryRunChangeSignal>());
        Assert.Same(signal, second.ServiceProvider.GetRequiredService<IDeliveryRunChangeSignal>());

        var lifecycle = Assert.IsType<LocalDeliverySurfaceLifecycle>(provider.GetRequiredService<IDeliverySurfaceLifecycle>());
        Assert.Same(signal, lifecycle.Changes);
    }

    /// <summary>A host that composed its own signal first keeps it — the registration
    /// adds one only where none is.</summary>
    [Fact]
    public void A_signal_the_host_composed_first_is_the_one_the_lifecycle_raises()
    {
        var mine = new DeliveryRunChangeSignal();
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentitySource>(new StubDeviceIdentity());
        services.AddSingleton<IDeliveryRunChangeSignal>(mine);
        services.AddAgentSessionSource();

        using var provider = services.BuildServiceProvider();

        Assert.Same(mine, provider.GetRequiredService<IDeliveryRunChangeSignal>());
        Assert.Same(mine, Assert.IsType<LocalDeliverySurfaceLifecycle>(provider.GetRequiredService<IDeliverySurfaceLifecycle>()).Changes);
    }

    private LocalDeliverySurfaceLifecycle Surface() =>
        new(_home, "machine", "Workshop PC", changes: _signal);

    private async Task<(LocalDeliverySurfaceLifecycle Surface, string RunId)> Started()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: Cancellation);
        _raised = 0;

        return (surface, started.RunId);
    }

    private sealed class StubDeviceIdentity : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = new(Guid.Empty, "Workshop PC");
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }
}
