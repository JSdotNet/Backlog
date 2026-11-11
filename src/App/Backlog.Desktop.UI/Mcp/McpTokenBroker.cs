using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.FileSystem;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The token broker of local ADR 0052 §3: a named pipe only the person's own Windows SID can
/// open, which hands the <c>headersHelper</c> a grant token for the grant id it names. The OS
/// has authenticated the caller by the time a byte arrives, so nobody is prompted.
/// <para>
/// <b>Wire format.</b> One UTF-8 JSON line each way per connection, then the connection
/// ends. The request is <c>{"grant":"g_…"}</c>, at most <see cref="MaxRequestBytes"/>. The
/// reply is <c>{"token":"…","expiresAt":"…"}</c>, or <c>{"error":"…"}</c> with
/// <c>unknown-grant</c>, <c>revoked</c>, <c>bad-request</c> or <c>forbidden</c>.
/// </para>
/// <para>
/// <b>Three defences, in order.</b> The pipe's ACL holds one rule, for the SID this process
/// runs as, with inheritance cut, so no other account can open it (<see cref="CreatePipeSecurity"/>).
/// The first instance is created with <see cref="PipeOptions.FirstPipeInstance"/>, so if
/// another process already owns the name the broker stays down and says so rather than share
/// it — the squatting defence on this side. And each client's SID is read again after its
/// request and compared (<see cref="IsSameUser"/>), so an ACL that somehow admitted another
/// account still mints it nothing.
/// </para>
/// <para>
/// <b>One instance, served one connection at a time and reused.</b> A served connection is
/// disconnected and the same instance waits again, so the name is never without its owner:
/// a second instance would need the create-instance right in the ACL, and a gap between
/// instances is a moment another process could take the name. A request is a few hundred
/// bytes answered in microseconds, and a client that sends nothing, or never reads or closes,
/// is cut off after <see cref="DefaultConnectionTimeout"/>.
/// </para>
/// <para>
/// Started and stopped on the same terms as the MCP listener (local ADR 0012 §2, ADR 0043 §5):
/// resolved and started after <c>Build()</c> by the desktop head alone, and disposed with the
/// head's container. The web harness composes no broker (ADR 0052 §9).
/// </para>
/// </summary>
public sealed class McpTokenBroker : IDisposable
{
    /// <summary>The most a request may be, newline included.</summary>
    public const int MaxRequestBytes = 4096;

    /// <summary>How long one connection may hold the pipe — send its request, read the reply
    /// and close — unless a test names another.</summary>
    private static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly McpGrantTokens _tokens;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _gate = new();
    private readonly TimeSpan _connectionTimeout;
    private Task? _loop;
    private bool _disposed;

    // Written on the thread pool by the loop, read by whoever asks.
    private volatile bool _isListening;
    private volatile string? _lastError;

    /// <summary>The broker for this installation and Windows user, on
    /// <see cref="PipeNameFor"/>'s name.</summary>
    public McpTokenBroker(McpGrantTokens tokens, ILogger<McpTokenBroker>? log = null)
        : this(tokens, PipeNameFor(WorkspaceSettingsStore.DefaultAppDataFolderName, Environment.UserName), log)
    {
    }

    /// <summary>The broker on <paramref name="pipeName"/>, for a test that must not take the
    /// installed app's name, and with a shorter <paramref name="connectionTimeout"/> for one that
    /// must not wait five seconds. Internal, so the container never picks it.</summary>
    internal McpTokenBroker(McpGrantTokens tokens, string pipeName, ILogger? log, TimeSpan? connectionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _tokens = tokens;
        PipeName = pipeName;
        _log = log ?? NullLogger.Instance;
        _connectionTimeout = connectionTimeout ?? DefaultConnectionTimeout;
    }

    /// <summary>The pipe's name, without the <c>\\.\pipe\</c> prefix.</summary>
    public string PipeName { get; }

    /// <summary>Whether the pipe is open and waiting for a client.</summary>
    public bool IsListening => _isListening;

    /// <summary>Why the broker is not listening, when it tried and could not.</summary>
    public string? LastError => _lastError;

    /// <summary>
    /// The pipe name for an installation and a Windows user: the app-data folder name
    /// (<c>Backlog</c>, or <c>Backlog.Debug</c> for a Debug build) lower-cased with its dot as a
    /// dash, then <c>-mcp-broker-</c>, then the user name lower-cased with every character
    /// outside <c>[a-z0-9._-]</c> as <c>_</c>. A Debug build and the installed app, or two
    /// users on one machine, never collide; the ACL, not the name, is what keeps others out.
    /// <c>tools/mcp/backlog-mcp-headers.mjs</c> computes the same name.
    /// </summary>
    public static string PipeNameFor(string appDataFolderName, string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataFolderName);
        ArgumentNullException.ThrowIfNull(userName);

