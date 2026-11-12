using System.ComponentModel;
using System.Globalization;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The Inbox's captures — from the phone, the website, a YouTube share — for an agent that
/// files them elsewhere: list them, read one with its attachments' files, and archive it once
/// it is filed. And the one of the four that writes a capture: an action the assistant
/// heard, so it reaches clarify (local ADR 0054).
/// <para>
/// <b>Through <see cref="IInboxItems"/>, never the database.</b> The archive is the module's
/// own <c>ArchiveAsync</c>, so it meets the same transition rule and the same save — and with
/// it the same replica outbox — as the pane's Archive. The capture is the module's own
/// <c>CaptureAsync</c>, Add by hand's path, so it meets the same guard on the text. Nothing
/// here deletes.
/// </para>
/// <para>
/// Each write is announced on <see cref="IInboxChangeSignal"/> once it lands, so an open
/// Inbox pane reloads and shows it without the person reopening the pane. The signal is
/// optional only so a host composing none still serves the tools.
/// </para>
/// <para>
/// Its own group behind the Inbox pane's key: switching the pane off withdraws these too.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class InboxTools(IInboxItems inbox, IInboxChangeSignal? changes = null)
{
    internal const string ListInboxItems = "list_inbox_items";
    internal const string ReadInboxItem = "read_inbox_item";
    internal const string ArchiveInboxItem = "archive_inbox_item";
    internal const string CaptureToInbox = "capture_to_inbox";

    /// <summary>How much of a body a listed item previews.</summary>
    internal const int PreviewLength = 160;

    [McpServerTool(Name = ListInboxItems, Title = "List the Inbox's captures", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The Inbox's captures, newest first: id, title, kind, channel, captured_at, status, the body's length and a "
        + "short preview, and how many attachments each holds. Unprocessed only unless status says otherwise. "
        + "Read one in full with read_inbox_item. Read-only.")]
    public async Task<InboxItemsPayload> ListAsync(
        [Description("Optional. unprocessed (the default), triaged, deferred, archived, or all.")]
        string? status = null,
        [Description("Optional. Only captures from this channel, e.g. mobile, web, manual.")]
        string? channel = null,
        [Description("Optional. Only captures of this kind, e.g. note, youtube, link, image.")]
        string? kind = null,
        [Description("Optional. The first day captured, yyyy-MM-dd, in UTC, inclusive.")]
        DateOnly? from = null,
        [Description("Optional. The last day captured, yyyy-MM-dd, in UTC, inclusive.")]
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var wanted = StatusFilter(status).ValueOrThrow();

        if (from is { } first && to is { } last && last < first)
        {
            throw RepositoryScope.Failure(Error.Validation("inbox.window_reversed", "to falls before from."));
        }

        var snapshot = await inbox.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var items = snapshot.Items
            .Where(item => wanted is null || item.Status == wanted)
            .Where(item => channel is null || string.Equals(item.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .Where(item => kind is null || string.Equals(item.KindSlug, kind, StringComparison.OrdinalIgnoreCase))
            .Where(item => from is null || DateOnly.FromDateTime(item.CapturedAt.UtcDateTime) >= from)
            .Where(item => to is null || DateOnly.FromDateTime(item.CapturedAt.UtcDateTime) <= to)
            .OrderByDescending(item => item.CapturedAt)
            .Select(InboxItemSummaryPayload.From)
            .ToList();

        return new InboxItemsPayload(items);
    }

    [McpServerTool(Name = ReadInboxItem, Title = "Read an Inbox capture", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "One capture in full: its Markdown body, source URL, tags and status, and each attachment's name, "
        + "content type, size and the file's path on this machine (null while it is not downloaded), so its "
        + "images can be read or copied. Refused for an unknown id (inbox.item.not_found). Read-only.")]
    public async Task<InboxItemPayload> ReadAsync(
        [Description("The capture's id, as a GUID, from list_inbox_items.")]
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken).ConfigureAwait(false);

        return InboxItemPayload.From(item);
    }

    [McpServerTool(Name = ArchiveInboxItem, Title = "Archive an Inbox capture", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Archives a capture once it has been filed elsewhere, as the Inbox's own Archive does: it leaves the "
        + "unprocessed list, stays restorable, and syncs to the other devices. Never deletes. Refused for an unknown "
        + "id (inbox.item.not_found) or a capture already archived (inbox.item.invalid_transition).")]
    public async Task<InboxItemSummaryPayload> ArchiveAsync(
        [Description("The capture's id, as a GUID.")]
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var archived = await inbox.ArchiveAsync(id, cancellationToken).ConfigureAwait(false);
        if (archived.IsFailure) throw RepositoryScope.Failure(archived.Error);

        changes?.Raise();

        return InboxItemSummaryPayload.From(await FindAsync(id, cancellationToken).ConfigureAwait(false));
    }

    // Not idempotent: Add by hand keeps no key a second call could match on, so the
    // same title sent twice is two captures — a client must not retry one that may have
    // landed. Not destructive: nothing that was there changes. Closed: the Inbox is this
    // machine's, and a capture reaches the phone only through the sync it already has.
    [McpServerTool(Name = CaptureToInbox, Title = "Capture into the Inbox", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Captures something the person wants to act on — a reminder, a to-do, a thing to look into — into the "
        + "Inbox as a new unprocessed item, as the Inbox's own Add by hand does, so the person clarifies it later: "
        + "what it is, and where it goes. Use capture_note instead for a note already judged worth keeping as "
        + "knowledge; that one goes straight into the Brain. Calling this twice captures twice. Answers the new "
        + "item, its id first. Refused for a blank title (inbox.item.needs_title) or a source link that is no "
        + "http or https address (inbox.capture.source_not_a_link).")]
    public async Task<InboxItemSummaryPayload> CaptureAsync(
        [Description("The capture's title: one line, as the person would say it. Line breaks become spaces.")]
        string title,
        [Description("Optional. Notes under the title, in Markdown, kept as the item's body as text, trimmed.")]
        string? body = null,
        [Description("Optional. The link the capture is about, an absolute http or https address.")]
        string? sourceUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Manual: the domain's word for "by hand" — somebody deciding to capture, which an
        // assistant asked to remember something is, rather than a capture arriving from a
        // device or a feed. The title is the module's to trim and collapse, so the guard is
        // the one the capture field meets.
        var captured = await inbox
            .CaptureAsync(title, body, InboxEnumMap.ManualChannel, sourceUrl, cancellationToken)
            .ConfigureAwait(false);
        if (captured.IsFailure) throw RepositoryScope.Failure(captured.Error);

        // Heard by an open Inbox, which reloads; the pane learns of no other way.
        changes?.Raise();

        return InboxItemSummaryPayload.From(captured.Value);
    }

    private async Task<InboxItemDto> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await inbox.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshot.Items.FirstOrDefault(item => item.Id == id)
            ?? throw RepositoryScope.Failure(InboxErrors.ItemNotFound);
    }

    /// <summary>The status a call asks for — null for all — or the refusal for one the Inbox has not.</summary>
    internal static Result<InboxStatus?> StatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return Result.Success<InboxStatus?>(InboxStatus.Unprocessed);
        if (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)) return Result.Success<InboxStatus?>(null);

        return Enum.TryParse<InboxStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            && !int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? Result.Success<InboxStatus?>(parsed)
            : Result.Failure<InboxStatus?>(Error.Validation(
                "inbox.unknown_status",
                $"'{status}' is no Inbox status: unprocessed, triaged, deferred, archived, or all."));
    }
}

