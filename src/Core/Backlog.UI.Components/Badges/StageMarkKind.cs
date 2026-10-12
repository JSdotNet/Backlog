namespace Backlog.UI.Components.Badges;

/// <summary>The mark a stage of a flow carries after its chips.</summary>
public enum StageMarkKind
{
    /// <summary>No mark: the stage ran as configured, with every value recorded.</summary>
    None,

    /// <summary>≠ — the stage ran other than as configured.</summary>
    Drift,

    /// <summary>? — a value on the stage was inferred, not recorded.</summary>
    Inferred
}
