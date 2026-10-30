using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Where each stack component works, read from <see cref="AiModel"/> rather than
/// authored a second time: the AI usage chapters whose <c>depends-on</c> names the
/// component's <c>.devbook/tech/ai-development.md</c> entry, with the stages and
/// layers they sit at.
/// </summary>
public static class DevbookStackUsages
{
    /// <summary>The three components the Devbook tab draws, as <c>components</c>
    /// keys them, with the <c>.devbook/tech/ai-development.md</c> entry each one is
    /// rated in.</summary>
    public static IReadOnlyList<(string Key, string TechAnchor)> Components { get; } =
    [
        ("delivery", "delivery-engine"),
        ("devbook", "devbook-plugin"),
        ("schedule", "delivery-schedule")
    ];

    private const string TechEntryFile = ".tech/ai-development.md";

    /// <summary>The usages whose <c>depends-on</c> names the component's entry, in
    /// reading order; none for a key off <see cref="Components"/>.</summary>
    public static IReadOnlyList<AiModelUsage> Of(AiModel model, string componentKey)
    {
        ArgumentNullException.ThrowIfNull(model);

        var anchor = Components.FirstOrDefault(component => string.Equals(component.Key, componentKey, StringComparison.Ordinal)).TechAnchor;
        if (anchor is null) return [];

        return [.. model.Usages.Where(usage => usage.DependsOn.Any(reference =>
            string.Equals(reference.Slug, anchor, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DevbookLayout.ConventionalPath(reference.Path), TechEntryFile, StringComparison.OrdinalIgnoreCase)))];
    }
}
