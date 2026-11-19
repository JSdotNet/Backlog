using Backlog.Modules.Sync.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The credential gate every sync worker shares: with the feature on but no device
/// credential stored, no cycle runs however long the clock goes on, and the first
/// cycle runs once one is stored. Written once here, run once per worker by each
/// worker's own test class, which supplies its fixture.
/// <para>
/// The Devbook subject and knowledge event workers assert their cycle's summary
/// beside the count, so they keep their own copy of this test.
/// </para>
/// </summary>
public abstract class CredentialGatedSyncWorkerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan WellPastSeveralCycles = TimeSpan.FromMinutes(30);

    /// <summary>As much of a worker's fixture as the gate reads.</summary>
    internal interface IGateFixture : IDisposable
    {
        FakeTimeProvider Clock { get; }

        InMemoryDeviceCredentialStore Credentials { get; }

        int SessionsResolved { get; }

        Task NextCycle();
    }

    /// <summary>The worker's fixture with its feature on and no credential stored.</summary>
    private protected abstract IGateFixture CreateUnpaired();

    /// <summary>The worker's own delay before its first cycle.</summary>
    private protected abstract TimeSpan FirstCycleDelay { get; }

    /// <summary>The credential a pairing would store.</summary>
    private protected abstract DeviceCredential PairedCredential { get; }

    [Fact]
    public async Task No_cycle_runs_until_a_device_credential_is_stored()
    {
        using var fixture = CreateUnpaired();

        fixture.Clock.Advance(WellPastSeveralCycles);

        Assert.Equal(0, fixture.SessionsResolved);

        var first = fixture.NextCycle();
        fixture.Credentials.Save(PairedCredential);
        fixture.Clock.Advance(FirstCycleDelay);
        await first.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.SessionsResolved);
    }
}
