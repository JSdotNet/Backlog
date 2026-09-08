using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// The one JSON contract for the account — both the <c>tasks</c> container and
/// the <c>sessions</c> container beside it.
/// <para>
/// It is a contract rather than a formatting preference. The camelCase policy is
/// what turns <c>OwnerId</c> into <c>ownerId</c>, and <c>/ownerId</c> is the
/// partition key path declared in the AppHost and in
/// <c>infra/sync/main.bicep</c> for both containers: change the policy and every
/// document lands in the same undefined partition, with no error anywhere to say
/// so. The <c>sessions</c> container raises the stake — its indexing policy
/// includes exactly <c>/ownerId/?</c>, <c>/machineId/?</c>,
/// <c>/repositoryAlias/?</c>, <c>/startedAt/?</c> and <c>/lastActivityAt/?</c>
/// and excludes everything else, so a renaming policy would leave an index over
/// five paths no document has.
/// </para>
/// <para>
/// That is why there is one options instance, why the document types spell out
/// the reserved names they cannot express through a policy, and why the Cosmos
/// client is configured with <c>UseSystemTextJsonSerializerWithOptions</c> — the
/// SDK serialises with Newtonsoft by default and would ignore every attribute on
/// those classes.
/// </para>
/// <para>
/// One type for both containers rather than one apiece. The SDK is configured
/// with a single serializer for the whole client, so a second options instance
/// could not be honoured for one container and would only be a second place for
/// the policy to drift — visibly wrong here, and silently wrong in the container
/// that stopped matching its own index.
/// </para>
/// <para>
/// Nulls are dropped on write, which the task TTL depends on: an absent
/// <c>ttl</c> means "keep forever", and a written <c>null</c> would not. Session
/// documents carry no <c>ttl</c> at all — their retention is a container
/// setting — so for them this only means an unknown branch or repository is
/// absent rather than present-and-null.
/// </para>
/// </summary>
internal static class ReplicaDocumentSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The text form of an id, for <c>id</c>, <c>ownerId</c>,
    /// <c>deviceId</c> and <c>machineId</c> alike. <c>"D"</c> because that is
    /// what the local SQLite store already writes, so the same task or machine
    /// has one spelling everywhere and no layer has to normalise another's.</summary>
    public static string Key(Guid value) => value.ToString("D");
}
