using System.Net;

using Backlog.Infrastructure.Cosmos.Tasks;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// The three answers this adapter gives that no emulator is needed to pin: what
/// happens after the client fails to build, what a page claims about the rest of
/// the feed, and what a Cosmos failure becomes on the way out.
/// </summary>
public class CosmosTaskReplicaTests
{
    /// <summary>An account that is never reached. The v3 client constructor does
    /// no IO, so a replica can resolve its container against this and never send
    /// a byte.</summary>
    private const string UnreachableAccount = "https://localhost:8081/";

    private static readonly string PlaceholderKey = Convert.ToBase64String(new byte[64]);

    private static readonly OwnerId Owner = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));

    /// <summary>
    /// The failure has to be retried, not remembered. Lazy construction is here
    /// so that a service whose store is not up yet answers 503 on the call that
    /// needed it rather than refusing to start; a mode that cached the exception
    /// would answer 503 on every call for the life of the process instead, which
    /// is worse than not starting — Container Apps restarts a container that
    /// died, and never one that is answering.
    /// </summary>
    [Fact]
    public void A_client_that_failed_to_build_is_built_again_on_the_next_call()
    {
        var attempts = 0;

        var services = new ServiceCollection();
        services.AddSingleton(_ =>
            ++attempts == 1
                ? throw new InvalidOperationException("The store is not up yet.")
                : new CosmosClient(UnreachableAccount, PlaceholderKey));

        using var provider = services.BuildServiceProvider();
        var replica = Replica(provider);

        Assert.Throws<SyncReplicaException>(replica.Container);

        Assert.NotNull(replica.Container());
        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// <c>PageSizeHint</c> is a hint: Cosmos returns fewer items when the
    /// response nears its size limit, so a short page and a drained feed look
    /// identical from the item count. Only the 304 says the feed is caught up,
    /// and a device bootstrapping a large backlog stops at the first short page
    /// if anything else is allowed to.
    /// </summary>
    [Fact]
    public void A_short_page_is_not_a_drained_feed()
    {
        var page = CosmosTaskReplica.Page([Document(), Document()], Owner, "continue-here", drained: false);

        Assert.True(page.HasMore);
        Assert.Equal("continue-here", page.Cursor.Continuation);
    }

    /// <summary>The store's own "nothing since your cursor" answer, and the only
    /// thing that ends the loop.</summary>
    [Fact]
    public void A_drained_feed_is_the_one_thing_that_says_there_is_no_more()
    {
        var page = CosmosTaskReplica.Page([], Owner, "continue-here", drained: true);

        Assert.False(page.HasMore);

        // A cursor even so: the feed's position advances whether or not anything
        // was there, and a client that kept its old one would rescan for ever.
        Assert.Equal("continue-here", page.Cursor.Continuation);
    }

    /// <summary>
    /// The page a foreign document forces, and what it must not carry. The
    /// continuation belongs to the feed range the foreign documents came from, so
    /// signing it for this caller would pin them to a range they can never read —
    /// silently, and for as long as they keep sending it back. No cursor means
    /// the next pull starts from the beginning, which is the safe direction to
    /// fail in.
    /// </summary>
    [Fact]
    public void A_discarded_page_hands_back_no_continuation()
    {
        var discarded = CosmosTaskReplica.Discarded(Owner);

        Assert.Empty(discarded.Changes);
        Assert.False(discarded.HasMore);
        Assert.Empty(discarded.Cursor.Continuation);
    }

    /// <summary>
    /// A document over Cosmos's two-megabyte ceiling. Retrying cannot help, so it
    /// needs a code of its own: left as an unclassified 500 it would fail the
    /// batch carrying it on every run for ever, and the push watermark would
    /// never advance past it again.
    /// </summary>
    [Fact]
    public void A_document_the_store_will_not_take_has_its_own_code()
    {
        var fault = CosmosTaskReplica.Fault(Cosmos(HttpStatusCode.RequestEntityTooLarge));

        Assert.Equal(SyncErrorCodes.TaskTooLarge, fault.Code);
    }

    /// <summary>Throttling that outlived the SDK's own retries. The caller comes
    /// back rather than changes anything, which is a different sentence from "the
    /// store is not up yet" and a different status.</summary>
    [Fact]
    public void Throttling_that_outlived_the_sdks_retries_has_its_own_code()
    {
        var fault = CosmosTaskReplica.Fault(Cosmos(HttpStatusCode.TooManyRequests));

        Assert.Equal(SyncErrorCodes.ReplicaBusy, fault.Code);
    }

    /// <summary>Everything else is the store not being reachable, which is the
    /// ordinary state of the first minutes of a local run.</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Anything_else_is_the_replica_being_unavailable(HttpStatusCode status)
    {
        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, CosmosTaskReplica.Fault(Cosmos(status)).Code);
    }

    private static CosmosTaskReplica Replica(IServiceProvider provider) => new(
        provider,
        Options.Create(new CosmosOptions()),
        NullLogger<CosmosTaskReplica>.Instance);

    /// <summary>The least a payload can be. Nothing here is read by the two
    /// things these cases are about — the page's own claims, and how a Cosmos
    /// failure is classified — so filling it in would suggest they were.</summary>
    private static TaskPayload Payload() => new(
        "Call the dentist",
        ContentMd: string.Empty,
        Type: "task",
        Status: "draft",
        Priority: "medium",
        Order: 0,
        Area: null,
        DateTimeOffset.UnixEpoch,
        SourceInboxId: null,
        RecurrenceSourceId: null,
        DueOn: null,
        RemindAt: null,
        Recurrence: null,
        InMyDayOn: null,
        View: null,
        Effort: null,
        ImportPlanId: null,
        ImportItemId: null,
        AttachmentPath: null,
        Tags: [],
        RepoIds: [],
        DependsOn: [],
        SubItems: [],
        UsageEvents: [],
        ProjectionRefs: []);

    private static CosmosException Cosmos(HttpStatusCode status) =>
        new("The store said no.", status, subStatusCode: 0, activityId: "test", requestCharge: 0);

    private static TaskDocument Document() => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        OwnerId = Owner.Value.ToString("D"),
        DeviceId = Guid.NewGuid().ToString("D"),
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Task = Payload(),
    };
}
