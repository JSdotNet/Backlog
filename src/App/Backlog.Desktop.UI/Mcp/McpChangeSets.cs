using Backlog.SharedKernel.Changes;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The change sets MCP writes are grouped into (local ADR 0061 §3): one per caller and
/// session, or per caller when a call names no session, kept open while each write comes
/// within <see cref="Window"/> of the one before it.
/// <para>
/// One per host, a singleton, because the grouping outlives the request: the transport is
/// stateless, and every call builds a fresh scope. Kept in memory only — a restart starts a
/// new change set, which is the honest answer for writes on either side of it.
/// </para>
/// </summary>
public sealed class McpChangeSets(TimeProvider? time = null)
{
    /// <summary>How long after its last write a change set still takes the next one.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OpenSet> _open = new(StringComparer.Ordinal);

    /// <summary>
    /// The change set a write by <paramref name="caller"/> in <paramref name="sessionId"/>
    /// would join, or null when it would open a new one: none is open for the key, or the last
    /// write was more than <see cref="Window"/> ago. A look only: it moves nothing, so a caller
    /// can skip naming a change set it will not open.
    /// </summary>
    public ChangeSet? Peek(string caller, string? sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        var key = Key(caller, sessionId);
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            return _open.TryGetValue(key, out var open) && now - open.LastWriteAt <= Window ? open.ChangeSet : null;
        }
    }

    /// <summary>
    /// The change set a write joins, moving its window on, or, when none is open for its key
    /// within <see cref="Window"/>, the one it opens: actor agent, the caller and session it
    /// came from, the correlation id of its audit record, and <paramref name="label"/>. Checked
    /// and opened under one lock, so two first writes sent at once share one change set.
    /// </summary>
    public ChangeSet JoinOrOpen(string caller, string? sessionId, string correlationId, string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var session = Normalize(sessionId);
        var key = Key(caller, session);
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_open.TryGetValue(key, out var open) && now - open.LastWriteAt <= Window)
            {
                _open[key] = open with { LastWriteAt = now };
                return open.ChangeSet;
            }

            var changeSet = ChangeSet.Start(ChangeActor.Agent, label, _time) with
            {
                Caller = caller,
                SessionId = session,
                CorrelationId = correlationId,
            };

            Prune(now);
            _open[key] = new OpenSet(changeSet, now);
            return changeSet;
        }
    }

    /// <summary>The caller always, and its session when it names one: a change set is one
    /// principal's (local ADR 0061 §3), so a session id another caller passes — its own
    /// session's, or one it acts on, as <c>mark_session_request</c> does — never files its
    /// writes under the first caller's name.</summary>
    private static string Key(string caller, string? sessionId) =>
        Normalize(sessionId) is { } session ? $"{caller}\u001f{session}" : caller;

    private static string? Normalize(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();

    // Drops the change sets no write can join any more, so a long-running app does not keep
    // one per session it has ever seen.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, open) in _open.ToList())
        {
            if (now - open.LastWriteAt > Window) _open.Remove(key);
        }
    }

    private sealed record OpenSet(ChangeSet ChangeSet, DateTimeOffset LastWriteAt);
}
