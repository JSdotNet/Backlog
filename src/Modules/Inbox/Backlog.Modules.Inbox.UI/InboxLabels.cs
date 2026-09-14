using Backlog.Modules.Inbox.Abstractions;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The words the pane prints beside the module's tokens. The module speaks in
/// slugs — <c>web_clipper</c>, <c>claude-artifact</c> — and a reader reads
/// "Web clipper" and "Claude artifact"; this is the one place the two are
/// written down together, so a row and a detail never spell one differently.
/// </summary>
public static class InboxLabels
{
    /// <summary>The channel as a reader reads it beside its badge. A token this
    /// build does not know is printed as it came: the raw value is the most
    /// honest label there is for it.</summary>
    public static string Channel(string channel) => channel switch
    {
        "mobile" => "Mobile",
        "youtube" => "YouTube",
        "website" => "Website",
        "email" => "Email",
        "web_clipper" => "Web clipper",
        "ide" => "IDE",
        InboxEnumMap.ManualChannel => "Manual",
        "claude" => "Claude",
        _ => channel
    };

    /// <summary>The kind as a reader reads it beside the mark. The library's own
    /// label for a slug it draws; the slug itself for one it does not, which is
    /// the "shown as its plain word" rule for a kind a newer client invented.</summary>
    public static string Kind(string kindSlug) => CaptureKinds.Label(kindSlug);

    /// <summary>The status as a row says it, for the badge on an item that is no
    /// longer waiting.</summary>
    public static string Status(InboxStatus status) => status switch
    {
        InboxStatus.Unprocessed => "Unprocessed",
        InboxStatus.Triaged => "Routed",
        InboxStatus.Deferred => "Deferred",
        InboxStatus.Archived => "Archived",
        _ => status.ToString()
    };
}
