using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Buttons;

namespace Backlog.UI.Components.Integrations;

/// <summary>
/// The words and the class names for every state in this family, in one place.
///
/// <para>It is a mapper and not a set of parameters because the slug, the label
/// and the title are a triple that must not be mismatched: a chip whose class
/// says <c>merged</c> and whose text says "Closed" is worse than either, and
/// letting a host pass the three separately is letting a host do that. Taking an
/// enum and returning all three is the only shape where the mismatch is
/// unreachable, which is how <c>.design/accessibility.md</c>'s "colour is never
/// the sole carrier" rule becomes enforced rather than remembered.</para>
///
/// <para>The slug goes through <see cref="BadgeSlug"/> like every other badge in
/// the library. That type is assembly-internal, which is exactly why this file
/// sits in the library beside it rather than in a host: a modifier that reaches
/// a stylesheet has to survive the trip through CSS, and there is one rule for
/// that, not two.</para>
/// </summary>
internal static class IntegrationStates
{
    /// <summary>The slug a state falls back to when its own is unusable. Named
    /// rather than repeated, because a dangling <c>badge--integration-</c> is the
    /// one outcome <see cref="BadgeSlug"/> exists to prevent.</summary>
    private const string Fallback = "unknown";

    // --- Artifacts ---------------------------------------------------------

    public static string SlugOf(IntegrationArtifactState state) => BadgeSlug.Of(state switch
    {
        IntegrationArtifactState.Open => "open",
        IntegrationArtifactState.Draft => "draft",
        IntegrationArtifactState.Merged => "merged",
        IntegrationArtifactState.Closed => "closed",
        _ => Fallback
    }, Fallback);

    public static string LabelOf(IntegrationArtifactState state) => state switch
    {
        IntegrationArtifactState.Open => "Open",
        IntegrationArtifactState.Draft => "Draft",
        IntegrationArtifactState.Merged => "Merged",
        IntegrationArtifactState.Closed => "Closed",
        _ => "Not checked"
    };

    public static string TitleOf(IntegrationArtifactState state) => state switch
    {
        IntegrationArtifactState.Open => "Open on GitHub",
        IntegrationArtifactState.Draft => "Opened as a draft",
        IntegrationArtifactState.Merged => "Merged",
        IntegrationArtifactState.Closed => "Closed without merging",
        _ => "State has not been read yet"
    };

    // --- Sessions ----------------------------------------------------------

    public static string SlugOf(IntegrationSessionState state) => BadgeSlug.Of(state switch
    {
        IntegrationSessionState.Starting => "starting",
        IntegrationSessionState.Running => "running",
        IntegrationSessionState.Waiting => "waiting",
        IntegrationSessionState.Stalled => "stalled",
        IntegrationSessionState.Finished => "finished",
        IntegrationSessionState.Failed => "failed",
        _ => Fallback
    }, Fallback);

    /// <summary>"Waiting for you" rather than "Waiting", because the pair that
    /// earns this whole vocabulary is <see cref="IntegrationSessionState.Waiting"/>
    /// against <see cref="IntegrationSessionState.Stalled"/>: both look like
    /// nothing happening, one is correct, and the other is what the monitoring
    /// rules raise an alert on. A label that did not say who is being waited on
    /// would make the alert unexplainable.</summary>
    public static string LabelOf(IntegrationSessionState state) => state switch
    {
        IntegrationSessionState.Starting => "Starting",
        IntegrationSessionState.Running => "Running",
        IntegrationSessionState.Waiting => "Waiting for you",
        IntegrationSessionState.Stalled => "Stalled",
        IntegrationSessionState.Finished => "Finished",
        IntegrationSessionState.Failed => "Failed",
        _ => "Not checked"
    };

    public static string TitleOf(IntegrationSessionState state) => state switch
    {
        IntegrationSessionState.Starting => "Launched; not reporting yet",
        IntegrationSessionState.Running => "Working now",
        IntegrationSessionState.Waiting => "Waiting on an answer from you",
        IntegrationSessionState.Stalled => "Running, but nothing has moved",
        IntegrationSessionState.Finished => "Ended with work delivered",
        IntegrationSessionState.Failed => "Ended without delivering",
        _ => "State has not been read yet"
    };

    // --- Drift -------------------------------------------------------------

    public static string SlugOf(IntegrationDrift drift) => BadgeSlug.Of(drift switch
    {
        IntegrationDrift.LocalAhead => "local-ahead",
        IntegrationDrift.RemoteAhead => "remote-ahead",
        IntegrationDrift.Detached => "detached",
        _ => Fallback
    }, Fallback);

    /// <summary>Three short labels rather than one word and three titles. The
    /// reader's next move differs in each case, and a single "Mismatch" would
    /// make them look interchangeable when the fix for one is to close an issue
    /// and the fix for another is to go and find it.</summary>
    public static string LabelOf(IntegrationDrift drift) => drift switch
    {
        IntegrationDrift.LocalAhead => "Still open",
        IntegrationDrift.RemoteAhead => "Already closed",
        IntegrationDrift.Detached => "Missing",
        _ => string.Empty
    };

    public static string TitleOf(IntegrationDrift drift) => drift switch
    {
        IntegrationDrift.LocalAhead => "This entry is done, but the issue is still open.",
        IntegrationDrift.RemoteAhead => "The issue is closed, but this entry is not done.",
        IntegrationDrift.Detached => "The linked artifact is not where the projection points.",
        _ => string.Empty
    };

