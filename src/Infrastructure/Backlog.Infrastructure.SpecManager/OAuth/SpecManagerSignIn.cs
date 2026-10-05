using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Polly;

namespace Backlog.Infrastructure.SpecManager.OAuth;

/// <summary>
/// Signing in to a spec-manager installation and keeping the sign-in alive: OAuth
/// 2.1's authorization code flow with PKCE, the browser sent back to a loopback
/// listener (RFC 8252), and the refresh that rotates the refresh token.
/// <para>
/// The client is registered dynamically (RFC 7591) once per base URL and the
/// registration kept with the tokens, so a sign-out and a sign-in do not leave a
/// client behind at the installation each time.
/// </para>
/// <para>
/// <b>Refreshes are serialized.</b> The installation rotates the refresh token on
/// every use and honours only the newest, so two callers refreshing at once would
/// each present the same token and the loser would sign the person out. One
/// <see cref="SemaphoreSlim"/> lets one refresh run; whoever waited reads what it
/// stored. The rotated token is stored before it is used for anything.
/// </para>
/// <para>
/// No token is logged, and none is put in an exception message.
/// </para>
/// </summary>
internal sealed class SpecManagerSignIn : IDisposable
{
    /// <summary>The name an account is shown with until the installation says
    /// whose it is.</summary>
    public const string FallbackAccountName = "spec-manager";

    /// <summary>What this app registers as: RFC 8252 §7.3's loopback redirect, any
    /// port, which spec-manager honours for <c>127.0.0.1</c>.</summary>
    public const string RegisteredRedirectUri = "http://127.0.0.1/callback";

    /// <summary>An access token this close to expiring is refreshed before use, so
    /// a request does not set out with a token that lapses on the way.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    /// <summary>The lifetime assumed when a token answer leaves
    /// <c>expires_in</c> out; spec-manager's own.</summary>
    private const int DefaultLifetimeSeconds = 3600;

    private readonly Func<HttpClient> _http;
    private readonly ISpecManagerTokenStore _store;
    private readonly SpecManagerOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<Uri, CancellationToken, Task> _openBrowser;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _signInGate = new(1, 1);
    private AuthorizationServerMetadata? _metadata;

    /// <summary>A rotated credential the store could not keep. The installation
    /// honours only the newest refresh token, so it is used for the rest of the run
    /// rather than going back to the dead one on disk, until a save succeeds.</summary>
    private volatile SpecManagerCredential? _unkept;

    /// <param name="http">A client per call, from the factory in a host.</param>
    /// <param name="openBrowser">Opens the authorization page;
    /// <see cref="SystemBrowser.OpenAsync"/> in a host.</param>
    public SpecManagerSignIn(
        Func<HttpClient> http,
        ISpecManagerTokenStore store,
        SpecManagerOptions options,
        TimeProvider time,
        Func<Uri, CancellationToken, Task> openBrowser,
        ILogger<SpecManagerSignIn>? log = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(openBrowser);

        _http = http;
        _store = store;
        _options = options;
        _time = time;
        _openBrowser = openBrowser;
        _log = log ?? NullLogger<SpecManagerSignIn>.Instance;
    }

    /// <summary>How long the browser is given to come back.</summary>
    public TimeSpan SignInTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Raised after a sign-in, a sign-out, a new account name, or a
    /// sign-in the installation stopped honouring.</summary>
    public event Action? AccountChanged;

    /// <summary>Who is signed in, or null.</summary>
    public TaskConnectorAccount? Account =>
        Current() is { IsSignedIn: true } credential
            ? new TaskConnectorAccount(
                string.IsNullOrWhiteSpace(credential.AccountName) ? FallbackAccountName : credential.AccountName,
                credential.SignedInAt ?? DateTimeOffset.MinValue)
            : null;

    /// <summary>
    /// Signs a person in through their browser. Null on success, otherwise what
    /// went wrong, for the person. Cancelling <paramref name="cancellationToken"/>
    /// throws; the browser not coming back within <see cref="SignInTimeout"/>
    /// answers.
    /// </summary>
    public async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        if (!await _signInGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return "A sign-in to spec-manager is already waiting in the browser. Finish or close that one first.";
        }

