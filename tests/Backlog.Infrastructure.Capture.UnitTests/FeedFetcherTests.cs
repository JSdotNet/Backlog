using System.Net;

using Backlog.Infrastructure.Capture.Feeds;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The one HTTP path every adapter reads through, and the bound it puts on a
/// server that answers slowly rather than not at all.
/// </summary>
public sealed class FeedFetcherTests
{
    /// <summary>The client's timeout stops counting once the headers are in
    /// — the body is read with <c>ResponseHeadersRead</c> — so a server that
    /// sends its headers and then nothing would otherwise hold the pane for
    /// as long as it liked. The body read gets the same bound the headers
    /// got, and running out of it reads as the timeout it is.</summary>
    [Fact]
    public async Task A_body_that_never_finishes_ends_with_the_timeout_not_a_hang()
    {
        var wire = new StubHttpMessageHandler().Map(
            "https://example.org/feed.xml",
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverEndingStream()) });
        var fetcher = new FeedFetcher(new StubHttpClientFactory(wire, TimeSpan.FromMilliseconds(200)));

        // The caller's token is not what ends the read: the run's own is
        // never cancelled here, the way the shell's is not when it presses
        // the button.
        var fetch = fetcher.GetAsync(new Uri("https://example.org/feed.xml"), TestContext.Current.CancellationToken);

        var finished = await Task.WhenAny(fetch, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(fetch, finished);
        var timeout = await Assert.ThrowsAsync<TimeoutException>(() => fetch);
        Assert.Contains("no answer within", timeout.Message, StringComparison.Ordinal);
    }

    /// <summary>A body that only ever completes when the read is cancelled,
    /// like a connection the server has stopped writing to and not closed.</summary>
    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
