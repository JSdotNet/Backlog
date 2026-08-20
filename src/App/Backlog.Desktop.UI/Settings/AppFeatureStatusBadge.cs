using Backlog.SharedKernel;

// Same namespace as AppFeatures for the reason set out there and at length in
// Settings.razor: a sibling namespace Backlog.Desktop.UI.Settings shadows the
// Settings component for everything under Backlog.Desktop.UI.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What a feature's maturity looks like, wherever it is drawn.
/// <para>
/// A status is shown in two places — the settings screen, where it qualifies the
/// switch, and the app chrome, where it qualifies the feature you are about to
/// use — and they are the same fact about the same feature. This is the one
/// place that turns <see cref="AppFeatureStatus"/> into what the badge renders,
/// so the two cannot drift the way the knowledge status pill and its select once
/// did before <c>KnowledgeStatusBadge</c> was written for the same reason.
/// </para>
/// <para>
/// It lives here rather than in the component library because the library is
/// kept renderable in the storybook with no application behind it, and an
/// <c>AppFeatureStatus</c> parameter would put the shared kernel behind every
/// badge. <c>FeatureStatusBadge</c> therefore takes a slug, and this is where the
/// slug comes from.
/// </para>
/// </summary>
public static class AppFeatureStatusBadge
{
    /// <summary>The <c>badge--feature-*</c> modifier a status maps onto, or the
    /// empty string for a status that draws nothing.
    ///
    /// <para>Empty for <see cref="AppFeatureStatus.Released"/> rather than a
    /// "released" modifier nobody styles: a finished feature is the ordinary
    /// case, and marking the ordinary case is how a badge stops meaning
    /// anything. The component renders nothing at all for an empty slug, which
    /// is what lets callers drop it into a header without an
    /// <c>@if</c>.</para></summary>
    public static string Slug(AppFeatureStatus status) => status switch
    {
        AppFeatureStatus.Dev => "dev",
        AppFeatureStatus.Beta => "beta",
        _ => string.Empty
    };

    /// <summary>The sentence behind the badge. Two or three uppercase letters
    /// cannot say why they are there, and a tooltip is the only room the app
    /// chrome has — the settings screen has the feature's description beside it,
    /// but a button in the header does not.</summary>
    public static string? Title(AppFeatureStatus status) => status switch
    {
        AppFeatureStatus.Dev => "In development — not usable yet.",
        AppFeatureStatus.Beta => "Ready to try, but not tested yet.",
        _ => null
    };

    /// <summary>The slug for a feature the app is about to draw an entry point
    /// for — empty unless the feature is both switched on and unfinished.
    ///
    /// <para>The enabled half of that is the point of the whole indicator: the
    /// settings screen shows a status whether or not the switch is on, because
    /// it is describing the switch. Everywhere else is describing something you
    /// can actually reach, and a feature that is off is not reachable. Asked
    /// here rather than at each of the seven entry points, because seven copies
    /// of an <c>&amp;&amp;</c> is seven chances to leave one out.</para>
    ///
    /// <para>An unknown key returns empty rather than throwing. The catalog and
    /// the call sites are the same assembly and a typo is a compile-time
    /// constant away from impossible, but a missing badge is the right failure
    /// for a decoration: <see cref="IAppFeatureSettings.IsEnabled"/> already
    /// throws for an unknown key, and it is the one that matters.</para></summary>
    public static string SlugFor(
        IReadOnlyList<AppFeatureDefinition> catalog,
        IAppFeatureSettings settings,
        string key)
    {
        var feature = Find(catalog, key);

        return feature is null || !settings.IsEnabled(key) ? string.Empty : Slug(feature.Status);
    }

    /// <summary>The tooltip matching <see cref="SlugFor"/>, on the same
    /// conditions.</summary>
    public static string? TitleFor(
        IReadOnlyList<AppFeatureDefinition> catalog,
        IAppFeatureSettings settings,
        string key)
    {
        var feature = Find(catalog, key);

        return feature is null || !settings.IsEnabled(key) ? null : Title(feature.Status);
    }

    private static AppFeatureDefinition? Find(IReadOnlyList<AppFeatureDefinition> catalog, string key)
    {
        for (var i = 0; i < catalog.Count; i++)
        {
            if (string.Equals(catalog[i].Key, key, StringComparison.OrdinalIgnoreCase)) return catalog[i];
        }

        return null;
    }
}
