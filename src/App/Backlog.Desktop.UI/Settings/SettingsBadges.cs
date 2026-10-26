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

    /// <summary>The Features page's badge: how many features of
    /// <paramref name="catalog"/> are on, over how many there are - <c>9/19</c>.</summary>
    public static string Features(IReadOnlyCollection<AppFeatureDefinition> catalog, Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(isEnabled);

        var on = catalog.Count(feature => isEnabled(feature.Key));
        return Fraction(on, catalog.Count);
    }

    /// <summary>
    /// The AI providers page's badge: how many of the providers the page offers are
    /// set up, over how many it offers - <c>2/3</c>. Claude and GitHub come with
    /// <paramref name="usageMetrics"/>, Azure Foundry with <paramref name="aiAssistant"/>:
    /// the rule the page draws its tiles and cards by, so the badge never counts a
    /// provider the page does not show. A provider not offered is not counted set up
    /// either, whatever its store holds.
    /// </summary>
    public static string AiProviders(bool usageMetrics, bool aiAssistant, bool claude, bool gitHub, bool azureFoundry)
    {
        var offered = (usageMetrics ? 2 : 0) + (aiAssistant ? 1 : 0);
        var configured = usageMetrics ? (claude ? 1 : 0) + (gitHub ? 1 : 0) : 0;
        configured += aiAssistant && azureFoundry ? 1 : 0;
        return Fraction(configured, offered);
    }

    /// <summary>
    /// The MCP server page's badge: the listener's state in a word or two -
    /// <c>Listening</c>, <c>Not listening</c> once a bind has failed, or
    /// <c>Starting</c> between asking and knowing. <see langword="null"/> where
    /// <paramref name="isListening"/> is, which is a host with no listener of its
    /// own to report on: the browser harness, whose endpoint is the host's.
    /// </summary>
    public static string? McpServer(bool? isListening, bool failed) => isListening switch
    {
        null => null,
        true => "Listening",
        false when failed => "Not listening",
        false => "Starting",
    };

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