        var installation = appDataFolderName.ToLowerInvariant().Replace('.', '-');
        var user = new string([.. userName.ToLowerInvariant().Select(character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-' ? character : '_')]);

        return installation + "-mcp-broker-" + user;
    }

    /// <summary>Whether the client's SID is this process's own. No SID, which is what a
    /// failed read of the client's token gives, is not.</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsSameUser(SecurityIdentifier? client, SecurityIdentifier own)
    {
        ArgumentNullException.ThrowIfNull(own);

        return client is not null && client.Equals(own);
    }

    /// <summary>The pipe's ACL: read and write for <paramref name="sid"/>, and nothing for
    /// anybody else — no inherited rule, no Everyone, no Administrators.</summary>
    [SupportedOSPlatform("windows")]
    public static PipeSecurity CreatePipeSecurity(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    /// <summary>Opens the pipe and serves it on the thread pool until disposed. Once only;
    /// off Windows it does nothing, because there is no pipe ACL to hold it to.</summary>
    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _log.LogInformation("The MCP token broker runs on Windows only; grant tokens are not available here.");
            return;
        }

        lock (_gate)
        {
            if (_disposed || _loop is not null) return;

            _loop = Listen();
        }
    }

    /// <summary>The serving loop, on the thread pool.</summary>
    [SupportedOSPlatform("windows")]
    private Task Listen()
    {
        var lifetime = _lifetime.Token;
        return Task.Run(() => ListenAsync(lifetime));
    }

    /// <summary>
    /// Reads one request from <paramref name="stream"/>, asks <paramref name="isSameUser"/>
    /// whether the caller is this user — after the request, because a pipe cannot say who its
    /// client is until it has read from it — and writes the one-line reply.
    /// </summary>
    internal async Task HandleAsync(Stream stream, Func<bool> isSameUser, CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);

        object reply;

        if (!isSameUser())
        {
            _log.LogWarning("The MCP token broker refused a client that is not this Windows user.");
            reply = new ErrorReply("forbidden");
        }
        else if (ParseGrantId(request) is not { } grantId)
        {
            _log.LogWarning("The MCP token broker refused a request that names no grant.");
            reply = new ErrorReply("bad-request");
        }
        else
        {
            var minted = _tokens.Mint(grantId);

            if (minted.Token is { } token)
            {
                _log.LogInformation("The MCP token broker minted a token for grant {GrantId}.", grantId);
                reply = new TokenReply(token.Value, token.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
            }
            else
            {
                var error = ErrorFor(minted.Refusal);
                _log.LogWarning("The MCP token broker refused grant {GrantId}: {Refusal}.", grantId, error);
                reply = new ErrorReply(error);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply, reply.GetType(), JsonOptions) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Task? loop;

        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            loop = _loop;
        }

        _lifetime.Cancel();

        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop logs what ended it; disposal only needs it to have stopped.
        }

        _lifetime.Dispose();
    }

    [SupportedOSPlatform("windows")]
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        NamedPipeServerStream pipe;
        SecurityIdentifier own;

