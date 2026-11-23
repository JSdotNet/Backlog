// The SDK's Container is abstract with no in-memory implementation, so the members no
// connector credential store calls throw. Nullability is off for the file: the overrides
// mirror the SDK's unannotated signatures.
#nullable disable

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// One Cosmos container held in memory, with the etag semantics the connector credential
/// stores lean on — every write issues a new etag, a replace whose <c>IfMatchEtag</c> is
/// stale is a 412, a create over an existing id is a 409 — and a script that fails any
/// call. A scripted fault either refuses the call or, with <see cref="ScriptedFault.Applied"/>,
/// applies it and then throws: the timeout that leaves the caller not knowing.
/// </summary>
internal sealed class ScriptedContainer : Container
{
    private readonly Dictionary<string, JsonObject> _documents = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    /// <summary>Asked before every item call with the operation and the id; a fault it
    /// returns is thrown, after the write when <see cref="ScriptedFault.Applied"/>.</summary>
    public Func<string, string, ScriptedFault> Script { get; set; } = (_, _) => null;

    public bool Contains(string id)
    {
        lock (_sync)
        {
            return _documents.ContainsKey(id);
        }
    }

    public T Get<T>(string id)
    {
        lock (_sync)
        {
            return _documents.TryGetValue(id, out var node)
                ? node.Deserialize<T>(ReplicaDocumentSerialization.Options)
                : default;
        }
    }

    /// <summary>Stores <paramref name="item"/> as if written, bypassing the script.</summary>
    public void Seed<T>(string id, T item)
    {
        lock (_sync)
        {
            _documents[id] = Stamped(item);
        }
    }

    public override string Id => "connectorCredentials";

    public override Task<ItemResponse<T>> ReadItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Before("read", id);

