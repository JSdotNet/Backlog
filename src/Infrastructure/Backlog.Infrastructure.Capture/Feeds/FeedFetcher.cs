using System.Net.Http.Headers;
using System.Text;

namespace Backlog.Infrastructure.Capture.Feeds;

/// <summary>
/// One fetched document: where it came from after redirects, what the server
/// said it was, and the bytes — capped, see <see cref="FeedFetcher"/>.
/// </summary>
internal sealed class FetchedDocument(Uri url, MediaTypeHeaderValue? mediaType, byte[] body)
{
    public Uri Url => url;

    /// <summary>Whether the server called it XML. A vote, not a verdict — see
    /// <see cref="FeedReader.LooksLikeFeed"/> for the other half.</summary>
    public bool IsXml =>
        mediaType?.MediaType is { } type
        && type.Contains("xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>The body as text, in the charset the server named or UTF-8
    /// when it named none, and without the byte-order mark some servers put
    /// in front — decoded, it is a character, and a character before the
    /// first tag is not what a sniff for the first tag expects. Good enough
    /// to search HTML for a tag; a feed is parsed from <see cref="OpenRead"/>
    /// instead so the XML declaration wins.</summary>
    public string Text
    {
        get
        {
            var encoding = Encoding.UTF8;

            if (mediaType?.CharSet is { } charset)
            {
                try
                {
                    encoding = Encoding.GetEncoding(charset.Trim('"'));
                }
                catch (ArgumentException)
                {
                    // A charset this runtime has no name for; UTF-8 is the
                    // better guess than nothing.
                }
            }

            return encoding.GetString(body).TrimStart('\uFEFF');
        }
    }

    public Stream OpenRead() => new MemoryStream(body, writable: false);
}

/// <summary>
/// The one way anything in this project reads from the web.
/// <para>
/// Every request goes through <see cref="IHttpClientFactory"/>'s
/// <see cref="CaptureHttpClients.Feeds"/> client, which is where the timeout
/// lives (inherited ADR 0015) — a capture run happens on the reader's machine
/// when they press the button, and a feed that never answers would be a pane
/// that never answers. The body is read up to <see cref="MaxBodyBytes"/> and no
/// further: a channel page is around a megabyte and the id this project needs
/// from it is in the head, while a feed larger than the cap is not one worth
/// reading in full on a button press.
/// </para>
/// <para>
/// The client's timeout covers the request up to the headers and no further —
/// the response is read as <c>ResponseHeadersRead</c> so the cap can stop the
/// body early — so the body gets the same bound again, on its own clock,
/// through a token this class links for the read. Either running out is the
/// <see cref="TimeoutException"/> the caller is promised.
/// </para>
/// </summary>
internal sealed class FeedFetcher(IHttpClientFactory clients)
{
    /// <summary>Two megabytes. Comfortably more than any feed and most pages;
    /// a bound rather than a budget.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>();

    /// <summary>Fetches one document. A non-success status is an
    /// <see cref="HttpRequestException"/> that names it; the client's timeout
    /// elapsing is a <see cref="TimeoutException"/> rather than the
    /// cancellation it is dressed as, so a caller can tell it from its own
    /// token.</summary>
    public Task<FetchedDocument> GetAsync(Uri url, CancellationToken cancellationToken) =>
        GetAsync(url, headers: null, cancellationToken);

    /// <summary>The same, with headers that belong to this one request rather
    /// than to the client — a cookie one host insists on is not something
    /// every other host should be shown.</summary>
    public async Task<FetchedDocument> GetAsync(
        Uri url,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        using var client = clients.CreateClient(CaptureHttpClients.Feeds);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            foreach (var (name, value) in headers ?? EmptyHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"the server answered {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()})",
                    inner: null,
                    response.StatusCode);
            }

            var body = await ReadCappedAsync(response, client.Timeout, cancellationToken).ConfigureAwait(false);

            return new FetchedDocument(
                response.RequestMessage?.RequestUri ?? url,
                response.Content.Headers.ContentType,
                body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, or the body read's copy of it: the
            // caller did not cancel, so it was the clock.
            throw new TimeoutException($"no answer within {client.Timeout.TotalSeconds:0} seconds");
        }
    }

    /// <summary>The body under the cap and under the timeout — the latter on
    /// a token of this method's own, because the client stopped counting when
    /// the headers arrived.</summary>
    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(timeout);

        await using var stream = await response.Content.ReadAsStreamAsync(bodyTimeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();

        var chunk = new byte[16 * 1024];
        int read;

        while (buffer.Length < MaxBodyBytes
               && (read = await stream.ReadAsync(chunk, bodyTimeout.Token).ConfigureAwait(false)) > 0)
        {
            var take = (int)Math.Min(read, MaxBodyBytes - buffer.Length);
            buffer.Write(chunk, 0, take);
        }

        return buffer.ToArray();
    }
}
