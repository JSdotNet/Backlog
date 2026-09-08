using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Adapters;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The machines the dashboard's filter can offer, now that records arrive from more
/// than one of them.
/// <para>
/// The failure this file is about is a filter that can only ever empty the surface.
/// Every option it offers has to be a machine the figures behind the surface can
/// actually account for, which is why the list is the union of this device and the
/// machines named by the records that arrived — never a machine somebody registered,
/// and never a name this adapter made up.
/// </para>
/// </summary>
public sealed class DeviceMachineDirectoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisDevice = new("6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71");

    /// <summary>
    /// Nothing has replicated, and the honest list is one row. It is also the list a
    /// machine that never turns sync on will always have, so it is not a degenerate
    /// case to be tolerated — it is the common one.
    /// </summary>
    [Fact]
    public async Task This_device_is_on_the_list_when_nothing_has_replicated()
    {
        var directory = Directory();

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        var machine = Assert.Single(machines);
        Assert.Equal(ThisDevice.ToString(), machine.Id);
        Assert.Equal("DEV-TOWER", machine.Name);
    }

    /// <summary>
    /// A machine this device has records from is a machine the filter can offer,
    /// because the figures behind the filter already count those records.
    /// </summary>
    [Fact]
    public async Task The_machines_the_records_came_from_are_on_the_list()
    {
        var directory = Directory(Session("laptop", "DEV-LAPTOP"), Session("nuc", "DEV-NUC"));

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        // This device first — it is the one the reader is sitting at — and the rest
        // by name, so the options do not reorder themselves as sessions come and go.
        Assert.Equal(
            ["DEV-TOWER", "DEV-LAPTOP", "DEV-NUC"],
            machines.Select(machine => machine.Name));

        Assert.Equal(
            [ThisDevice.ToString(), "laptop", "nuc"],
            machines.Select(machine => machine.Id));
    }

    /// <summary>
    /// The union is on the id, so fifty sessions from one machine are one option.
    /// </summary>
    [Fact]
    public async Task A_machine_that_sent_many_records_is_one_option()
    {
        var directory = Directory(
            Session("laptop", "DEV-LAPTOP"),
            Session("laptop", "DEV-LAPTOP"),
            Session("laptop", "DEV-LAPTOP"));

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP"], machines.Select(machine => machine.Name));
    }

    /// <summary>
    /// On the id and not on the name, which is the whole reason a machine carries
    /// both: two machines can be called the same thing, and merging them would give
    /// the reader one option that filters to half of what it names.
    /// </summary>
    [Fact]
    public async Task Two_machines_sharing_a_name_are_two_options()
    {
        var directory = Directory(Session("laptop", "DEV-LAPTOP"), Session("other", "DEV-LAPTOP"));

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP", "DEV-LAPTOP"], machines.Select(machine => machine.Name));
        Assert.Equal([ThisDevice.ToString(), "laptop", "other"], machines.Select(machine => machine.Id));
    }

    /// <summary>
    /// The local device is on the list once, whatever the records say. Sessions this
    /// device read for itself carry its own id, so without the de-duplication the
    /// filter would offer this machine twice — and a record this device pushed and
    /// received back would do it a third time.
    /// </summary>
    [Fact]
    public async Task This_device_is_not_listed_twice_by_its_own_records()
    {
        var directory = Directory(
            Session(ThisDevice.ToString(), "DEV-TOWER"),
            Session(ThisDevice.ToString(), "DEV-TOWER-RENAMED"),
            Session("laptop", "DEV-LAPTOP"));

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP"], machines.Select(machine => machine.Name));
    }

    /// <summary>
    /// A read that could not answer leaves the filter narrower rather than empty. The
    /// failure is not swallowed — the sessions part asks the same source and reports
    /// it as unavailable with its reason — and a filter that lost its own machine
    /// would take a surface down over a source that only decides how far it widens.
    /// </summary>
    [Fact]
    public async Task A_source_that_cannot_answer_still_leaves_this_device_on_the_list()
    {
        var directory = new DeviceMachineDirectory(new StubIdentity(), new ThrowingSessionSource());

        var machines = await directory.GetMachinesAsync(TestContext.Current.CancellationToken);

        var machine = Assert.Single(machines);
        Assert.Equal(ThisDevice.ToString(), machine.Id);
    }

    /// <summary>Cancellation is the reader closing the dashboard, not a source
    /// failing, and it travels rather than being turned into a shorter list.</summary>
    [Fact]
    public async Task Cancellation_is_not_read_as_a_source_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var directory = Directory(Session("laptop", "DEV-LAPTOP"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => directory.GetMachinesAsync(cancelled.Token));
    }

    private static DeviceMachineDirectory Directory(params AssistantSession[] sessions) =>
        new(new StubIdentity(), new StubSessionSource(sessions));

    private static AssistantSession Session(string machineId, string machineName) =>
        new(machineId, machineName, "Claude", Noon.AddHours(-2), Noon);

    private sealed class StubIdentity : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = new(ThisDevice, "DEV-TOWER");
    }

    private sealed class StubSessionSource(IReadOnlyList<AssistantSession> sessions) : IAssistantSessionSource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightAvailability.Available);

        public Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AssistantSessionReport(sessions, [], false, 100));
        }
    }

    private sealed class ThrowingSessionSource : IAssistantSessionSource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightAvailability.Available);

        public Task<AssistantSessionReport> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("The replicated session store could not be read.");
    }
}
