namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// The feature key Events owns (<c>.devbook/arc42/08-crosscutting-concepts.md#feature-enablement</c>,
/// local ADR 0050 §6).
/// <para>
/// One key, per device like every flag: off, the whole Events area is withdrawn and nothing of
/// it shows. Its status — Dev — is authored in the settings catalog with every other feature's.
/// The value is persisted in each device's feature settings, so it never changes.
/// </para>
/// </summary>
public static class EventsFeatures
{
    /// <summary>Keep the knowledge events the person attends to learn something.</summary>
    public const string Events = "events";
}
