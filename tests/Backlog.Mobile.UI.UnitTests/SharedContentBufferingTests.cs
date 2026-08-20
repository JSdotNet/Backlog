namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The buffering half of <see cref="ISharedContentReceiver"/>.
///
/// <para>An Android share launches the activity, so the intent is handled before
/// the <c>BlazorWebView</c> has built the Inbox component that wants it. A purely
/// push-based receiver would drop that first — and only — payload, which is
/// exactly the share the person just made.</para>
/// </summary>
public sealed class SharedContentBufferingTests
{
    [Fact]
    public void A_payload_that_arrives_before_anyone_is_listening_is_replayed_on_subscribe()
    {
        var receiver = new TestSharedContentReceiver();
        receiver.Share("https://youtu.be/abc123", "A title");

        var seen = new List<SharedContent>();
        using var subscription = receiver.Subscribe(seen.Add);

        Assert.Equal("A title https://youtu.be/abc123", Assert.Single(seen).Draft);
    }

    [Fact]
    public void A_replayed_payload_is_not_handed_out_a_second_time()
    {
        var receiver = new TestSharedContentReceiver();
        receiver.Share("https://example.test/one");

        using (receiver.Subscribe(_ => { }))
        {
        }

        var seen = new List<SharedContent>();
        using var second = receiver.Subscribe(seen.Add);

        Assert.Empty(seen);
    }

    [Fact]
    public void Only_the_most_recent_buffered_payload_survives()
    {
        var receiver = new TestSharedContentReceiver();
        receiver.Share("https://example.test/one");
        receiver.Share("https://example.test/two");

        var seen = new List<SharedContent>();
        using var subscription = receiver.Subscribe(seen.Add);

        Assert.Equal("https://example.test/two", Assert.Single(seen).Draft);
    }

    [Fact]
    public void A_payload_that_arrives_while_a_subscriber_is_listening_is_pushed_straight_to_it()
    {
        var receiver = new TestSharedContentReceiver();

        var seen = new List<SharedContent>();
        using var subscription = receiver.Subscribe(seen.Add);

        receiver.Share("https://example.test/one");

        Assert.Equal("https://example.test/one", Assert.Single(seen).Draft);
    }

    [Fact]
    public void An_unsubscribed_screen_hears_nothing_more()
    {
        var receiver = new TestSharedContentReceiver();

        var seen = new List<SharedContent>();
        receiver.Subscribe(seen.Add).Dispose();

        receiver.Share("https://example.test/one");

        Assert.Empty(seen);
    }

    [Fact]
    public void A_share_that_carries_nothing_is_never_handed_to_a_screen()
    {
        var receiver = new TestSharedContentReceiver();
        receiver.Share("  ", "  ");

        var seen = new List<SharedContent>();
        using var subscription = receiver.Subscribe(seen.Add);

        Assert.Empty(seen);
    }
}
