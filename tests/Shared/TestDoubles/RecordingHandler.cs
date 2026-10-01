using System.Net;
using System.Text;

namespace Backlog.Tests;

/// <summary>
/// An HTTP handler that keeps the last request it was sent, its body and how
/// many it saw, and answers each one either with <paramref name="respond"/> or,
/// when none is given, with <see cref="Status"/> and an empty JSON object.
/// </summary>
/// <param name="respond">How to answer a request; the fixed JSON answer when omitted.</param>
internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }

    public string? Body { get; private set; }

    public int RequestCount { get; private set; }

    /// <summary>What every request is answered with when no
    /// <paramref name="respond"/> was given; OK unless a test wants a
    /// refusal.</summary>
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return respond is not null
            ? respond(request)
            : new HttpResponseMessage(Status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
    }
}
