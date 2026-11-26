using Backlog.Infrastructure.FileSystem.DevPc;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Dev PCs' machines (local ADR 0073 §7): this device, and the machines the session
/// records that arrived name. The failure this is about is a filter that can only ever
/// empty the surface, so a machine is known exactly when there are records behind it.
/// </summary>
public sealed class SessionMachineRegistryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisDevice = new("6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71");

    private const string Laptop = "0f4c2a9e-0000-4000-8000-000000000001";

    private const string Nuc = "a71d33b0-0000-4000-8000-000000000002";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_current_machine_is_built_over_the_device_identity()
    {
        var current = Registry().Current;

        Assert.Equal(new MachineId(ThisDevice), current.Id);
        Assert.Equal("DEV-TOWER", current.Name);
        Assert.Equal(ThisDevice.ToString("D"), current.Id.ToString());
    }

    [Fact]
    public async Task This_device_is_on_the_list_when_nothing_has_replicated()
    {
        var machine = Assert.Single(await Registry().GetMachinesAsync(Noon.AddDays(-84), Ct));

        Assert.Equal(new MachineId(ThisDevice), machine.Id);
    }

    [Fact]
    public async Task The_machines_the_records_came_from_follow_this_device_by_name()
    {
        var machines = await Registry(Session(Nuc, "DEV-NUC"), Session(Laptop, "DEV-LAPTOP"))
            .GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP", "DEV-NUC"], machines.Select(machine => machine.Name));
    }

    [Fact]
    public async Task One_machine_is_one_entry_however_many_records_it_sent_and_this_device_is_not_listed_twice()
    {
        var machines = await Registry(
                Session(Laptop, "DEV-LAPTOP"),
                Session(Laptop, "DEV-LAPTOP"),
                Session(ThisDevice.ToString(), "DEV-TOWER-RENAMED"))
            .GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP"], machines.Select(machine => machine.Name));
    }

    [Fact]
    public async Task Two_machines_sharing_a_name_stay_two()
    {
        var machines = await Registry(Session(Laptop, "DEV-LAPTOP"), Session(Nuc, "DEV-LAPTOP"))
            .GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Equal(3, machines.Count);
        Assert.Equal([Laptop, Nuc], machines.Skip(1).Select(machine => machine.Id.ToString()));
    }

    [Fact]
    public async Task A_record_whose_environment_is_no_device_identity_names_no_machine()
    {
        var machines = await Registry(Session("tower", "DEV-OLD"), Session("", "DEV-NONE"))
            .GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Single(machines);
    }

    [Fact]
    public async Task A_record_without_a_name_is_listed_by_its_id()
    {
        var machines = await Registry(Session(Laptop, "")).GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Equal(Laptop, machines[1].Name);
    }

    [Fact]
    public async Task The_records_are_read_back_to_the_horizon_asked_for()
    {
        var source = new StubSessionSource();
        var registry = new SessionMachineRegistry(Identity, source);

        _ = await registry.GetMachinesAsync(Noon.AddDays(-84), Ct);

        Assert.Equal(Noon.AddDays(-84), Assert.Single(source.Queries).Horizon);
    }

    [Fact]
    public async Task A_source_that_cannot_answer_still_leaves_this_device()
    {
        var registry = new SessionMachineRegistry(Identity, new ThrowingSessionSource());

        var machine = Assert.Single(await registry.GetMachinesAsync(Noon.AddDays(-84), Ct));

        Assert.Equal(new MachineId(ThisDevice), machine.Id);
    }

    [Fact]
    public async Task A_host_with_no_session_source_knows_this_device()
    {
        var registry = new SessionMachineRegistry(Identity, null);

        Assert.Single(await registry.GetMachinesAsync(Noon.AddDays(-84), Ct));
    }

    [Fact]
    public async Task Cancellation_travels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SessionMachineRegistry(Identity, new CancellableSessionSource()).GetMachinesAsync(Noon.AddDays(-84), cancelled.Token));
    }

    private static readonly IDeviceIdentitySource Identity = new StubDeviceIdentity(new DeviceIdentity(ThisDevice, "DEV-TOWER"));

    private static SessionMachineRegistry Registry(params AgentSession[] sessions) =>
        new(Identity, new StubSessionSource(sessions));

    private static AgentSession Session(string environmentId, string environment) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            Kind: AgentSessionKind.Claude,
            EnvironmentId: environmentId,
            Environment: environment,
            Title: "session",
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddHours(-1),
            State: AgentSessionState.Finished,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);

    // Differs from StubSessionSource: it honours the token, which the shared one ignores.
    private sealed class CancellableSessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AgentSessionCatalog([], [], 0));
        }
    }

    // Differs from StubSessionSource: every read throws, as a store that cannot be read does.
    private sealed class ThrowingSessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("The replicated session store could not be read.");

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            throw new IOException("The replicated session store could not be read.");
    }
}
