using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// Keeps the sync service holding this phone's push token (local ADR 0038 §4): the token goes
/// to <c>PUT /api/sync/notifications/push</c> behind the device token, and the service tags its
/// Notification Hubs installation with the owner and the device that token names.
/// <para>
/// Once per device and token. It registers when the app starts, when the phone is paired anew —
/// a new device id is a new installation — and when Firebase issues a new token. A service with
/// no hub answers "not registered", which is remembered like a success so the phone does not
/// ask again; a refusal or no network is not, and the next of those occasions tries again.
/// What it remembers is the process's: a cold start registers once more, which the service
/// takes as an overwrite of the same installation.
/// </para>
/// <para>
/// Never throws for a failed registration, and never logs the token.
/// </para>
/// </summary>
public sealed class PushRegistration : IDisposable
{
    /// <summary>The most each outbound step of a registration may take — reading the token from
    /// Play Services, then the call to the sync service — so neither can hold the background work
    /// that started it (guideline ADR 0015).</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly IPushTokenSource _tokens;
    private readonly IDeviceCredentialStore _credentials;
    private readonly CloudSyncClient _sync;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private (Guid DeviceId, string Token)? _registered;
    private bool _started;

    public PushRegistration(
        IPushTokenSource tokens,
        IDeviceCredentialStore credentials,
        CloudSyncClient sync,
        TimeProvider clock,
        ILogger<PushRegistration>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(clock);

        _tokens = tokens;
        _credentials = credentials;
        _sync = sync;
        _clock = clock;
        _logger = logger ?? NullLogger<PushRegistration>.Instance;
    }

    /// <summary>The registration <see cref="Start"/> or a new pairing set going, for a caller
    /// that has to know it finished — the tests.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Registers now, in the background, and again whenever the phone is paired anew.
    /// A second call does nothing.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        _credentials.Changed += OnCredentialsChanged;
        Pending = RegisterAsync();
    }

    /// <summary>Registers the current token for the paired device, unless that pair is already
    /// registered. True when the service now holds an installation for it.</summary>
    public async Task<bool> RegisterAsync(CancellationToken cancellationToken = default)
    {
        string? token;

        try
        {
            using var budget = Budget(cancellationToken);
            token = await _tokens.GetTokenAsync(budget.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("No push token to register: {Error}", ex.GetType().Name);
            return false;
        }

        return await RegisterTokenAsync(token, cancellationToken);
    }

    /// <summary>Firebase issued <paramref name="token"/> in place of the last one.</summary>
    public Task<bool> TokenRefreshedAsync(string token, CancellationToken cancellationToken = default) =>
        RegisterTokenAsync(token, cancellationToken);

    public void Dispose()
    {
        if (_started) _credentials.Changed -= OnCredentialsChanged;
        _gate.Dispose();
    }

    private void OnCredentialsChanged() => Pending = RegisterAsync();

    /// <summary><paramref name="cancellationToken"/>, or <see cref="CallBudget"/> on the clock,
    /// whichever ends first.</summary>
    private BudgetToken Budget(CancellationToken cancellationToken) => new(_clock, cancellationToken);

    /// <summary>A timer source on the injected clock, linked to the caller's token.</summary>
    private sealed class BudgetToken : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _linked;

        public BudgetToken(TimeProvider clock, CancellationToken cancellationToken)
        {
            _timeout = new CancellationTokenSource(CallBudget, clock);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeout.Token);
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            _linked.Dispose();
            _timeout.Dispose();
        }
    }

    private async Task<bool> RegisterTokenAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || _credentials.Current is not { } device) return false;

        var pair = (device.DeviceId, token);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_registered == pair) return true;

            using var budget = Budget(cancellationToken);

            var response = await _sync.RegisterPushAsync(token, budget.Token);

            // Not registered is the service saying it has no hub: remembered all the same,
            // so the phone does not ask it again until something changes.
            _registered = pair;

            if (!response.Registered) _logger.LogInformation("The sync service has no push hub; this phone gets no pushes.");
            return response.Registered;
        }
        catch (Exception ex) when (ex is HttpRequestException or SyncUnavailableException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("Push registration did not reach the sync service: {Error}", (ex as SyncUnavailableException)?.Code ?? ex.GetType().Name);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}