public sealed record InboxItemsPayload(IReadOnlyList<InboxItemSummaryPayload> Items);

public sealed record InboxItemSummaryPayload(
    Guid Id,
    string Title,
    string Kind,
    string Channel,
    DateTimeOffset CapturedAt,
    string Status,
    int BodyLength,
    string Preview,
    int AttachmentCount)
{
    internal static InboxItemSummaryPayload From(InboxItemDto item) => new(
        item.Id,
        item.Title,
        item.KindSlug,
        item.Channel,
        item.CapturedAt,
        StatusSlug(item.Status),
        item.BodyMd.Length,
        PreviewOf(item.BodyMd),
        item.Attachments.Count);

    internal static string StatusSlug(InboxStatus status) => status.ToString().ToLowerInvariant();

    private static string PreviewOf(string body)
    {
        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return flat.Length <= InboxTools.PreviewLength ? flat : flat[..InboxTools.PreviewLength] + "…";
    }
}

public sealed record InboxItemPayload(
    Guid Id,
    string Title,
    string Kind,
    string Channel,
    DateTimeOffset CapturedAt,
    string Status,
    string BodyMd,
    string? SourceUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<InboxAttachmentPayload> Attachments)
{
    internal static InboxItemPayload From(InboxItemDto item) => new(
        item.Id,
        item.Title,
        item.KindSlug,
        item.Channel,
        item.CapturedAt,
        InboxItemSummaryPayload.StatusSlug(item.Status),
        item.BodyMd,
        item.SourceUrl,
        [.. item.Tags.Select(tag => tag.Name)],
        [.. item.Attachments.Select(attachment => new InboxAttachmentPayload(
            attachment.Id,
            attachment.Name,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.LocalPath))]);
}

public sealed record InboxAttachmentPayload(Guid Id, string Name, string ContentType, long SizeBytes, string? LocalPath);
