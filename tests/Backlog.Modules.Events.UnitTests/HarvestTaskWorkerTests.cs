using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Extensions;
using Backlog.Modules.Events.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Desktop clock (<c>.devbook/domain/events/context.md#desktop-clock</c>): the worker the
/// desktop heads resolve at start checks for due events shortly after the app starts and again
/// each time the local day turns, through <c>AddEventsModule()</c> over a real container. The
/// clock is pinned to Tuesday 10 November 2026, 09:00 in Amsterdam (UTC+1), so local midnight
/// is 23:00 UTC.
/// </summary>
public sealed class HarvestTaskWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryKnowledgeEvents _store = new();
    private readonly FakeEventTaskTarget _tasks = new();
    private ServiceProvider? _provider;

    public HarvestTaskWorkerTests() =>
        _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("CET-test", TimeSpan.FromHours(1), "CET", "CET"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _provider?.Dispose();

    [Fact]
    public async Task A_due_event_is_harvested_shortly_after_the_app_starts()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 9));
        var worker = Start(new FakeEventsFeature(on: true));

        await Advance(worker, HarvestTaskWorker.StartDelay + TimeSpan.FromSeconds(1));

        Assert.Equal(["Harvest Techorama Belgium"], _tasks.Filed);
    }

    [Fact]
    public async Task An_event_ending_today_is_harvested_once_the_local_day_turns()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 10));
        var worker = Start(new FakeEventsFeature(on: true));
        await Advance(worker, HarvestTaskWorker.StartDelay + TimeSpan.FromSeconds(1));
        Assert.Empty(_tasks.Filed);

        // To 00:00:05 on Wednesday in Amsterdam: still 10 November in UTC.
        await Advance(worker, new DateTimeOffset(2026, 11, 11, 0, 0, 5, TimeSpan.FromHours(1)) - _clock.GetUtcNow());

        Assert.Equal(["Harvest Techorama Belgium"], _tasks.Filed);
    }

    [Fact]
    public async Task With_the_events_area_off_nothing_is_filed()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 9));
        var worker = Start(new FakeEventsFeature(on: false));

        await Advance(worker, TimeSpan.FromDays(2));

        Assert.Empty(_tasks.Filed);
    }

    [Fact]
    public async Task With_no_feature_settings_nothing_is_filed()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 9));
        var worker = Start(features: null);

        await Advance(worker, TimeSpan.FromDays(2));

        Assert.Empty(_tasks.Filed);
    }

    [Fact]
    public async Task A_check_that_throws_is_tried_again_the_next_day()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 9));
        _store.FailNextList = true;
        var worker = Start(new FakeEventsFeature(on: true));
        await Advance(worker, HarvestTaskWorker.StartDelay + TimeSpan.FromSeconds(1));
        Assert.Empty(_tasks.Filed);

        await Advance(worker, TimeSpan.FromDays(1));

        Assert.Equal(["Harvest Techorama Belgium"], _tasks.Filed);
    }

    [Fact]
    public async Task A_disposed_worker_files_nothing()
    {
        await Going("Techorama Belgium", lastDay: new(2026, 11, 9));
        var worker = Start(new FakeEventsFeature(on: true));

        worker.Dispose();
        await Advance(worker, TimeSpan.FromDays(1));

        Assert.Empty(_tasks.Filed);
    }

    /// <summary>The worker as a head resolves it: the module over the host's store, task target,
    /// clock and — unless <paramref name="features"/> is null — feature settings.</summary>
    private HarvestTaskWorker Start(IAppFeatureSettings? features)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IKnowledgeEventRepository>(_store);
        services.AddScoped<IEventTaskTarget>(_ => _tasks);
        if (features is not null) services.AddSingleton(features);

        _provider = services.AddEventsModule().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return _provider.GetRequiredService<HarvestTaskWorker>();
    }

    /// <summary>Moves the clock on, then waits for the check it set off, if any.</summary>
    private async Task Advance(HarvestTaskWorker worker, TimeSpan by)
    {
        _clock.Advance(by);
        await worker.LastCheck.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    private async Task Going(string title, DateOnly lastDay)
    {
        var e = KnowledgeEvent.Create(Guid.NewGuid(), title).Value;
        Assert.True(e.SetDates(EventDate.Create(lastDay.AddDays(-1), null, null).Value, EventDate.Create(lastDay, null, null).Value, DateSource.Typed).IsSuccess);
        Assert.True(e.MoveTo(KnowledgeEventState.Going).IsSuccess);
        await _store.SaveAsync(e, Token);
    }
}
