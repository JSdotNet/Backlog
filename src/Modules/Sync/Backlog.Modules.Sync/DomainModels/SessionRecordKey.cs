using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// How one machine's copy of one session is addressed in either replica:
/// <c>{machineId}:{agentKind}:{sessionId}</c>.
/// <para>
/// It lives in the module rather than in the Cosmos adapter because it states a
/// rule about identity rather than one about storage, and because both adapters
/// have to key the same way — otherwise the in-memory stand-in and Cosmos would
/// disagree about what re-pushing a session does, and every endpoint test would
/// be exercising the wrong answer.
/// </para>
/// <para>
/// <strong>The agent is in the key because a session id is not an identity on
/// its own.</strong> .domain/sessions/naming.md#session-identity puts a session's
/// identity at the agent plus the identifier that agent issued, both halves
/// always: an id is unique only within its own agent, so keying on the id alone
/// would let two unrelated sessions collapse into one record and show one row
/// where there were two.
/// </para>
/// <para>
/// <strong>The machine id leads it because that turns .arc42/adr/0005's
/// single-writer rule into a structural property.</strong> A machine can only
/// ever address keys beginning with its own device id, so no machine can
/// overwrite another's record even if the stamping above it were wrong. That is
/// the difference between an invariant and a convention, and it is the second of
/// the two things standing behind §Session records' "a caller may only write
/// records stamped with its own machine id" — the first being that a
/// <see cref="SessionRecord"/> has nowhere to put a machine id at all.
/// </para>
/// </summary>
public static class SessionRecordKey
{
    /// <summary>
    /// The separator. A colon because none of the three parts may contain one: a
    /// GUID in <c>"D"</c> format cannot, and an agent kind or session id that did
    /// would be refused at the edge before it reached a key — which is why the
    /// bound belongs there and not in an escaping scheme here.
    /// </summary>
    public const string Separator = ":";

    /// <summary>The key for one machine's copy of one session.</summary>
    public static string For(Guid machineId, SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return string.Join(Separator, machineId.ToString("D"), record.AgentKind, record.SessionId);
    }
}
