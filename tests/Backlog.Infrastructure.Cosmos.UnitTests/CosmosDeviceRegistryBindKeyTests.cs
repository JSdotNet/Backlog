using System.Net;
using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Devices;
using Backlog.Modules.Sync.DomainModels;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// How the device registry binds a key in a credential's place (local ADR 0058 §2):
/// a point read, then a replace conditional on the etag that read carried. The
/// container under it keeps documents and etags the way Cosmos does — a replace whose
/// etag no longer matches is a 412 — so what is pinned is that the adapter sends the
/// etag and reads a lost race as false, not that a fake was written to agree with it.
/// </summary>
public class CosmosDeviceRegistryBindKeyTests
{
    private const string Key = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE-bound-key";
    private const string OtherKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE-other-key";

    private static readonly OwnerId Owner = new(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Binding_stores_the_key_and_clears_the_credential_hash()
    {
        var device = CredentialDevice();
        var container = new EtagContainer();
        container.Seed(DeviceDocumentFactory.From(device));

        Assert.True(await Registry(container).BindKey(device.Id, Key, Cancellation));

        var stored = container.Stored(device.Id);
        Assert.Equal(Key, stored.PublicKey);
        Assert.Null(stored.CredentialHash);
        Assert.Equal(device.Name, stored.Name);
    }

    [Fact]
    public async Task A_second_binding_is_refused_and_keeps_the_first_key()
    {
        var device = CredentialDevice();
        var container = new EtagContainer();
        container.Seed(DeviceDocumentFactory.From(device));
        var registry = Registry(container);

        Assert.True(await registry.BindKey(device.Id, Key, Cancellation));
        Assert.False(await registry.BindKey(device.Id, OtherKey, Cancellation));

        Assert.Equal(Key, container.Stored(device.Id).PublicKey);
    }

    [Fact]
    public async Task An_unknown_device_binds_nothing()
    {
        Assert.False(await Registry(new EtagContainer()).BindKey(DeviceId.New(), Key, Cancellation));
    }

    /// <summary>Both read the device unbound before either writes. Without the etag
    /// condition both replaces would land and the second key would overwrite the first.</summary>
    [Fact]
    public async Task Of_two_bindings_racing_exactly_one_wins()
    {
        var device = CredentialDevice();
        var container = new EtagContainer(readsToHold: 2);
        container.Seed(DeviceDocumentFactory.From(device));
        var registry = Registry(container);

        var results = await Task.WhenAll(
            registry.BindKey(device.Id, Key, Cancellation),
            registry.BindKey(device.Id, OtherKey, Cancellation));

        Assert.Single(results, bound => bound);
        Assert.Equal(results[0] ? Key : OtherKey, container.Stored(device.Id).PublicKey);
        Assert.Null(container.Stored(device.Id).CredentialHash);
    }

    private static Device CredentialDevice() => new(
        DeviceId.New(),
        Owner,
        "Study desktop",
        "ab" + new string('0', 62),
        new DateTimeOffset(2026, 9, 14, 8, 15, 0, TimeSpan.Zero));

    private static CosmosDeviceRegistry Registry(Container container)
    {
        var options = Options.Create(new CosmosOptions());
        var services = new ServiceCollection()
            .AddSingleton<CosmosClient>(new OneContainerClient(container))
            .AddSingleton(provider => new CosmosAccountGate(provider, options, NullLogger<CosmosAccountGate>.Instance))
            .BuildServiceProvider();

        return new CosmosDeviceRegistry(services, options);
    }

    /// <summary>A client whose account always answers and whose every container is
    /// the one handed in. The SDK's protected constructor is there for this.</summary>
    private sealed class OneContainerClient(Container container) : CosmosClient
    {
        public override Task<AccountProperties> ReadAccountAsync() => Task.FromResult<AccountProperties>(null!);

        public override Container GetContainer(string databaseId, string id) => container;
    }

    private sealed class Response<T>(T resource, string etag) : ItemResponse<T>
    {
        public override T Resource => resource;

        public override string ETag => etag;

        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    }

    /// <summary>
    /// Device documents by id, each with an etag that changes on every replace, and a
    /// replace whose <c>IfMatchEtag</c> is stale refused with 412 — the two behaviours
    /// of Cosmos the binding leans on. Documents go in and out as JSON copies, so the
    /// adapter cannot change what is stored by mutating what it read.
    /// <paramref name="readsToHold"/> holds that many reads until all have arrived,
    /// which is how two bindings are made to race.
    /// </summary>
    private sealed class EtagContainer(int readsToHold = 0) : Container
    {
        private static readonly Exception Unused = new NotSupportedException("The device registry's binding does not call this.");

        private readonly Lock _sync = new();
        private readonly Dictionary<string, (string Json, string ETag)> _documents = [];
        private readonly TaskCompletionSource _allRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        private int _version;

        public void Seed(DeviceDocument document)
        {
            lock (_sync)
            {
                _documents[document.Id] = (Serialize(document), NextETag());
            }
        }

        public DeviceDocument Stored(DeviceId id)
        {
            lock (_sync)
            {
                return Deserialize<DeviceDocument>(_documents[ReplicaDocumentSerialization.Key(id.Value)].Json);
            }
        }

        public override async Task<ItemResponse<T>> ReadItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken)
        {
            (string Json, string ETag) found;
            bool known;

            lock (_sync)
            {
                known = _documents.TryGetValue(id, out found);
            }

            var read = Interlocked.Increment(ref _reads);
            if (read <= readsToHold)
            {
                if (read == readsToHold)
                {
                    _allRead.SetResult();
                }

                await _allRead.Task.WaitAsync(cancellationToken);
            }

            return known
                ? new Response<T>(Deserialize<T>(found.Json), found.ETag)
                : throw Status(HttpStatusCode.NotFound);
        }

        public override Task<ItemResponse<T>> ReplaceItemAsync<T>(T item, string id, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_documents.TryGetValue(id, out var current))
                {
                    throw Status(HttpStatusCode.NotFound);
                }

                if (requestOptions?.IfMatchEtag is { } expected && expected != current.ETag)
                {
                    throw Status(HttpStatusCode.PreconditionFailed);
                }

                var etag = NextETag();
                _documents[id] = (Serialize(item), etag);
                return Task.FromResult<ItemResponse<T>>(new Response<T>(item, etag));
            }
        }

        private string NextETag() => $"\"etag-{++_version}\"";

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ReplicaDocumentSerialization.Options);

        private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, ReplicaDocumentSerialization.Options)!;

        private static CosmosException Status(HttpStatusCode status) =>
            new("The store said no.", status, subStatusCode: 0, activityId: "test", requestCharge: 0);

        // Everything below is the rest of Container, which the binding never reaches.
        public override Task<ItemResponse<T>> CreateItemAsync<T>(T item, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> CreateItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey) => throw Unused;
        public override Task<ContainerResponse> DeleteContainerAsync(ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> DeleteContainerStreamAsync(ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ItemResponse<T>> DeleteItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> DeleteItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override ChangeFeedEstimator GetChangeFeedEstimator(string processorName, Container leaseContainer) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(string processorName, Container.ChangesEstimationHandler estimationDelegate, TimeSpan? estimationPeriod) => throw Unused;
        public override FeedIterator<T> GetChangeFeedIterator<T>(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, Container.ChangesHandler<T> onChangesDelegate) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, Container.ChangeFeedHandler<T> onChangesDelegate) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(string processorName, Container.ChangeFeedStreamHandler onChangesDelegate) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(string processorName, Container.ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate) => throw Unused;
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(string processorName, Container.ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate) => throw Unused;
        public override FeedIterator GetChangeFeedStreamIterator(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions) => throw Unused;
        public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken) => throw Unused;
        public override IOrderedQueryable<T> GetItemLinqQueryable<T>(bool allowSynchronousQueryExecution, string continuationToken, QueryRequestOptions requestOptions, CosmosLinqSerializerOptions linqSerializerOptions) => throw Unused;
        public override FeedIterator<T> GetItemQueryIterator<T>(QueryDefinition queryDefinition, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override FeedIterator<T> GetItemQueryIterator<T>(string queryText, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override FeedIterator<T> GetItemQueryIterator<T>(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override FeedIterator GetItemQueryStreamIterator(QueryDefinition queryDefinition, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override FeedIterator GetItemQueryStreamIterator(string queryText, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override FeedIterator GetItemQueryStreamIterator(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken, QueryRequestOptions requestOptions) => throw Unused;
        public override Task<ItemResponse<T>> PatchItemAsync<T>(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> PatchItemStreamAsync(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ContainerResponse> ReadContainerAsync(ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> ReadContainerStreamAsync(ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> ReadItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(IReadOnlyList<(string, PartitionKey)> items, ReadManyRequestOptions readManyRequestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> ReadManyItemsStreamAsync(IReadOnlyList<(string, PartitionKey)> items, ReadManyRequestOptions readManyRequestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken) => throw Unused;
        public override Task<ThroughputResponse> ReadThroughputAsync(RequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ContainerResponse> ReplaceContainerAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> ReplaceContainerStreamAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> ReplaceItemStreamAsync(Stream streamPayload, string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ThroughputResponse> ReplaceThroughputAsync(int throughput, RequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ThroughputResponse> ReplaceThroughputAsync(ThroughputProperties throughputProperties, RequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ItemResponse<T>> UpsertItemAsync<T>(T item, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override Task<ResponseMessage> UpsertItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) => throw Unused;
        public override string Id => throw Unused;
        public override Database Database => throw Unused;
        public override Conflicts Conflicts => throw Unused;
        public override Microsoft.Azure.Cosmos.Scripts.Scripts Scripts => throw Unused;
    }
}
