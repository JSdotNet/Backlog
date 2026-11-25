namespace Backlog.SharedKernel.Changes;

/// <summary>Who made a change set (local ADR 0061 §2). <see cref="Unknown"/> is a
/// write no entry point named: another process, or a path not yet wired.</summary>
public enum ChangeActor
{
    Unknown,
    Person,
    Agent,
    Sync,
    System,
}

/// <summary>
/// One change set of the change journal (local ADR 0061 §2): the writes one
/// gesture, one applied sync page, one background pass or one agent run made,
/// taken back together.
/// <para>
/// <see cref="Caller"/>, <see cref="SessionId"/> and <see cref="CorrelationId"/>
/// name an MCP write's grant, session and first call (§3); every other actor
/// leaves them null.
/// </para>
/// </summary>
public sealed record ChangeSet(Guid Id, ChangeActor Actor, string? Label, DateTimeOffset StartedAt)
{
    public string? Caller { get; init; }

    public string? SessionId { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>The change set that restored this one (local ADR 0061 §4), or null
    /// while it stands.</summary>
    public Guid? UndoneBy { get; init; }

    /// <summary>A new change set, stamped now on <paramref name="time"/>.</summary>
    public static ChangeSet Start(ChangeActor actor, string? label = null, TimeProvider? time = null) =>
        new(Guid.CreateVersion7(), actor, label, (time ?? TimeProvider.System).GetUtcNow());

    /// <summary>The actor as the journal stores it.</summary>
    public static string ToWire(ChangeActor actor) => actor switch
    {
        ChangeActor.Person => "person",
        ChangeActor.Agent => "agent",
        ChangeActor.Sync => "sync",
        ChangeActor.System => "system",
        _ => "unknown",
    };

    /// <summary>The actor <see cref="ToWire"/> stored; anything else is
    /// <see cref="ChangeActor.Unknown"/>.</summary>
    public static ChangeActor FromWire(string? wire) => wire switch
    {
        "person" => ChangeActor.Person,
        "agent" => ChangeActor.Agent,
        "sync" => ChangeActor.Sync,
        "system" => ChangeActor.System,
        _ => ChangeActor.Unknown,
    };
}
