namespace Backlog.SharedKernel;

/// <summary>
/// How far along a feature is, which is a different question from whether it is
/// switched on: a half-built feature can be enabled by whoever wants to try it,
/// and the point of saying so is that they know what they are looking at.
/// <para>
/// Three values rather than a lifecycle, because only three of them change what
/// somebody sees. The scale is deliberately not <c>.domain</c>'s
/// draft/proposed/active/deprecated: those describe how settled a written model
/// is, and a chapter can sit at <c>draft</c> for a year while the feature it
/// describes ships. What is wanted here is whether the thing on screen can be
/// relied on. The two can be brought into line later by a script that writes
/// this value from the matching <c>.domain</c> chapter — which is why the
/// statuses are authored in one table rather than scattered across the screens
/// that read them.
/// </para>
/// </summary>
public enum AppFeatureStatus
{
    /// <summary>Finished, and carries no flag. First so that it is the default:
    /// a feature nobody has classified is an ordinary feature, and the scale
    /// only ever has to be mentioned by the features that are not.</summary>
    Released,

    /// <summary>Built and reachable, but not yet proven — worth trying, not yet
    /// worth trusting. Drawn as <c>BETA</c>.</summary>
    Beta,

    /// <summary>Under construction and not usable yet. Drawn as <c>DEV</c>, and
    /// the strongest thing the scale says.</summary>
    Dev
}

/// <summary>
/// Which half of the settings screen a feature is listed under.
/// <para>
/// The split is the one the kernel already draws for feature <em>keys</em>, read
/// back as product copy: a key that belongs to a bounded context is a
/// <see cref="Domain"/> feature, and a key that belongs to no single context —
/// the ones <see cref="AppFeatureKeys"/> exists for, plus the app chrome and the
/// integrations every area reaches through — is <see cref="CrossCutting"/>.
/// Thirteen switches in one undifferentiated grid gave a reader no way to tell
/// "an area of the product" from "something the whole product uses", which are
/// the two quite different things you might be turning off.
/// </para>
/// </summary>
public enum AppFeatureGroup
{
    /// <summary>An area of the product: a context's own capability. First
    /// because it is what somebody opening the screen is usually looking
    /// for.</summary>
    Domain,

    /// <summary>Something the whole product uses rather than one area of it —
    /// app chrome, an integration, an assistant.</summary>
    CrossCutting
}

/// <summary>
/// A switchable part of the app. Most features ship on and are opt-out; a
/// feature that is not proven yet sets <paramref name="EnabledByDefault"/> to
/// false so nobody meets it without asking for it.
/// <para>
/// <paramref name="Status"/> answers the neighbouring question — not whether the
/// feature is on, but whether it is finished. The two are independent: a
/// <see cref="AppFeatureStatus.Dev"/> feature that somebody has switched on is
/// exactly the case the flag exists for, and it is why the badge follows the
/// feature into the app rather than staying on the settings screen.
/// </para>
/// <para>
/// <paramref name="Group"/> is presentation and nothing else: it decides which
/// heading the switch is listed under and has no bearing on whether the feature
/// is on. It sits on the record rather than in the screen because the catalog is
/// already the one list, and a second list pairing keys with headings would be a
/// second thing to keep in step.
/// </para>
/// </summary>
public sealed record AppFeatureDefinition(
    string Key,
    string Name,
    string Description,
    bool AlwaysEnabled = false,
    bool EnabledByDefault = true,
    AppFeatureStatus Status = AppFeatureStatus.Released,
    AppFeatureGroup Group = AppFeatureGroup.Domain);

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
