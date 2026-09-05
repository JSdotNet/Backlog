using System.Text.RegularExpressions;
using Backlog.Desktop.UI.Inbox;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The backlog entries nobody has decided on yet, offered to the Inbox in the
/// Inbox's own words.
/// <para>
/// <strong>This is scaffolding.</strong> Inbox is a Core context in
/// <c>.domain/context-map.md</c>, with its own capture sources and a triage
/// decision — and it owns none of that yet. Until it does, the Inbox pane shows
/// backlog drafts. When capture becomes real the pane keeps working and this
/// file is what gets deleted.
/// </para>
/// <para>
/// It lives here rather than in <c>Inbox/</c> on purpose: it needs
/// <see cref="EntryRow"/>, and an Inbox that needs the backlog to render is an
/// Inbox that can never be lifted out. Tasks conforms to the Inbox's
/// published <see cref="InboxItem"/> contract instead — the direction the context
/// map already has between the two, and the one cross-context reference
/// <c>DesktopDomainBoundaryTests</c> allows.
/// </para>
/// <para>
/// What a draft <em>is</em> — a video, an article, a picture — is read off its
/// text, because a backlog draft has no capture source to ask. That is a
/// heuristic and is named as one: a YouTube host makes a video, an image
/// extension on the attachment makes an image, a fenced block makes code, any
/// other URL makes an article or a bare link, and everything else is text. The
/// real Inbox will be told by Capture rather than guess; until then a guess a
/// reader can see is better than a column of "Text".
/// </para>
/// </summary>
public static partial class TasksDrafts
{
    /// <summary>How many drafts the pane shows before it stops being a triage
    /// queue and starts being a second backlog.</summary>
    private const int MaxItems = 12;

    /// <summary>The channel every backlog draft arrives through: typed into the
    /// desktop by hand.</summary>
    private const string ManualChannel = "manual";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp"];
    private static readonly string[] DocumentExtensions = [".pdf", ".docx", ".doc", ".pptx", ".xlsx", ".md", ".txt", ".zip", ".tar.gz"];

    /// <summary>The untriaged drafts, as inbox items.</summary>
    public static IReadOnlyList<InboxItem> ForInbox(IEnumerable<EntryRow> rows) =>
        [.. rows
            .Where(row => !row.IsUntouched && row.PreviewStatus == EntryStatus.Draft)
            .OrderBy(row => row.PreviewTitle, StringComparer.OrdinalIgnoreCase)
            .Take(MaxItems)
            .Select(ToItem)];

    /// <summary>The row an item came from, or null when it has since gone.</summary>
    public static EntryRow? Find(IEnumerable<EntryRow> rows, InboxItem item) =>
        rows.FirstOrDefault(row => string.Equals(row.Key.ToString(), item.Key, StringComparison.Ordinal));

    /// <summary>One draft in the Inbox's words. Public so the reading of a draft
    /// can be pinned on its own, without a pane around it.</summary>
    public static InboxItem ToItem(EntryRow row)
    {
        var kind = KindOf(row);
        var tags = row.PreviewTags.Where(tag => !TagText.IsPerson(tag)).ToList();
        var person = row.PreviewTags.FirstOrDefault(TagText.IsPerson);
        var repository = row.PreviewRepoIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        var area = row.PreviewArea;

        return new InboxItem(row.Key.ToString(), row.PreviewTitle, area)
        {
            Kind = kind,
            Source = new InboxSource(ManualChannel, person),
            Tags = tags,
            Repository = repository,
            Para = ParaOf(kind, repository, area)
        };
    }

    /// <summary>
    /// Which PARA drawer a draft leans towards. Actionable beats reference: a
    /// draft that names a repository is work on a project whatever it links to,
    /// one filed in an area is that area's, and only then does a link or a video
    /// read as a resource. A plain note about nothing in particular is unsorted,
    /// which the pane says out loud rather than filing it somewhere plausible.
    /// </summary>
    internal static ParaCategory? ParaOf(InboxItemKind kind, string? repository, string? area)
    {
        if (!string.IsNullOrWhiteSpace(repository)) return ParaCategory.Projects;
        if (!string.IsNullOrWhiteSpace(area)) return ParaCategory.Areas;
        if (InboxItemKinds.IsReference(kind)) return ParaCategory.Resources;
        return null;
    }

    /// <summary>What the draft is, read off its text and attachment.</summary>
    internal static InboxItemKind KindOf(EntryRow row)
    {
        var text = row.RawText ?? string.Empty;

        if (row.PreviewAttachment is { } attachment)
        {
            if (HasExtension(attachment.Path, ImageExtensions)) return InboxItemKind.Image;
            if (HasExtension(attachment.Path, DocumentExtensions)) return InboxItemKind.Document;
        }

        var url = UrlRegex().Match(text);

        if (url.Success)
        {
            var address = url.Value;

            if (IsHost(address, "youtube.com") || IsHost(address, "youtu.be")) return InboxItemKind.YouTube;
            if (IsHost(address, "claude.ai") || IsHost(address, "claude.com")) return InboxItemKind.ClaudeArtifact;
            if (HasExtension(address, ImageExtensions)) return InboxItemKind.Image;
            if (HasExtension(address, [".pdf"])) return InboxItemKind.Document;

            // A title that is the URL itself is a link nobody has read yet; a title
            // in words over a URL is an article somebody meant to read.
            return string.Equals(row.PreviewTitle.Trim(), address, StringComparison.OrdinalIgnoreCase)
                ? InboxItemKind.Link
                : InboxItemKind.Article;
        }

        if (text.Contains("```", StringComparison.Ordinal)) return InboxItemKind.Code;

        return InboxItemKind.Text;
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        // Query strings and fragments are not part of the name.
        var clean = path.Split('?', '#')[0];
        return extensions.Any(extension => clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHost(string address, string host)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;

        return string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"https?://[^\s<>()""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
