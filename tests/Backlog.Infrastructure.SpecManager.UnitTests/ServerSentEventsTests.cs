using System.Text;

using Backlog.Infrastructure.SpecManager.Questions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>The notification stream's wire format, read for the event names alone.</summary>
public sealed class ServerSentEventsTests
{
    [Fact]
    public async Task Events_are_read_by_name_past_the_retry_line_and_the_heartbeats()
    {
        await using var events = Read("retry: 5000\n\n: ping\n\nevent: changed\ndata: {}\n\n: ping\n\nevent: changed\ndata: {}\n\n");

        Assert.Equal("changed", await events.ReadEventAsync(TestContext.Current.CancellationToken));
        Assert.Equal("changed", await events.ReadEventAsync(TestContext.Current.CancellationToken));
        Assert.Null(await events.ReadEventAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_event_with_data_and_no_name_is_a_message()
    {
        await using var events = Read("data: hello\r\n\r\n");

        Assert.Equal(ServerSentEvents.DefaultEvent, await events.ReadEventAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_stream_that_ends_mid_event_dispatches_nothing()
    {
        await using var events = Read("event: changed\ndata: {}");

        Assert.Null(await events.ReadEventAsync(TestContext.Current.CancellationToken));
    }

    private static ServerSentEvents Read(string wire) => new(null, new MemoryStream(Encoding.UTF8.GetBytes(wire)));
}