        try
        {
            return await SignInCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _signInGate.Release();
        }
    }

    /// <summary>Forgets the tokens and the account. The client registration is
    /// kept, so the next sign-in does not register again.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Current() is not { IsSignedIn: true } current) return;

            Keep(current.SignedOut());
        }
        finally
        {
            _refreshGate.Release();
        }

        AccountChanged?.Invoke();
    }

    /// <summary>Records whose account this is, once the installation has said.</summary>
    public void SetAccountName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Current() is not { IsSignedIn: true } current || current.AccountName == name) return;

        Keep(current with { AccountName = name });
        AccountChanged?.Invoke();
    }

    /// <summary>
    /// An access token to call with, refreshed first when it expires within
    /// <see cref="RefreshMargin"/>; null when nobody is signed in, or the
    /// installation no longer honours the sign-in. A refresh that fails for any
    /// other reason throws, and the sign-in is kept.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = Current();
        if (current is not { IsSignedIn: true }) return null;
        if (IsFresh(current)) return current.AccessToken;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Whoever held the gate may have refreshed already.
            current = Current();
            if (current is not { IsSignedIn: true }) return null;
            if (IsFresh(current)) return current.AccessToken;

            return await RefreshLockedAsync(current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// A new access token after the installation answered 401 to
    /// <paramref name="rejectedAccessToken"/>, or null when there is none to be had.
    /// When another caller has already replaced the rejected token, that one is
    /// answered rather than refreshing a second time.
    /// </summary>
    public async Task<string?> RefreshAfterRejectionAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Current();
            if (current is not { IsSignedIn: true }) return null;
            if (!string.Equals(current.AccessToken, rejectedAccessToken, StringComparison.Ordinal) && IsFresh(current))
            {
                return current.AccessToken;
            }

            return await RefreshLockedAsync(current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
        _signInGate.Dispose();
    }

    private async Task<string?> SignInCoreAsync(CancellationToken cancellationToken)
    {
        AuthorizationServerMetadata metadata;
        SpecManagerCredential registration;
        bool wasKept;
        try
        {
            metadata = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
            (registration, wasKept) = await EnsureRegisteredAsync(metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            _log.LogWarning(ex, "spec-manager at {BaseUrl} could not be reached to start a sign-in.", _options.Root);
            return $"spec-manager could not be reached at {_options.Root}. Check the connection and try again.";
        }
        catch (Exception ex) when (IsNotKept(ex))
        {
            _log.LogError(ex, "The spec-manager client registration could not be kept.");
            return "The sign-in could not be kept on this machine.";
        }

        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();

        using var listener = LoopbackRedirectListener.Start();
        var authorize = AuthorizeUri(metadata.AuthorizationEndpoint!, registration.ClientId, listener.RedirectUri, Pkce.Challenge(verifier), state);

        using var timeout = new CancellationTokenSource(SignInTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        LoopbackCallback callback;
        try
        {
            await _openBrowser(authorize, linked.Token).ConfigureAwait(false);
            callback = await listener.WaitForCallbackAsync(state, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An installation that has forgotten a kept client shows the person an
            // error page and never sends the browser back, which looks exactly like
            // this. Dropping the registration costs one registration if it was not
            // that; keeping it would fail every sign-in from now on if it was. Not
            // while someone is signed in, though: their refresh needs the client id.
            if (wasKept && Current() is { IsSignedIn: false })
            {
                TryDrop();
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"The sign-in was not finished in the browser within {SignInTimeout.TotalMinutes:0} minutes. Try again.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _log.LogWarning(ex, "The browser could not be opened for a spec-manager sign-in.");
            return "The browser could not be opened to sign in to spec-manager.";
        }

        if (callback.Error is not null) return callback.Error;

        TokenResponse? answer;
        try
        {
            answer = await RequestTokenAsync(metadata, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = registration.ClientId,
                ["code"] = callback.Code!,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = listener.RedirectUri.AbsoluteUri,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            _log.LogWarning(ex, "spec-manager at {BaseUrl} could not be reached to finish a sign-in.", _options.Root);
            return $"spec-manager could not be reached at {_options.Root} to finish the sign-in. Try again.";
        }

        if (answer is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 })
        {
            if (answer?.Error == "invalid_client")
            {
                _log.LogInformation("spec-manager at {BaseUrl} no longer knows this app's client; its registration is dropped.", _options.Root);
                TryDrop();
                return "spec-manager no longer knows this app's registration. Sign in again to register it afresh.";
            }

            return answer?.Error is { Length: > 0 } error
                ? $"spec-manager did not accept the sign-in ({error}). Try again."
                : "spec-manager did not answer the sign-in with a token. Try again.";
        }

        var now = _time.GetUtcNow();
        try
        {
            Keep(registration with
            {
                AccessToken = answer.AccessToken,
                AccessTokenExpiresAt = now.AddSeconds(answer.ExpiresIn ?? DefaultLifetimeSeconds),
                RefreshToken = answer.RefreshToken,
                AccountName = FallbackAccountName,
                SignedInAt = now,
            });
        }
        catch (Exception ex) when (IsNotKept(ex))
        {
            _log.LogError(ex, "A spec-manager sign-in could not be kept.");
            return "Signed in, but the sign-in could not be kept on this machine.";
        }

        AccountChanged?.Invoke();
        return null;
    }

    private async Task<string?> RefreshLockedAsync(SpecManagerCredential current, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            Forget(current, dropRegistration: false);
            return null;
        }

        var metadata = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var answer = await RequestTokenAsync(metadata, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = current.ClientId,
            ["refresh_token"] = current.RefreshToken,
        }, cancellationToken).ConfigureAwait(false);

        if (answer is { AccessToken.Length: > 0 })
        {
            // Stored before it is returned: the refresh token just presented is dead
            // at the installation, so losing this one is losing the sign-in.
            var next = current with
            {
                AccessToken = answer.AccessToken,
                AccessTokenExpiresAt = _time.GetUtcNow().AddSeconds(answer.ExpiresIn ?? DefaultLifetimeSeconds),
                RefreshToken = string.IsNullOrEmpty(answer.RefreshToken) ? current.RefreshToken : answer.RefreshToken,
            };

            try
            {
                Keep(next);
            }
            catch (Exception ex) when (IsNotKept(ex))
            {
                // The old token is dead whether or not this one is written, so it is
                // used from memory; the next sign-in after a restart is the cost.
                _unkept = next;
                _log.LogWarning(ex, "The refreshed spec-manager sign-in could not be kept on this machine; it is used until the app closes.");
            }

            return next.AccessToken;
        }

        if (answer?.Error is "invalid_grant" or "invalid_client")
        {
            _log.LogInformation(
                "spec-manager at {BaseUrl} no longer honours this sign-in ({Error}); signed out.",
                _options.Root,
                answer.Error);
            Forget(current, dropRegistration: answer.Error == "invalid_client");
            return null;
        }

        // Anything else — a 5xx, a page that is not a token answer — says nothing
        // about the sign-in, so it is kept and the call that needed it fails.
        throw new HttpRequestException("spec-manager did not refresh the sign-in.");
    }

    /// <summary>Drops the tokens and tells whoever shows the account. An
    /// installation that forgot the client loses the registration as well, so the
    /// next sign-in registers afresh.</summary>
    private void Forget(SpecManagerCredential current, bool dropRegistration)
    {
        _unkept = null;
        if (dropRegistration)
        {
            _store.Remove(_options.Root);
        }
        else
        {
            _store.Save(_options.Root, current.SignedOut());
        }

        AccountChanged?.Invoke();
    }

    /// <summary>What is kept for this installation: the rotated credential the
    /// store could not write, else the store's.</summary>
    private SpecManagerCredential? Current() => _unkept ?? _store.Get(_options.Root);

    /// <summary>Writes <paramref name="credential"/>; once that works, nothing is
    /// held in memory alone any more.</summary>
    private void Keep(SpecManagerCredential credential)
    {
        _store.Save(_options.Root, credential);
        _unkept = null;
    }

    /// <summary>Forgets the registration and anything with it, so the next sign-in
    /// registers afresh. A store that cannot remove it is logged, not thrown: the
    /// caller is already answering the person.</summary>
    private void TryDrop()
    {
        _unkept = null;
        try
        {
            _store.Remove(_options.Root);
        }
        catch (Exception ex) when (IsNotKept(ex))
        {
            _log.LogWarning(ex, "The spec-manager client registration could not be dropped.");
        }
    }

    private bool IsFresh(SpecManagerCredential credential) =>
        !string.IsNullOrEmpty(credential.AccessToken)
        && credential.AccessTokenExpiresAt is { } expires
        && expires - _time.GetUtcNow() > RefreshMargin;

    private async Task<AuthorizationServerMetadata> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (_metadata is { } known) return known;

        var http = _http();
        var metadata = await http.GetFromJsonAsync<AuthorizationServerMetadata>(
            new Uri(_options.Root + "/.well-known/oauth-authorization-server"),
            SpecManagerJson.Options,
            cancellationToken).ConfigureAwait(false);

        if (metadata is not { AuthorizationEndpoint: not null, TokenEndpoint: not null, RegistrationEndpoint: not null })
        {
            throw new HttpRequestException("spec-manager's authorization server metadata names no authorization, token or registration endpoint.");
        }

        _metadata = metadata;
        return metadata;
    }

    /// <summary>The kept registration, or a new one kept before it is used; and
    /// which of the two.</summary>
    private async Task<(SpecManagerCredential Registration, bool WasKept)> EnsureRegisteredAsync(AuthorizationServerMetadata metadata, CancellationToken cancellationToken)
    {
        if (Current() is { ClientId.Length: > 0 } kept) return (kept, true);

        var http = _http();
        using var response = await http.PostAsJsonAsync(
            metadata.RegistrationEndpoint,
            new ClientRegistrationRequest("Backlog", [RegisteredRedirectUri]),
            SpecManagerJson.Options,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var registration = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(registration?.ClientId))
        {
            throw new HttpRequestException("spec-manager registered the client without answering its id.");
        }

        var credential = new SpecManagerCredential(registration.ClientId, RegisteredRedirectUri);
        Keep(credential);
        return (credential, false);
    }

    /// <summary>Posts a form to the token endpoint and reads the answer, success or
    /// error alike; null when the body is not a token answer at all.</summary>
    private async Task<TokenResponse?> RequestTokenAsync(
        AuthorizationServerMetadata metadata,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var http = _http();
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(metadata.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);

        try
        {
            var answer = await response.Content.ReadFromJsonAsync<TokenResponse>(SpecManagerJson.Options, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode || answer?.Error is not null ? answer : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri AuthorizeUri(Uri endpoint, string clientId, Uri redirectUri, string challenge, string state)
    {
        var separator = string.IsNullOrEmpty(endpoint.Query) ? '?' : '&';
        return new Uri(
            endpoint.AbsoluteUri
            + separator
            + "client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri.AbsoluteUri)
            + "&response_type=code"
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=" + Pkce.Method
            + "&state=" + Uri.EscapeDataString(state));
    }

    /// <summary>Whether <paramref name="ex"/> says the installation could not be
    /// reached: a transport failure, an answer that is not what was asked for, the
    /// pipeline giving up (a total timeout, an open circuit), or a timeout that was
    /// not the caller cancelling.</summary>
    internal static bool IsUnreachable(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or JsonException or NotSupportedException or ExecutionRejectedException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Whether <paramref name="ex"/> is a token store failing to write.</summary>
    internal static bool IsNotKept(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or CryptographicException;
}
