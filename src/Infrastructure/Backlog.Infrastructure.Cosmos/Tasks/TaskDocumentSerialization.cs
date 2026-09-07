using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Cosmos.Tasks;

/// <summary>
/// The one JSON contract for the <c>tasks</c> container.
/// <para>
/// It is a contract rather than a formatting preference. The camelCase policy is
/// what turns <c>OwnerId</c> into <c>ownerId</c>, and <c>/ownerId</c> is the
/// partition key path declared in the AppHost and in
/// <c>infra/sync/main.bicep</c>: change the policy and every document lands in
/// the same undefined partition, with no error anywhere to say so. That is why
/// there is one options instance, why <see cref="TaskDocument"/> spells out the
/// reserved names it cannot express through a policy, and why the Cosmos client
/// is configured with <c>UseSystemTextJsonSerializerWithOptions</c> — the SDK
/// serialises with Newtonsoft by default and would ignore every attribute on
/// that class.
/// </para>
/// <para>
/// Nulls are dropped on write, which the TTL depends on: an absent <c>ttl</c>
/// means "keep forever", and a written <c>null</c> would not.
/// </para>
/// </summary>
internal static class TaskDocumentSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The text form of an id, for both <c>id</c> and <c>ownerId</c>.
    /// <c>"D"</c> because that is what the local SQLite store already writes, so
    /// the same task has one spelling everywhere and no layer has to normalise
    /// another's.</summary>
    public static string Key(Guid value) => value.ToString("D");
}
