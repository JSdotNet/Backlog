namespace Backlog.UI.Components.Integrations;

/// <summary>
/// One of a source's statuses on a <see cref="ConnectedTargetEditor"/> card, and the
/// task status its items become.
/// </summary>
/// <param name="Name">The status as the source names it.</param>
/// <param name="DefaultLabel">What the row's empty choice reads as — the status the
/// item becomes when nothing is mapped, already worded by the host, such as
/// "Default (Ready)".</param>
/// <param name="Mapped">The value of the <see cref="ConnectedTargetOptions.StatusChoices"/>
/// entry the status is mapped to, or null for the default.</param>
public sealed record SourceStatusRow(string Name, string DefaultLabel, string? Mapped = null);
