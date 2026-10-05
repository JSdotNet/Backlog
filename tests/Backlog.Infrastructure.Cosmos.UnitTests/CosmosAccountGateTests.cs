using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// The gate every adapter waits on before its first operation: through as soon
/// as the account answers, the coded 503 within the budget when it does not,
/// and never a failure remembered past the next caller. The endpoint-level case
/// — an account that accepts the connection and never replies — is
/// <c>UnreachableReplicaEndpointTests</c> in the sync service's tests.
/// </summary>
public class CosmosAccountGateTests
{
    private const string Message = "The store is not available yet.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_that_answers_lets_the_caller_through()
    {
        var account = new ScriptedAccount(_ => Task.FromResult<AccountProperties>(null!));

        await Gate(account).WaitUntilReachable(Message, Cancellation);

        Assert.Equal(1, account.Reads);
    }

    /// <summary>Once is enough: the account read is the SDK's initialization,
    /// and a gate that asked again on every operation would be a second round
    /// trip on every operation.</summary>
    [Fact]
    public async Task An_account_that_answered_once_is_not_asked_again()
    {
        var account = new ScriptedAccount(_ => Task.FromResult<AccountProperties>(null!));
        var gate = Gate(account);

        await gate.WaitUntilReachable(Message, Cancellation);
        await gate.WaitUntilReachable(Message, Cancellation);

        Assert.Equal(1, account.Reads);
    }

    [Fact]
    public async Task An_account_that_never_answers_is_the_coded_503_once_the_budget_passes()
    {
        var never = new TaskCompletionSource<AccountProperties>();
        var gate = Gate(new ScriptedAccount(_ => never.Task));

        var refusal = await Assert.ThrowsAsync<SyncReplicaException>(
            () => gate.WaitUntilReachable(Message, Cancellation).AsTask());

        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, refusal.Code);
        Assert.Equal(Message, refusal.Message);
    }

    /// <summary>The second caller shares the read already in flight rather than
    /// starting another, so a silent account is asked once however many
    /// requests are waiting on it.</summary>
    [Fact]
    public async Task Callers_while_the_account_is_silent_share_one_read()
    {
        var never = new TaskCompletionSource<AccountProperties>();
        var account = new ScriptedAccount(_ => never.Task);
        var gate = Gate(account);

        await Assert.ThrowsAsync<SyncReplicaException>(() => gate.WaitUntilReachable(Message, Cancellation).AsTask());
        await Assert.ThrowsAsync<SyncReplicaException>(() => gate.WaitUntilReachable(Message, Cancellation).AsTask());

        Assert.Equal(1, account.Reads);
    }

    /// <summary>The emulator coming up after a failed read is the ordinary
    /// first minutes of a local run; a gate that remembered the failure would
    /// answer 503 for the life of the process.</summary>
    [Fact]
    public async Task A_failed_read_is_not_remembered_past_the_next_caller()
    {
        var account = new ScriptedAccount(read => read == 1
            ? Task.FromException<AccountProperties>(new HttpRequestException("No connection could be made."))
            : Task.FromResult<AccountProperties>(null!));
        var gate = Gate(account);

        await Assert.ThrowsAsync<SyncReplicaException>(() => gate.WaitUntilReachable(Message, Cancellation).AsTask());
        await gate.WaitUntilReachable(Message, Cancellation);

        Assert.Equal(2, account.Reads);
    }

    private static CosmosAccountGate Gate(CosmosClient account)
    {
        var services = new ServiceCollection().AddSingleton(account).BuildServiceProvider();

        return new CosmosAccountGate(
            services,
            Options.Create(new CosmosOptions { ReadinessTimeoutSeconds = 1 }),
            NullLogger<CosmosAccountGate>.Instance);
    }

    /// <summary>A client whose account read answers as the test says. The
    /// SDK's protected constructor is there for exactly this.</summary>
    private sealed class ScriptedAccount(Func<int, Task<AccountProperties>> read) : CosmosClient
    {
        private int _reads;

        public int Reads => _reads;

        public override Task<AccountProperties> ReadAccountAsync() => read(Interlocked.Increment(ref _reads));
    }
}