    // --- Availability ------------------------------------------------------

    public static string SlugOf(IntegrationAvailability availability) => BadgeSlug.Of(availability switch
    {
        IntegrationAvailability.NotAuthorized => "not-authorized",
        IntegrationAvailability.NotInstalled => "not-installed",
        IntegrationAvailability.Offline => "offline",
        IntegrationAvailability.FeatureOff => "feature-off",
        _ => "available"
    }, Fallback);

    /// <summary>
    /// The sentence a reader is owed when something cannot happen.
    ///
    /// <para>A host may write its own through <see cref="IntegrationReadiness.Reason"/>,
    /// but it never has to, and that is the point: an optional reason is a reason
    /// that goes missing. The default is built from the cause and the subject the
    /// record already had to carry, so the worst case is a general sentence and
    /// never a bare disabled control.</para>
    /// </summary>
    public static string ReasonFor(IntegrationReadiness readiness)
    {
        if (!string.IsNullOrWhiteSpace(readiness.Reason)) return readiness.Reason;

        var subject = string.IsNullOrWhiteSpace(readiness.Subject) ? "This" : readiness.Subject;

        return readiness.Availability switch
        {
            IntegrationAvailability.NotAuthorized => $"{subject} is not connected.",
            IntegrationAvailability.NotInstalled => $"{subject} is not installed on this machine.",

            // The one sentence with a second clause, and it is deliberate: the
            // design principles require offline to read as a calm, standing
            // condition rather than a failure, and the half that says everything
            // else keeps working is what makes it one.
            IntegrationAvailability.Offline => "Offline. This needs a connection; everything else keeps working.",

            IntegrationAvailability.FeatureOff => $"{subject} is turned off in settings.",
            _ => string.Empty
        };
    }

    /// <summary>What the way out is called, where the host did not name it. Null
    /// for offline: there is no button that puts a network back, and offering one
    /// would be the product pretending it can do something it cannot.</summary>
    public static string? RemedyFor(IntegrationReadiness readiness)
    {
        if (!string.IsNullOrWhiteSpace(readiness.RemedyLabel)) return readiness.RemedyLabel;

        var subject = string.IsNullOrWhiteSpace(readiness.Subject) ? null : readiness.Subject;

        return readiness.Availability switch
        {
            IntegrationAvailability.NotAuthorized => subject is null ? "Connect" : $"Connect {subject}",
            IntegrationAvailability.NotInstalled => "How to install",
            IntegrationAvailability.FeatureOff => "Open settings",
            _ => null
        };
    }

    // --- Providers ---------------------------------------------------------

    /// <summary>The provider's own name, spelled the way the provider spells it.
    /// It reaches an accessible name — "Suggested by GitHub Copilot" — so an
    /// abbreviation here would be an abbreviation read aloud.</summary>
    public static string NameOf(IntegrationProvider provider) => provider switch
    {
        IntegrationProvider.GitHub => "GitHub",
        IntegrationProvider.Copilot => "GitHub Copilot",
        IntegrationProvider.Claude => "Claude",
        IntegrationProvider.VsCode => "VS Code",
        _ => "AI"
    };

    public static string SlugOf(IntegrationProvider provider) => BadgeSlug.Of(provider switch
    {
        IntegrationProvider.GitHub => "github",
        IntegrationProvider.Copilot => "copilot",
        IntegrationProvider.Claude => "claude",
        IntegrationProvider.VsCode => "vscode",
        _ => "none"
    }, "none");

    // --- Density -----------------------------------------------------------

    /// <summary>
    /// How many acts a density shows before the rest collapse.
    ///
    /// <para>Four, three, two and none — deliberately tighter than the six acts
    /// this family ships, so the default state of a busy surface is a short row
    /// and a menu. <c>.design/design-principles.md#low-chrome-content-first</c>
    /// asks for "contextual and on-demand affordances over always-visible button
    /// rows", and a budget that fitted everything would be a budget that never
    /// applied.</para>
    /// </summary>
    public static int BudgetFor(IntegrationDensity density) => density switch
    {
        IntegrationDensity.Toolbar => 4,
        IntegrationDensity.Inline => 3,
        IntegrationDensity.Compact => 2,
        _ => 0
    };

    public static ButtonSize SizeFor(IntegrationDensity density) =>
        density is IntegrationDensity.Toolbar ? ButtonSize.Default : ButtonSize.Small;

    /// <summary>The class stem a button takes at this density. Compact is
    /// <c>btn btn--icon</c>, which is <c>IconButton</c>'s own stem, so an
    /// icon-only integration act is the same shape as every other icon-only
    /// button in the product rather than a second one that happens to look
    /// similar.</summary>
    public static string ButtonBaseClassFor(IntegrationDensity density) =>
        density is IntegrationDensity.Compact ? "btn btn--icon" : "btn";

    /// <summary>Whether the visible label is dropped. It is dropped from the
    /// screen only: the name still reaches the accessible name and the title,
    /// because an icon on its own says nothing to anyone who cannot see it and
    /// very little to anyone who can.</summary>
    public static bool IsIconOnly(IntegrationDensity density) =>
        density is IntegrationDensity.Compact;

    public static string SlugOf(IntegrationDensity density) => BadgeSlug.Of(density switch
    {
        IntegrationDensity.Toolbar => "toolbar",
        IntegrationDensity.Inline => "inline",
        IntegrationDensity.Compact => "compact",
        _ => "menu"
    }, "inline");
}
