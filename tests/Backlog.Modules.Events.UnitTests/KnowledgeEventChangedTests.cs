using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <see cref="IKnowledgeEventReplication.Changed"/>, what the sync loop pushes on: raised after
/// every save on this machine — the pane, the To Event route, an applied attachment request —
/// and never for a copy taken from another device, so an edit reaches the phone in seconds and
/// a pulled copy is not sent straight back.
/// </summary>
public sealed class KnowledgeEventChangedTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private int _raised;

    public KnowledgeEventChangedTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventRepository, InMemoryKnowledgeEvents>();
        services.AddSingleton<IEventSeriesRepository, InMemoryEventSeries>();
        services.AddSingleton<IKnowledgeEventReplicaStore, InMemoryKnowledgeEventReplicaStore>();
        services.AddSingleton<IEventSeriesReplicaStore, InMemoryEventSeriesReplicaStore>();
        services.AddScoped<IEventTaskTarget, FakeEventTaskTarget>();
        services.AddSingleton<IEventBrainNotes, FakeEventBrainNotes>();

        _provider = services.AddEventsModule().BuildServiceProvider();
        _scope = _provider.CreateScope();
        _provider.GetRequiredService<IKnowledgeEventReplication>().Changed += () => Interlocked.Increment(ref _raised);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IEventsPane Pane => _scope.ServiceProvider.GetRequiredService<IEventsPane>();

    private int Raised => Volatile.Read(ref _raised);

    [Fact]
    public async Task Every_save_from_the_pane_is_announced()
    {
        var added = await Pane.AddAsync("Techorama 2026", cancellationToken: Cancellation);
        Assert.Equal(1, Raised);

        await Pane.RenameAsync(added.Value.Id, "Techorama Netherlands 2026", Cancellation);
        Assert.Equal(2, Raised);

        await Pane.StartSeriesAsync("Techorama", cancellationToken: Cancellation);
        Assert.Equal(3, Raised);
    }

    [Fact]
    public async Task A_change_the_event_refuses_is_not_announced()
    {
        var added = await Pane.AddAsync("Techorama 2026", cancellationToken: Cancellation);

        var refused = await Pane.RenameAsync(added.Value.Id, "  ", Cancellation);

        Assert.True(refused.IsFailure);
        Assert.Equal(1, Raised);
    }

    /// <summary>A removal travels as the event's tombstone, so it is pushed as an edit is; a
    /// refused one writes nothing and announces nothing.</summary>
    [Fact]
    public async Task A_removal_is_announced_and_a_refused_one_is_not()
    {
        var added = await Pane.AddAsync("Techorama 2026", cancellationToken: Cancellation);

        Assert.True((await Pane.RemoveAsync(added.Value.Id, Cancellation)).IsSuccess);
        Assert.Equal(2, Raised);

        Assert.True((await Pane.RemoveAsync(added.Value.Id, Cancellation)).IsFailure);
        Assert.Equal(2, Raised);
    }

    [Fact]
    public async Task An_event_spotted_by_the_to_event_route_is_announced()
    {
        await _provider.GetRequiredService<IKnowledgeEvents>().SpotAsync(
            new SpotKnowledgeEventDto("Techorama 2026", "https://techorama.nl/2026", null, null, null, null, null), Cancellation);

        Assert.Equal(1, Raised);
    }

    /// <summary>The desktop's answer to the phone travels on the event feed, so it is pushed as
    /// an edit is; a request already answered writes nothing and announces nothing.</summary>
    [Fact]
    public async Task An_applied_attachment_request_is_announced_and_one_already_attached_is_not()
    {
        var added = await Pane.AddAsync("Techorama 2026", cancellationToken: Cancellation);
        var requests = _provider.GetRequiredService<IEventAttachmentRequests>();
        var request = new EventAttachmentRequestDto(Guid.NewGuid(), added.Value.Id, Guid.NewGuid(), "Keynote");

        Assert.Equal(EventAttachmentOutcome.Attached, await requests.ApplyAsync(request, Cancellation));
        Assert.Equal(2, Raised);

        Assert.Equal(EventAttachmentOutcome.AlreadyAttached, await requests.ApplyAsync(request, Cancellation));
        Assert.Equal(2, Raised);
    }

    [Fact]
    public async Task A_copy_taken_from_another_device_is_not_announced()
    {
        var replication = _provider.GetRequiredService<IKnowledgeEventReplication>();
        var copy = new KnowledgeEventReplicaDto(
            Guid.NewGuid(), "Techorama 2026", KnowledgeEventState.Spotted, null, null, null, null, false, null, [], null, null, [], null, null, null, [], null, Noon);

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await replication.ApplyEventAsync(copy, Cancellation));
        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await replication.ApplySeriesAsync(
            new EventSeriesReplicaDto(Guid.NewGuid(), "Techorama", null, null, Noon), Cancellation));

        Assert.Equal(0, Raised);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
