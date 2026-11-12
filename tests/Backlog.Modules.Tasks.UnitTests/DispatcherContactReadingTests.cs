using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The session queue's warning (local ADR 0038 §4): it is raised only while a request
/// waits Requested and no dispatcher has been heard for longer than the silence
/// threshold. A request already Claimed or Started has a dispatcher, so it warns of none.
/// </summary>
public sealed class DispatcherContactReadingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static EntrySessionRequest Open(EntrySessionRequestState state) =>
        new(Guid.NewGuid(), state, "JSdotNet/Backlog", Noon);

    [Theory]
    [InlineData(true, EntrySessionRequestState.Requested, true)]
    [InlineData(false, EntrySessionRequestState.Requested, false)]
    [InlineData(true, EntrySessionRequestState.Claimed, false)]
    [InlineData(true, EntrySessionRequestState.Started, false)]
    public void A_silent_dispatcher_is_warned_of_only_while_a_request_waits_Requested(
        bool silent,
        EntrySessionRequestState state,
        bool warns)
    {
        var reading = new DispatcherContactReading(null, silent);

        Assert.Equal(warns, reading.WarnsFor([Open(state)]));
    }

    [Fact]
    public void An_empty_queue_warns_of_nothing_however_long_the_silence()
    {
        Assert.False(new DispatcherContactReading(Noon, IsSilent: true).WarnsFor([]));
    }

    [Fact]
    public void One_Requested_request_among_started_ones_is_enough_to_warn()
    {
        var reading = new DispatcherContactReading(Noon, IsSilent: true);

        Assert.True(reading.WarnsFor([Open(EntrySessionRequestState.Started), Open(EntrySessionRequestState.Requested)]));
    }
}
