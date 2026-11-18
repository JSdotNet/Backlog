using System.Net;
using Backlog.Infrastructure.Cosmos.ConnectorCredentials;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// What the connector credential stores do when Cosmos faults at the moments that matter:
/// after a code has burned or a refresh token has rotated, a fault must not fail the grant,
/// because the client would never receive the token it was owed and its retry would read
/// as a replay that revokes the family. Before anything committed, a fault still goes out
/// as the coded, retryable problem. The container is <see cref="ScriptedContainer"/>.
/// </summary>
public class ConnectorCredentialStoreFaultTests
{
    private const string ClientId = "claude-connector";
    private static readonly OwnerId Owner = new(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293"));
    private static readonly Guid Family = Guid.Parse("4c3f3d0a-4b4e-4c6d-9e5f-708192a3b4c5");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly string SecretHash = "ab" + new string('0', 62);
    private static readonly string OtherSecretHash = "ac" + new string('0', 62);
    private static readonly string PresentedHash = "cd" + new string('0', 62);
    private static readonly string NextHash = "ef" + new string('0', 62);

    private static readonly string ClientDoc = ConnectorCredentialDocumentFactory.ClientDocumentId(ClientId);
    private static readonly string PresentedDoc = ConnectorCredentialDocumentFactory.RefreshTokenDocumentId(PresentedHash);
    private static readonly string NextDoc = ConnectorCredentialDocumentFactory.RefreshTokenDocumentId(NextHash);
    private static readonly string MarkerDoc = ConnectorCredentialDocumentFactory.FamilyRevocationId(Family);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---- the client: the stamp is best effort ----

    [Theory]
    [InlineData("read", HttpStatusCode.TooManyRequests)]
    [InlineData("read", HttpStatusCode.ServiceUnavailable)]
    [InlineData("replace", HttpStatusCode.TooManyRequests)]
    [InlineData("replace", HttpStatusCode.RequestTimeout)]
    public async Task A_stamp_that_faults_is_swallowed_and_logged(string operation, HttpStatusCode status)
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 3)));
        container.Script = (op, _) => op == operation ? new ScriptedFault(status) : null;
        var log = new CountingLogger<CosmosConnectorClientStore>();

        await Clients(container, log).StampLastUsed(ClientId, 3, Now.AddHours(1), Cancellation);

        Assert.Null(container.Get<ConnectorClientDocument>(ClientDoc)!.LastUsedAt);
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public async Task A_stamp_still_records_the_last_use_when_the_store_answers()
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 3)));

        await Clients(container).StampLastUsed(ClientId, 3, Now.AddHours(1), Cancellation);

        Assert.Equal(Now.AddHours(1), container.Get<ConnectorClientDocument>(ClientDoc)!.LastUsedAt);
    }

    [Fact]
    public async Task A_stamp_does_not_swallow_the_callers_own_cancellation()
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 3)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Clients(container).StampLastUsed(ClientId, 3, Now, cancelled.Token));
    }

    // ---- the client: Save stores only the generation after the stored one ----

    [Fact]
    public async Task The_first_save_creates_the_client()
    {
        var container = new ScriptedContainer();

        await Clients(container).Save(Client(generation: 1), Cancellation);

        Assert.Equal(1, container.Get<ConnectorClientDocument>(ClientDoc)!.Generation);
    }

    [Fact]
    public async Task A_save_of_the_next_generation_replaces_the_client()
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 3)));

        await Clients(container).Save(Client(generation: 4, OtherSecretHash), Cancellation);

        var stored = container.Get<ConnectorClientDocument>(ClientDoc)!;
        Assert.Equal(4, stored.Generation);
        Assert.Equal(OtherSecretHash, stored.SecretHash);
    }

    /// <summary>Two devices pressed Replace at once: both read generation 3, the first
    /// stored 4. The second's 4, with another hash, must not land over it — the first
    /// secret's tokens would keep refreshing under a generation the second now owns.</summary>
    [Fact]
    public async Task A_save_computed_from_a_stale_read_is_refused_as_retryable()
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 4)));

        var refusal = await Assert.ThrowsAsync<SyncReplicaException>(
            () => Clients(container).Save(Client(generation: 4, OtherSecretHash), Cancellation));

        Assert.Equal(SyncErrorCodes.ReplicaBusy, refusal.Code);
        Assert.Equal(SecretHash, container.Get<ConnectorClientDocument>(ClientDoc)!.SecretHash);
    }

    /// <summary>A stamp between the save's read and its replace moves the etag but not the
    /// generation: the save reads again and lands.</summary>
    [Fact]
    public async Task A_save_that_loses_its_etag_to_a_stamp_reads_again_and_lands()
    {
        var container = new ScriptedContainer();
        container.Seed(ClientDoc, ConnectorCredentialDocumentFactory.From(Client(generation: 3)));
        var replaces = 0;
        container.Script = (op, _) =>
            op == "replace" && ++replaces == 1 ? new ScriptedFault(HttpStatusCode.PreconditionFailed) : null;

        await Clients(container).Save(Client(generation: 4, OtherSecretHash), Cancellation);

        Assert.Equal(4, container.Get<ConnectorClientDocument>(ClientDoc)!.Generation);
        Assert.Equal(2, replaces);
    }

    // ---- refresh tokens: the post-write marker check keeps the write ----

    [Fact]
    public async Task An_add_whose_second_marker_read_faults_keeps_the_token()
    {
        var container = new ScriptedContainer();
        var markerReads = 0;
        container.Script = (op, id) =>
            op == "read" && id == MarkerDoc && ++markerReads == 2 ? new ScriptedFault(HttpStatusCode.TooManyRequests) : null;

        await Tokens(container).Add(Token(PresentedHash), Cancellation);

        Assert.False(container.Get<ConnectorRefreshTokenDocument>(PresentedDoc)!.Revoked);
        Assert.Equal(2, markerReads);
    }

    [Fact]
    public async Task A_rotation_whose_second_marker_read_faults_keeps_its_win()
    {
        var container = Presented();
        var markerReads = 0;
        container.Script = (op, id) =>
            op == "read" && id == MarkerDoc && ++markerReads == 2 ? new ScriptedFault(HttpStatusCode.ServiceUnavailable) : null;

        Assert.True(await Tokens(container).TryRotate(PresentedHash, Token(NextHash), Cancellation));

        Assert.Equal(NextHash, container.Get<ConnectorRefreshTokenDocument>(PresentedDoc)!.ReplacedByHash);
        Assert.True(container.Contains(NextDoc));
    }

    [Fact]
    public async Task A_rotation_whose_family_was_revoked_meanwhile_still_loses()
    {
        var container = Presented();
        container.Script = (op, _) =>
        {
            if (op == "replace")
            {
                container.Seed(MarkerDoc, ConnectorCredentialDocumentFactory.FamilyRevocation(Family, Now));
            }

            return null;
        };

        Assert.False(await Tokens(container).TryRotate(PresentedHash, Token(NextHash), Cancellation));
        Assert.False(container.Contains(NextDoc));
    }

    // ---- refresh tokens: an ambiguous fault on the swap ----

    [Fact]
    public async Task A_swap_that_applied_before_its_fault_is_a_win()
    {
        var container = Presented();
        container.Script = (op, _) => op == "replace" ? new ScriptedFault(HttpStatusCode.RequestTimeout, Applied: true) : null;

        Assert.True(await Tokens(container).TryRotate(PresentedHash, Token(NextHash), Cancellation));

        Assert.True(container.Contains(NextDoc));
    }

    [Fact]
    public async Task A_swap_that_did_not_apply_is_the_coded_fault_and_leaves_the_token_live()
    {
        var container = Presented();
        container.Script = (op, _) => op == "replace" ? new ScriptedFault(HttpStatusCode.ServiceUnavailable) : null;

        var fault = await Assert.ThrowsAsync<SyncReplicaException>(
            () => Tokens(container).TryRotate(PresentedHash, Token(NextHash), Cancellation));

        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, fault.Code);
        Assert.Null(container.Get<ConnectorRefreshTokenDocument>(PresentedDoc)!.ReplacedByHash);
        Assert.False(container.Contains(NextDoc));
    }

    [Fact]
    public async Task A_swap_fault_that_cannot_be_read_back_keeps_the_successor_and_throws()
    {
        var container = Presented();
        var replaced = false;
        container.Script = (op, id) =>
        {
            if (op == "replace")
            {
                replaced = true;
                return new ScriptedFault(HttpStatusCode.RequestTimeout, Applied: true);
            }

            return op == "read" && replaced && id == PresentedDoc
                ? new ScriptedFault(HttpStatusCode.ServiceUnavailable)
                : null;
        };

        await Assert.ThrowsAsync<SyncReplicaException>(
            () => Tokens(container).TryRotate(PresentedHash, Token(NextHash), Cancellation));

        Assert.True(container.Contains(NextDoc));
    }

    private static ScriptedContainer Presented()
    {
        var container = new ScriptedContainer();
        container.Seed(PresentedDoc, ConnectorCredentialDocumentFactory.From(Token(PresentedHash), Now));
        return container;
    }

    private static ConnectorClient Client(int generation, string? secretHash = null) =>
        new(ClientId, secretHash ?? SecretHash, Now, LastUsedAt: null, generation);

    private static ConnectorRefreshToken Token(string hash) => new(
        hash,
        Family,
        ClientGeneration: 3,
        Owner,
        Scope: "inbox:capture",
        Resource: "https://sync.example/mcp",
        Now,
        Now.AddDays(30));

    private static CosmosConnectorClientStore Clients(ScriptedContainer container, ILogger<CosmosConnectorClientStore>? log = null) =>
        new(Services(container), Options.Create(new CosmosOptions()), log ?? NullLogger<CosmosConnectorClientStore>.Instance);

    private static CosmosConnectorRefreshTokenStore Tokens(ScriptedContainer container) =>
        new(Services(container), Options.Create(new CosmosOptions()), new FakeTimeProvider(Now));

    private static ServiceProvider Services(ScriptedContainer container)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CosmosClient>(new ScriptedCosmosClient(container));
        services.AddSingleton(Options.Create(new CosmosOptions()));
        services.AddSingleton<ILogger<CosmosAccountGate>>(NullLogger<CosmosAccountGate>.Instance);
        services.AddSingleton<CosmosAccountGate>();
        return services.BuildServiceProvider();
    }

    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }
}
