using Backlog.SharedKernel;

namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>
/// Maps the Inbox enums to and from the tokens the store and the wire carry
/// (<c>unprocessed</c>, <c>claude-artifact</c>, <c>web_clipper</c>), the same
/// way Tasks' <c>EnumMap</c> does for its own vocabulary and for the same
/// reason: a database somebody opens should read as the domain reads, and an
/// ordinal would silently change meaning the day a member is inserted.
/// <para>
/// It differs from <c>EnumMap</c> in one place, on purpose. A status token this
/// build does not know still throws, because a status is a decision the module
/// took and there is no honest default for one. A <em>kind</em> token it does
/// not know is read as <see cref="ContentKind.Text"/> and the raw slug kept
/// beside it, because a kind is a reading of the capture rather than a
/// decision about it — a phone that learned a new kind before this desktop did
/// should produce an item shown as its plain word, not an item that breaks the
/// queue. The mechanism is the shared <see cref="WireTokenMap{TEnum}"/>; the
/// difference is the fallback <see cref="ParseKind"/> hands it.
/// </para>
/// </summary>
public static class InboxEnumMap
{
    /// <summary>The one channel a desktop-typed capture arrives through, and the
    /// value that says an item has no replica behind it.</summary>
    public const string ManualChannel = "manual";

    private static readonly WireTokenMap<InboxStatus> Statuses = new("inbox status", new Dictionary<InboxStatus, string>
    {
        [InboxStatus.Unprocessed] = "unprocessed",
        [InboxStatus.Triaged] = "triaged",
        [InboxStatus.Deferred] = "deferred",
        [InboxStatus.Archived] = "archived"
    });

    private static readonly WireTokenMap<ContentKind> Kinds = new("content kind", new Dictionary<ContentKind, string>
    {
        [ContentKind.Text] = "text",
        [ContentKind.Article] = "article",
        [ContentKind.Link] = "link",
        [ContentKind.YouTube] = "youtube",
        [ContentKind.Image] = "image",
        [ContentKind.Document] = "document",
        [ContentKind.Email] = "email",
        [ContentKind.Code] = "code",
        [ContentKind.Voice] = "voice",
        [ContentKind.ClaudeArtifact] = "claude-artifact"
    });

    private static readonly WireTokenMap<RoutingDomain> RoutingDomains = new(
        "routing domain",
        new Dictionary<RoutingDomain, string>
        {
            [RoutingDomain.Tasks] = "tasks",
            [RoutingDomain.Devbook] = "devbook",
            [RoutingDomain.Archive] = "archive"
        },
        // The wire token the member carried before the context was renamed; nothing
        // routed there, but a row that did would still be readable rather than thrown on.
        aliases: new Dictionary<string, RoutingDomain> { ["secondbrain"] = RoutingDomain.Devbook });

    public static string ToWire(InboxStatus value) => Statuses.ToWire(value);

    /// <summary>The slug a kind is stored and drawn as — identical to the
    /// shared component library's <c>CaptureKinds.All</c>, restated here because
    /// the module may not reference the library.</summary>
    public static string ToWire(ContentKind value) => Kinds.ToWire(value);

    public static string ToWire(RoutingDomain value) => RoutingDomains.ToWire(value);

    public static InboxStatus ParseStatus(string value) => Statuses.Parse(value);

    /// <summary>The kind a slug names, or <see cref="ContentKind.Text"/> when
    /// this build has no member for it. The caller keeps the raw slug beside the
    /// answer — see the class remarks for why an unknown kind is not an error.</summary>
    public static ContentKind ParseKind(string? value) => Kinds.Parse(value, fallback: ContentKind.Text);

    public static RoutingDomain ParseRoutingDomain(string value) => RoutingDomains.Parse(value);

    /// <summary>
    /// The channel token an item is filed under, from whatever a client wrote as
    /// its capture source. The editor extension calls itself <c>vscode</c> and
    /// the domain calls that channel <c>ide</c>; every other known token is
    /// already the domain's word, and one nobody recognises is kept as written
    /// rather than folded into "unknown" — the raw value is the most honest
    /// thing the pane can show for it.
    /// </summary>
    public static string NormalizeChannel(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "unknown";

        return trimmed.ToLowerInvariant() switch
        {
            "vscode" => "ide",
            var known and ("mobile" or "youtube" or "website" or "email" or "web_clipper" or "ide" or "import" or ManualChannel) => known,
            _ => trimmed
        };
    }
}
