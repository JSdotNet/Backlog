using System.Globalization;

using Backlog.SharedKernel;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The short values the settings menu draws on the right of a page's name, and the
/// words of the note a switched-off page shows in place of its own component.
/// Words only, so the screen and its tests agree on them without rendering.
/// </summary>
public static class SettingsBadges
{
    /// <summary>The badge on a module's page while the feature it sits behind is off.</summary>
    public const string Off = "Off";

    /// <summary>How many AI providers the AI providers page counts.</summary>
    public const int AiProviderCount = 3;

    /// <summary>The Features page's badge: how many features of
    /// <paramref name="catalog"/> are on, over how many there are - <c>9/19</c>.</summary>
    public static string Features(IReadOnlyCollection<AppFeatureDefinition> catalog, Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(isEnabled);

        var on = catalog.Count(feature => isEnabled(feature.Key));
        return Fraction(on, catalog.Count);
    }

    /// <summary>The AI providers page's badge: how many of Claude usage, GitHub usage
    /// and Azure Foundry are set up, over three - <c>2/3</c>.</summary>
    public static string AiProviders(bool claude, bool gitHub, bool azureFoundry)
    {
        var configured = (claude ? 1 : 0) + (gitHub ? 1 : 0) + (azureFoundry ? 1 : 0);
        return Fraction(configured, AiProviderCount);
    }

    /// <summary>A count as its figure, zero included: an empty list says so.</summary>
    public static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name the note on a switched-off page gives its feature: the
    /// catalog's name for <paramref name="featureKey"/>, or the page's own title
    /// where the catalog names no such feature.</summary>
    public static string FeatureName(IEnumerable<AppFeatureDefinition> catalog, string featureKey, string pageTitle)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.FirstOrDefault(feature => string.Equals(feature.Key, featureKey, StringComparison.Ordinal))?.Name
            ?? pageTitle;
    }

    private static string Fraction(int part, int whole) =>
        string.Create(CultureInfo.InvariantCulture, $"{part}/{whole}");
}
