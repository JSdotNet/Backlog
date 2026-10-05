namespace Backlog.UI.Components.Integrations;

/// <summary>
/// One thing a person connected to an outside tool, as a <c>ConnectorCard</c>
/// draws it: what it is called, whether it syncs, and when it last did.
/// </summary>
/// <param name="Name">What was connected, in the tool's own spelling.</param>
/// <param name="Enabled">Whether it syncs.</param>
/// <param name="LastSynced">Already formatted, or null before the first sync. How a
/// time reads, and in what language, is the host's — the division
/// <see cref="IntegrationReading"/> makes.</param>
public sealed record ConnectorTargetRow(string Name, bool Enabled, string? LastSynced = null);
