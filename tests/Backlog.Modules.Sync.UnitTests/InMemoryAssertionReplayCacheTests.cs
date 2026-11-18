using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>An assertion id is spent for one device until the assertion could no
/// longer be taken anyway, and no longer than that.</summary>
public class InMemoryAssertionReplayCacheTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly DeviceId _device = DeviceId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_id_is_taken_once_until_it_lapses()
    {
        var cache = new InMemoryAssertionReplayCache(_clock);
        var until = _clock.GetUtcNow().AddMinutes(1);

        Assert.True(await cache.TryAdd(_device, "jti-1", until, Cancellation));
        Assert.False(await cache.TryAdd(_device, "jti-1", until, Cancellation));

        _clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        Assert.True(await cache.TryAdd(_device, "jti-1", _clock.GetUtcNow().AddMinutes(1), Cancellation));
    }

    [Fact]
    public async Task The_same_id_from_another_device_is_its_own()
    {
        var cache = new InMemoryAssertionReplayCache(_clock);
        var until = _clock.GetUtcNow().AddMinutes(1);

        Assert.True(await cache.TryAdd(_device, "jti-1", until, Cancellation));
        Assert.True(await cache.TryAdd(DeviceId.New(), "jti-1", until, Cancellation));
    }
}
