using System.Net;
using System.Net.Http.Headers;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Downloads a repository branch as a zip archive.
/// <para>
/// It does not go through <see cref="IGitHubTransport"/>, and cannot. That
/// interface returns <see cref="System.Text.Json.JsonElement"/> — the token
/// transport parses every response as JSON and the CLI transport shells out to
/// <c>gh api</c> and reads stdout as text, so neither can carry a zip at all.
/// What it reuses instead is the layer underneath: the same
/// <see cref="IGitHubCredentialResolver"/> that decides which identity a path
/// goes out as, so an archive is fetched as the account the repository is bound
/// to rather than as whatever this machine happens to hold.
/// </para>
/// </summary>
public interface IGitHubArchiveClient
{
    /// <summary>
    /// The branch as a zip stream, which the caller owns and must dispose.
    /// <para>
    /// Streamed rather than buffered into a byte array: a repository archive is
    /// the whole tree, and the caller writes it straight to a staging folder, so
    /// there is no point holding a copy of it in memory on the way past.
    /// </para>
    /// </summary>
    /// <exception cref="GitHubException">The archive could not be fetched — no
    /// such branch, no access, or the network refused.</exception>
    Task<Stream> DownloadBranchZipAsync(
        GitHubRepositoryRef repository,
        string branch,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class GitHubArchiveClient : IGitHubArchiveClient
{
    /// <summary>
    /// How long one archive download may take.
    /// <para>
    /// Explicit because guideline 0015 requires it of every outbound call, and
    /// generous because this one is not a request-response — it is a whole
    /// repository tree over whatever connection somebody has. A minute would
    /// fail honest downloads on a slow line; nothing is retried, so the only job
    /// this value has is stopping a hung socket from being forever.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly IGitHubCredentialResolver _credentials;
    private readonly Func<string?> _apiEndpoint;

    public GitHubArchiveClient(
        IGitHubCredentialResolver credentials,
        HttpClient http,
        Func<string?>? apiEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(http);

        _credentials = credentials;
        _http = http;
        _apiEndpoint = apiEndpoint ?? (() => GitHubSettings.DefaultApiEndpoint);

        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Backlog");

        // The handler's own timeout has to be at least the one this class
        // enforces, or the default hundred seconds would cut off honest
        // downloads before the linked token ever fired. Both are set because
        // they cover different halves: with ResponseHeadersRead, this one runs
        // out at the response headers and the linked token covers the body.
        if (_http.Timeout < Timeout) _http.Timeout = Timeout;
    }

    public async Task<Stream> DownloadBranchZipAsync(
        GitHubRepositoryRef repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        var path = $"repos/{repository.Owner}/{repository.Name}/zipball/{Uri.EscapeDataString(branch.Trim())}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", IGitHubTransport.DefaultApiVersion);

        // A public repository's archive downloads without one, which is what makes
        // knowledge readable on a machine that has never signed in to anything.
        // GitHub answers the archive endpoint with a redirect to a pre-signed
        // codeload URL, and HttpClient drops this header when it crosses to that
        // host — which is both expected and wanted, since the signature is the
        // credential from there on.
        if (await Credential(path, timeout.Token).ConfigureAwait(false) is { } credential)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubException($"Could not reach GitHub to download {repository.FullName} ({branch}): {exception.Message}", exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubException($"Downloading {repository.FullName} ({branch}) took longer than {Timeout.TotalMinutes:0} minutes.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();

            throw new GitHubException(status switch
            {
                HttpStatusCode.NotFound =>
                    $"{repository.FullName} has no branch called {branch}, or this account cannot see it.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    $"GitHub refused access to {repository.FullName} ({branch}). Check the account this repository is worked as.",
                _ => $"GitHub returned {(int)status} downloading {repository.FullName} ({branch})."
            });
        }

        // The response owns the stream, so it is disposed with it. Returning a
        // wrapper that keeps both alive is what lets the caller treat this as an
        // ordinary stream it disposes once.
        return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<GitHubCredential?> Credential(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _credentials.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubNotConfiguredException)
        {
            // The repository is bound to an account this machine cannot satisfy.
            // Anonymous is not a substitute for a named identity on a private
            // repository, but it is exactly right for a public one, and the
            // difference shows up as the 404 above rather than as a guess here.
            return null;
        }
    }

    private Uri Endpoint(string path)
    {
        var root = _apiEndpoint() is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim().TrimEnd('/')
            : GitHubSettings.DefaultApiEndpoint;

        return new Uri($"{root}/{path}");
    }

    /// <summary>Keeps the response alive for as long as the stream read from it
    /// is, so disposing the stream disposes both.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