        lock (_sync)
        {
            if (!_documents.TryGetValue(id, out var node))
            {
                throw Status(HttpStatusCode.NotFound);
            }

            return Task.FromResult<ItemResponse<T>>(new ScriptedResponse<T>(node.Deserialize<T>(ReplicaDocumentSerialization.Options)));
        }
    }

    public override Task<ItemResponse<T>> CreateItemAsync<T>(T item, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) =>
        Write("create", IdOf(item), cancellationToken, () =>
        {
            if (_documents.ContainsKey(IdOf(item)))
            {
                throw Status(HttpStatusCode.Conflict);
            }

            _documents[IdOf(item)] = Stamped(item);
            return item;
        });

    public override Task<ItemResponse<T>> UpsertItemAsync<T>(T item, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) =>
        Write("upsert", IdOf(item), cancellationToken, () =>
        {
            _documents[IdOf(item)] = Stamped(item);
            return item;
        });

    public override Task<ItemResponse<T>> ReplaceItemAsync<T>(T item, string id, PartitionKey? partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) =>
        Write("replace", id, cancellationToken, () =>
        {
            if (!_documents.TryGetValue(id, out var current))
            {
                throw Status(HttpStatusCode.NotFound);
            }

            if (requestOptions?.IfMatchEtag is { } etag && etag != (string)current["_etag"])
            {
                throw Status(HttpStatusCode.PreconditionFailed);
            }

            _documents[id] = Stamped(item);
            return item;
        });

    public override Task<ItemResponse<T>> PatchItemAsync<T>(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions, CancellationToken cancellationToken) =>
        Write<T>("patch", id, cancellationToken, () =>
        {
            if (!_documents.TryGetValue(id, out var current))
            {
                throw Status(HttpStatusCode.NotFound);
            }

            foreach (var operation in patchOperations)
            {
                if (operation is PatchOperation<bool> set)
                {
                    current[operation.Path.TrimStart('/')] = set.Value;
                }
            }

            current["_etag"] = Guid.NewGuid().ToString();
            return default;
        });

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions, CancellationToken cancellationToken) =>
        Write<T>("delete", id, cancellationToken, () =>
        {
            if (!_documents.Remove(id))
            {
                throw Status(HttpStatusCode.NotFound);
            }

            return default;
        });

    public static CosmosException Status(HttpStatusCode status) =>
        new($"Scripted {(int)status}.", status, 0, string.Empty, 0);

    private Task<ItemResponse<T>> Write<T>(string operation, string id, CancellationToken cancellationToken, Func<T> apply)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fault = Before(operation, id);

        T written;
        lock (_sync)
        {
            written = apply();
        }

        if (fault is { Applied: true })
        {
            throw Status(fault.Status);
        }

        return Task.FromResult<ItemResponse<T>>(new ScriptedResponse<T>(written));
    }

    private ScriptedFault Before(string operation, string id)
    {
        var fault = Script(operation, id);

        if (fault is { Applied: false })
        {
            throw Status(fault.Status);
        }

        return fault;
    }

    private static string IdOf<T>(T item) =>
        (string)JsonSerializer.SerializeToNode(item, ReplicaDocumentSerialization.Options)!["id"];

    private static JsonObject Stamped<T>(T item)
    {
        var node = JsonSerializer.SerializeToNode(item, ReplicaDocumentSerialization.Options)!.AsObject();
        node["_etag"] = Guid.NewGuid().ToString();
        return node;
    }

    private sealed class ScriptedResponse<T>(T resource) : ItemResponse<T>
    {
        public override T Resource => resource;

        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    }

    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ContainerResponse> ReadContainerAsync(global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> ReadContainerStreamAsync(global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ContainerResponse> ReplaceContainerAsync(global::Microsoft.Azure.Cosmos.ContainerProperties containerProperties, global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> ReplaceContainerStreamAsync(global::Microsoft.Azure.Cosmos.ContainerProperties containerProperties, global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ContainerResponse> DeleteContainerAsync(global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> DeleteContainerStreamAsync(global::Microsoft.Azure.Cosmos.ContainerRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::System.Nullable<global::System.Int32>> ReadThroughputAsync(global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ThroughputResponse> ReadThroughputAsync(global::Microsoft.Azure.Cosmos.RequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ThroughputResponse> ReplaceThroughputAsync(global::System.Int32 throughput, global::Microsoft.Azure.Cosmos.RequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ThroughputResponse> ReplaceThroughputAsync(global::Microsoft.Azure.Cosmos.ThroughputProperties throughputProperties, global::Microsoft.Azure.Cosmos.RequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> CreateItemStreamAsync(global::System.IO.Stream streamPayload, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::Microsoft.Azure.Cosmos.ItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> ReadItemStreamAsync(global::System.String id, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::Microsoft.Azure.Cosmos.ItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> UpsertItemStreamAsync(global::System.IO.Stream streamPayload, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::Microsoft.Azure.Cosmos.ItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> ReplaceItemStreamAsync(global::System.IO.Stream streamPayload, global::System.String id, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::Microsoft.Azure.Cosmos.ItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> ReadManyItemsStreamAsync(global::System.Collections.Generic.IReadOnlyList<global::System.ValueTuple<global::System.String, global::Microsoft.Azure.Cosmos.PartitionKey>> items, global::Microsoft.Azure.Cosmos.ReadManyRequestOptions readManyRequestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.FeedResponse<T>> ReadManyItemsAsync<T>(global::System.Collections.Generic.IReadOnlyList<global::System.ValueTuple<global::System.String, global::Microsoft.Azure.Cosmos.PartitionKey>> items, global::Microsoft.Azure.Cosmos.ReadManyRequestOptions readManyRequestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> PatchItemStreamAsync(global::System.String id, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::System.Collections.Generic.IReadOnlyList<global::Microsoft.Azure.Cosmos.PatchOperation> patchOperations, global::Microsoft.Azure.Cosmos.PatchItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::Microsoft.Azure.Cosmos.ResponseMessage> DeleteItemStreamAsync(global::System.String id, global::Microsoft.Azure.Cosmos.PartitionKey partitionKey, global::Microsoft.Azure.Cosmos.ItemRequestOptions requestOptions, global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator GetItemQueryStreamIterator(global::Microsoft.Azure.Cosmos.QueryDefinition queryDefinition, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator<T> GetItemQueryIterator<T>(global::Microsoft.Azure.Cosmos.QueryDefinition queryDefinition, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator GetItemQueryStreamIterator(global::System.String queryText, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator<T> GetItemQueryIterator<T>(global::System.String queryText, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator GetItemQueryStreamIterator(global::Microsoft.Azure.Cosmos.FeedRange feedRange, global::Microsoft.Azure.Cosmos.QueryDefinition queryDefinition, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator<T> GetItemQueryIterator<T>(global::Microsoft.Azure.Cosmos.FeedRange feedRange, global::Microsoft.Azure.Cosmos.QueryDefinition queryDefinition, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions) => throw new NotSupportedException();
    public override global::System.Linq.IOrderedQueryable<T> GetItemLinqQueryable<T>(global::System.Boolean allowSynchronousQueryExecution, global::System.String continuationToken, global::Microsoft.Azure.Cosmos.QueryRequestOptions requestOptions, global::Microsoft.Azure.Cosmos.CosmosLinqSerializerOptions linqSerializerOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangesHandler<T> onChangesDelegate) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangesEstimationHandler estimationDelegate, global::System.Nullable<global::System.TimeSpan> estimationPeriod) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedEstimator GetChangeFeedEstimator(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container leaseContainer) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.TransactionalBatch CreateTransactionalBatch(global::Microsoft.Azure.Cosmos.PartitionKey partitionKey) => throw new NotSupportedException();
    public override global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::Microsoft.Azure.Cosmos.FeedRange>> GetFeedRangesAsync(global::System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator GetChangeFeedStreamIterator(global::Microsoft.Azure.Cosmos.ChangeFeedStartFrom changeFeedStartFrom, global::Microsoft.Azure.Cosmos.ChangeFeedMode changeFeedMode, global::Microsoft.Azure.Cosmos.ChangeFeedRequestOptions changeFeedRequestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.FeedIterator<T> GetChangeFeedIterator<T>(global::Microsoft.Azure.Cosmos.ChangeFeedStartFrom changeFeedStartFrom, global::Microsoft.Azure.Cosmos.ChangeFeedMode changeFeedMode, global::Microsoft.Azure.Cosmos.ChangeFeedRequestOptions changeFeedRequestOptions) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangeFeedHandler<T> onChangesDelegate) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangeFeedStreamHandler onChangesDelegate) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(global::System.String processorName, global::Microsoft.Azure.Cosmos.Container.ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate) => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.Database Database => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.Conflicts Conflicts => throw new NotSupportedException();
    public override global::Microsoft.Azure.Cosmos.Scripts.Scripts Scripts => throw new NotSupportedException();
}

/// <summary>A fault <see cref="ScriptedContainer"/> throws: <paramref name="Status"/>, and
/// whether the call was <paramref name="Applied"/> before it was thrown.</summary>
internal sealed record ScriptedFault(HttpStatusCode Status, bool Applied = false);

/// <summary>A client whose account always answers and whose every container is the one
/// <see cref="ScriptedContainer"/>. The SDK's protected constructor is there for this.</summary>
internal sealed class ScriptedCosmosClient(ScriptedContainer container) : CosmosClient
{
    public override Task<AccountProperties> ReadAccountAsync() => Task.FromResult<AccountProperties>(null);

    public override Container GetContainer(string databaseId, string containerId) => container;
}
