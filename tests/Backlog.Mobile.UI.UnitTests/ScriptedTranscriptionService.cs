using System.Net;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The sync service's transcription route, answered from a script: what it answers, and
/// every upload that reached it — the bytes, the declared type and the address, so a test
/// reads the language hint off the query.
/// </summary>
internal sealed class ScriptedTranscriptionService : HttpMessageHandler
{
    /// <summary>The answer to the next upload. Text by default.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(Text("hello from the cloud"));

    public List<Upload> Uploads { get; } = [];

    public static HttpResponseMessage Text(string text) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new TranscriptionResponse(text)) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string? code = null) =>
        new(status)
        {
            Content = JsonContent.Create(new { title = "refused", status = (int)status, code })
        };

    /// <summary>An answer that never comes, until the call is cancelled.</summary>
    public static async Task<HttpResponseMessage> Never(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    public CloudTranscriptionClient Client(TimeProvider? clock = null) =>
        new(new HttpClient(this) { BaseAddress = new Uri("https://sync.test") }, clock ?? TimeProvider.System);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uploads.Add(new Upload(
            request.Method,
            request.RequestUri!,
            request.Content?.Headers.ContentType?.ToString(),
            request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));

        return await Answer(request, cancellationToken);
    }

    public sealed record Upload(HttpMethod Method, Uri Address, string? ContentType, byte[] Body)
    {
        public string Path => Address.AbsolutePath;

        /// <summary>The <c>language</c> query, or null when the upload sent none.</summary>
        public string? Language => System.Web.HttpUtility.ParseQueryString(Address.Query)[SyncRoutes.TranscriptionLanguageQuery];
    }
}
