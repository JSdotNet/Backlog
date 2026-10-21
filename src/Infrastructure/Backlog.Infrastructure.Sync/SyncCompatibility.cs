using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Whether this app may sync with the service it is pointed at: not when the
/// service reports a newer version than the app's own.
/// <para>
/// The service and the apps are released apart — the service by hand, the
/// apps from their own tags — so a service can move its contract past an app
/// still installed somewhere. An app that went on syncing against it is how
/// fields get lost; this stops it and says so instead. The rule is the version
/// alone, so the service's <c>&lt;Version&gt;</c> is raised on purpose
/// (<c>docs/deployment/sync.md</c>).
/// </para>
/// <para>
/// One verdict per process, held for <see cref="VerdictWindow"/> and then asked
/// again, so a service rolled back to a version this app matches lets it sync
/// again without a restart. A service that cannot be asked, or answers with no
/// version this can read, is no reason to stop: sync goes on as it did before
/// this check existed, and the question is asked again after
/// <see cref="RetryAfterFailedProbe"/>.
/// </para>
/// </summary>
public sealed class SyncCompatibility : IDisposable
{
    /// <summary>What a person reads wherever a sync failure is shown.</summary>
    public const string UpdateRequiredMessage =
        "The sync service is newer than this version of Backlog. Update Backlog to keep syncing.";

    /// <summary>How long a verdict stands before the service is asked again.</summary>
    public static readonly TimeSpan VerdictWindow = TimeSpan.FromMinutes(10);

    /// <summary>How soon a question the service did not answer is asked again —
    /// shorter than <see cref="VerdictWindow"/>, because a service that was not
    /// there may come back newer.</summary>
    internal static readonly TimeSpan RetryAfterFailedProbe = TimeSpan.FromMinutes(1);

    /// <summary>How long the question may take. It sits in front of a real
    /// request, so a service that hangs must not hang it twice as long.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly SyncAppVersion _app;
    private readonly TimeProvider _time;
    private readonly ILogger<SyncCompatibility> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();

    private string? _origin;
    private DateTimeOffset _askAgainAt;
    private bool _updateRequired;
    private string? _serviceVersion;

    public SyncCompatibility(SyncAppVersion app, TimeProvider time, ILogger<SyncCompatibility>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(time);

        _app = app;
        _time = time;
        _logger = logger ?? NullLogger<SyncCompatibility>.Instance;
    }

    /// <summary>True while the service last answered with a version newer than
    /// this app's. Nothing goes out while it holds.</summary>
    public bool UpdateRequired
    {
        get { lock (_lock) return _updateRequired; }
    }

    /// <summary>The version the service last reported, or null before it was
    /// asked or when it could not say.</summary>
    public string? ServiceVersion
    {
        get { lock (_lock) return _serviceVersion; }
    }

    /// <summary>The version this app compares, as its head reported it.</summary>
    public string AppVersion => _app.Value;

    /// <summary>Raised when <see cref="UpdateRequired"/> changes, on whichever
    /// thread asked.</summary>
    public event Action? Changed;

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Whether a request to <paramref name="requestUri"/> must not go out,
    /// asking the service through <paramref name="send"/> first when the verdict
    /// for that address is missing or stale. The verdict is per address: the
    /// Settings page can point the app at another service while it runs.
    /// </summary>
    internal async Task<bool> UpdateRequiredForAsync(
        Uri requestUri,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var origin = requestUri.GetLeftPart(UriPartial.Authority);

        if (Current(origin) is { } cached) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Whoever was ahead in the queue has already asked.
            if (Current(origin) is { } fresh) return fresh;

            var serviceVersion = await AskAsync(new Uri(origin + "/"), send, cancellationToken).ConfigureAwait(false);
            var updateRequired = SyncVersion.IsServiceNewer(serviceVersion, _app.Value);

            bool changed;
            lock (_lock)
            {
                changed = _updateRequired != updateRequired;
                _origin = origin;
                _serviceVersion = serviceVersion;
                _updateRequired = updateRequired;
                _askAgainAt = _time.GetUtcNow() + (serviceVersion is null ? RetryAfterFailedProbe : VerdictWindow);
            }

            if (updateRequired)
            {
                _logger.LogWarning(
                    "The sync service at {Origin} reports version {ServiceVersion}, newer than this app's {AppVersion}; sync is paused until the app is updated.",
                    origin, serviceVersion, _app.Value);
            }

            if (changed) Changed?.Invoke();

            return updateRequired;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool? Current(string origin)
    {
        lock (_lock)
        {
            return _origin == origin && _time.GetUtcNow() < _askAgainAt ? _updateRequired : null;
        }
    }

    /// <summary>The <c>version</c> the service root reports, or null when it
    /// could not be asked or did not say.</summary>
    private async Task<string?> AskAsync(
        Uri root,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(ProbeTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, root);
            using var response = await send(probe, linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "The sync service root answered {Status}; its version is unknown, so sync goes on.", (int)response.StatusCode);
                return null;
            }

            using var body =await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: linked.Token).ConfigureAwait(false);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        // Anything short of the caller giving up: no network, a timeout, the
        // resilience pipeline's own rejection, a body that is not JSON. None of
        // them says the service is newer.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "The sync service version could not be read; sync goes on.");
            return null;
        }
    }
}