        try
        {
            own = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("This process's Windows user has no SID.");

            pipe = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                inBufferSize: 0,
                outBufferSize: 0,
                CreatePipeSecurity(own));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _lastError = CouldNotCreate(ex);
            _log.LogError(ex, "The MCP token broker could not create its pipe {PipeName}; it stays down rather than share the name.", PipeName);
            return;
        }

        using (pipe)
        {
            _isListening = true;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await ServeAsync(pipe, own, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
#pragma warning disable CA1031 // The broker is down after this, and says so; the app runs on without grant tokens.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _lastError = "The MCP token broker stopped: " + ex.Message;
                _log.LogError(ex, "The MCP token broker stopped serving {PipeName}.", PipeName);
            }
            finally
            {
                _isListening = false;
            }
        }
    }

    /// <summary>
    /// What <see cref="ListenAsync"/> could not do, said by its cause: a name another process
    /// holds is the squatting case, and the others are this process's own.
    /// </summary>
    private static string CouldNotCreate(Exception cause) => cause switch
    {
        UnauthorizedAccessException =>
            "Windows refused the MCP token broker its pipe — another account may hold the name — so grant tokens are not available until the app restarts.",
        InvalidOperationException =>
            "The MCP token broker could not read this Windows user's SID, so it opened no pipe and grant tokens are not available.",
        _ =>
            "Another process already holds the MCP token broker's pipe, so grant tokens are not available until the app restarts."
    };

    /// <summary>
    /// One connection: its request, its reply, then the instance is free again — and never
    /// later than <see cref="_connectionTimeout"/>, whatever the client does.
    /// <para>
    /// The reply is not drained with <see cref="PipeStream.WaitForPipeDrain"/>, which blocks
    /// without a timeout on a client that never reads. The broker reads instead until the client
    /// closes its end — which a client does once it has the line — or the timeout passes, and
    /// only then disconnects: disconnecting first would discard a reply still in the pipe.
    /// </para>
    /// <para>
    /// Whatever goes wrong inside one connection ends that connection and nothing else: a store
    /// that cannot be read, or a client that misbehaves, is logged by its type and the next
    /// client is served. No token is ever in the log.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private async Task ServeAsync(NamedPipeServerStream pipe, SecurityIdentifier own, CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connection.CancelAfter(_connectionTimeout);

        try
        {
            await HandleAsync(pipe, () => IsSameUser(ClientSid(pipe), own), connection.Token).ConfigureAwait(false);
            await ClientClosedAsync(pipe, connection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("The MCP token broker dropped a client that held its pipe past {Timeout}.", _connectionTimeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // One connection's failure is that connection's; the broker serves the next.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning("The MCP token broker dropped a connection: {Failure}.", ex.GetType().Name);
        }
        finally
        {
            Disconnect(pipe);
        }
    }

    /// <summary>Waits until the client has closed its end, reading and discarding anything it
    /// sends after its request.</summary>
    private static async Task ClientClosedAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var discard = new byte[256];

        try
        {
            while (await pipe.ReadAsync(discard, cancellationToken).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (IOException)
        {
            // A broken pipe is a client that closed: what was being waited for.
        }
    }

    /// <summary>
    /// Frees the instance for the next client. Unconditionally, not only while
    /// <see cref="PipeStream.IsConnected"/>: a client that closed first leaves the pipe broken,
    /// which reads as not connected, and an instance not disconnected from that state cannot
    /// wait for the next connection — the loop would end on it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void Disconnect(NamedPipeServerStream pipe)
    {
        try
        {
            pipe.Disconnect();
        }
        catch (InvalidOperationException)
        {
            // Never connected, or already disconnected: free as it is.
        }
        catch (IOException ex)
        {
            _log.LogWarning("The MCP token broker could not disconnect a client: {Failure}.", ex.GetType().Name);
        }
    }

    /// <summary>The connected client's SID, read under its own token, or null when the client
    /// cannot be impersonated.</summary>
    [SupportedOSPlatform("windows")]
    private SecurityIdentifier? ClientSid(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? sid = null;

        try
        {
            pipe.RunAsClient(() =>
            {
                using var client = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                sid = client.User;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.LogWarning(ex, "The MCP token broker could not read its client's identity.");
        }

        return sid;
    }

    /// <summary>The request line, up to its newline, the end of the stream, or one byte past
    /// <see cref="MaxRequestBytes"/> — null then, so an oversize request is never parsed.</summary>
    private static async Task<byte[]?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            var newline = Array.IndexOf(buffer, (byte)'\n', length, read);
            length += read;

            if (newline >= 0) return buffer[..newline];
        }

        return length > MaxRequestBytes ? null : buffer[..length];
    }

    /// <summary>The grant id of a request that is one JSON object with a non-blank string
    /// <c>grant</c>, and nothing after it; null for anything else.</summary>
    private static string? ParseGrantId(byte[]? request)
    {
        if (request is null) return null;

        try
        {
            var reader = new Utf8JsonReader(request);
            using var document = JsonDocument.ParseValue(ref reader);

            if (reader.Read()) return null;

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("grant", out var grant)
                && grant.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(grant.GetString())
                    ? grant.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The wire's word for why a mint was refused. A mint is only ever refused for
    /// its grant; the token refusals are the listener's and cannot arise here.</summary>
    private static string ErrorFor(McpGrantRefusal? refusal) => refusal switch
    {
        McpGrantRefusal.UnknownGrant => "unknown-grant",
        McpGrantRefusal.Revoked => "revoked",
        McpGrantRefusal.BadToken or McpGrantRefusal.Expired or McpGrantRefusal.StaleVersion or McpGrantRefusal.StoreUnavailable or null => "bad-request"
    };

    private sealed record TokenReply(string Token, string ExpiresAt);

    private sealed record ErrorReply(string Error);
}
