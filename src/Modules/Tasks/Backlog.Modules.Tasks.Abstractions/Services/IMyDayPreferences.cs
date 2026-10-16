namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The person's My Day preferences as a screen reads and sets them (local ADR 0032,
/// §2). Today one switch, Clear My Day overnight.
/// <para>
/// The same store answers <see cref="IMyDayPreferencesReplication"/>, so the screen
/// and the sync see one value. A host that keeps no store reads
/// <see cref="DefaultMyDayPreferences"/>.
/// </para>
/// </summary>
public interface IMyDayPreferences
{
    /// <summary>Whether My Day clears overnight: <c>true</c>, today's rule, until the
    /// person turns it off. Read by <see cref="MyDay.Includes"/>.</summary>
    bool ClearsOvernight { get; }

    /// <summary>
    /// Raised after the value changed — a person's change on this device, or a copy
    /// another device synced — so a list redraws under the new rule. Never for a set
    /// that changed nothing. It may arrive on any thread.
    /// </summary>
    event Action? Changed;

    /// <summary>Sets the switch. Returns <c>null</c> when it took and was saved, and a
    /// message to show beside the switch otherwise — a warning when it took but could
    /// not be written for next time, or a refusal when this host keeps no
    /// preferences.</summary>
    string? SetClearsOvernight(bool clearsOvernight);
}

/// <summary>
/// The preferences of a host that keeps none: My Day clears overnight, which is the
/// default and the rule from before the switch existed, and nothing changes it.
/// </summary>
public sealed class DefaultMyDayPreferences : IMyDayPreferences
{
    /// <summary>The one instance; it holds nothing.</summary>
    public static DefaultMyDayPreferences Instance { get; } = new();

    private DefaultMyDayPreferences()
    {
    }

    public bool ClearsOvernight => true;

    /// <summary>Never raised: the value never changes.</summary>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public string? SetClearsOvernight(bool clearsOvernight) =>
        "This device keeps no My Day preferences, so My Day clears overnight.";
}
