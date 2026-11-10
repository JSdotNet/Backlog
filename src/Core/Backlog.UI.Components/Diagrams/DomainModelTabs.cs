using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Diagrams;

/// <summary>Which tab of the Model page an asked anchor opens, and on which event.</summary>
/// <param name="Tab">The tab's id: <see cref="DomainModelTabs.Diagram"/>,
/// <see cref="DomainModelTabs.Fields"/> or <see cref="DomainModelTabs.Events"/>.</param>
/// <param name="Event">The event Events opens on, by the slug of its title, or
/// <see langword="null"/> for its first.</param>
public sealed record DomainModelOpening(string Tab, string? Event);

/// <summary>
/// The Model page's tab for the anchor of the link that opened it (local ADR 0047):
/// an event's slug — <c>taskprojected</c> — opens Events on that event's contract,
/// <c>events</c> opens Events, <c>fields</c> opens Fields where the model file has
/// a <c>## Fields</c> chapter, and anything else, or none, opens Diagram. No
/// chapter gains a heading for it: <c>#events</c> is the view's own anchor, which a
/// Markdown viewer reads as the file's top.
/// </summary>
public static class DomainModelTabs
{
    public const string Diagram = "diagram";
    public const string Fields = "fields";
    public const string Events = "events";

    public static DomainModelOpening For(string? anchor, IReadOnlyList<DomainEventContract> events, string? markdown)
    {
        ArgumentNullException.ThrowIfNull(events);

        var asked = (anchor ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
        if (asked.Length == 0) return new(Diagram, null);

        if (events.FirstOrDefault(e => DomainUnits.Slug(e.Title) == asked) is { } picked) return new(Events, DomainUnits.Slug(picked.Title));
        if (asked == Events) return new(Events, null);
        if (asked == Fields && HasFieldsChapter(markdown)) return new(Fields, null);
        return new(Diagram, null);
    }

    private static bool HasFieldsChapter(string? markdown) =>
        MarkdownChapters.Split(markdown).Any(chapter => chapter.Level == 2 && string.Equals(chapter.Title.Trim(), "Fields", StringComparison.OrdinalIgnoreCase));
}
