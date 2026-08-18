namespace Backlog.SharedKernel;

/// <summary>
/// A switchable part of the app. Most features ship on and are opt-out; a
/// feature that is not proven yet sets <paramref name="EnabledByDefault"/> to
/// false so nobody meets it without asking for it.
/// </summary>
public sealed record AppFeatureDefinition(
    string Key,
    string Name,
    string Description,
    bool AlwaysEnabled = false,
    bool EnabledByDefault = true);

/// <summary>
/// Which features have been switched away from their default. Two sets rather
/// than one, because "not mentioned" has to keep meaning "default" — otherwise
/// a default-off feature added after a settings file was written would silently
/// come on.
/// </summary>
public sealed class AppFeatureSettings
{
    public HashSet<string> DisabledFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> EnabledFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Whether a switchable part of the app is on, and how to switch it.
/// <para>
/// The port is in the shared kernel because every context asks the question and
/// none of them owns the answer: a pane in Backlog Management and a panel in
/// Second Brain both gate on a feature, and neither may reach through the other
/// or up into the Shell to find out. What a feature <em>is</em> — its display
/// name, its description, whether it is on by default — is product copy and
/// lives with the screen that renders it; the storage of the choice is file IO
/// and lives in an adapter. This is only the question and the answer.
/// </para>
/// </summary>
public interface IAppFeatureSettings
{
    /// <summary>Raised after any feature is switched, so open views can reload.</summary>
    event Action? Changed;

    /// <summary>The choices that have been made, as persisted.</summary>
    AppFeatureSettings Current { get; }

    /// <summary>Where those choices are written — shown on the settings page so
    /// the file can be found in a file manager.</summary>
    string SettingsPath { get; }

    /// <summary>Throws for a key no catalog defines: an unknown feature is a
    /// typo in code, not a choice somebody made.</summary>
    bool IsEnabled(string key);

    /// <summary>Switches a feature. Returns an error message rather than
    /// throwing when the switch could not be honoured or could not be saved —
    /// a settings toggle is an ordinary thing to click.</summary>
    string? SetEnabled(string key, bool enabled);
}

/// <summary>
/// The feature keys that belong to no single context.
/// <para>
/// Every other key lives with whatever owns the feature — <c>BacklogFeatures</c>
/// in Backlog Management's abstractions, <c>KnowledgeFeatures</c> in Second
/// Brain's, <c>DevPcFeatures</c> in Dev PC Management's, and the Shell's own on
/// the catalog it renders. This class is for the remainder: a key more than one
/// context gates on, where putting it in either context's abstractions would
/// make the other context reference it. The shared kernel is the one place all
/// of them may already see, so it is where such a key costs nothing.
/// </para>
/// </summary>
public static class AppFeatureKeys
{
    /// <summary>Backlog Management starts the CLI from an entry and Second Brain
    /// starts it from a knowledge chapter. Two contexts, one key.</summary>
    public const string CopilotCli = "copilot-cli";
}
