using Backlog.SharedKernel.Changes;

namespace Backlog.UI.Components.Changes;

/// <summary>Which change sets the Changes view lists, by who made them. A
/// <see cref="ChangeActor.System"/> change set shows under <see cref="All"/> only.</summary>
public enum ChangeSetFilter
{
    All,
    Agents,
    Me,
    Sync,
    Unknown,
}

/// <summary>What each <see cref="ChangeSetFilter"/> is called and keeps.</summary>
public static class ChangeSetFilters
{
    /// <summary>Every filter, in the order its chip is drawn.</summary>
    public static IReadOnlyList<ChangeSetFilter> All { get; } =
        [ChangeSetFilter.All, ChangeSetFilter.Agents, ChangeSetFilter.Me, ChangeSetFilter.Sync, ChangeSetFilter.Unknown];

    /// <summary>The chip's word.</summary>
    public static string Label(ChangeSetFilter filter) => filter switch
    {
        ChangeSetFilter.Agents => "Agents",
        ChangeSetFilter.Me => "Me",
        ChangeSetFilter.Sync => "Sync",
        ChangeSetFilter.Unknown => "Unknown",
        _ => "All",
    };

    /// <summary>Whether <paramref name="filter"/> keeps a change set <paramref name="actor"/> made.</summary>
    public static bool Keeps(ChangeSetFilter filter, ChangeActor actor) => filter switch
    {
        ChangeSetFilter.Agents => actor is ChangeActor.Agent,
        ChangeSetFilter.Me => actor is ChangeActor.Person,
        ChangeSetFilter.Sync => actor is ChangeActor.Sync,
        ChangeSetFilter.Unknown => actor is ChangeActor.Unknown,
        _ => true,
    };

    /// <summary>Who made a change set, in words.</summary>
    public static string ActorLabel(ChangeActor actor) => actor switch
    {
        ChangeActor.Person => "You",
        ChangeActor.Agent => "Agent",
        ChangeActor.Sync => "Sync",
        ChangeActor.System => "Backlog",
        _ => "Unknown writer",
    };

    /// <summary>The badge tone an actor wears; the word carries the meaning.</summary>
    public static string ActorTone(ChangeActor actor) => actor switch
    {
        ChangeActor.Person => "settled",
        ChangeActor.Agent => "live",
        ChangeActor.Unknown => "alert",
        _ => "quiet",
    };
}
