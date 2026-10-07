using System.Text;
using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IInboxBacklogTarget"/> port over Tasks'
/// published <see cref="ITaskItems"/>: the one place an inbox item becomes
/// entry text.
/// <para>
/// Here and not in either module, for the reason the roadmap's cross-context
/// joins are here: the Inbox may not see Tasks, and Tasks does not know the
/// Inbox exists. An adapter that references both published surfaces is the
/// only thing allowed to hold the translation — and the translation is the
/// entry text grammar (local ADR 0002, ADR 0007), which Tasks publishes and the
/// Inbox must never learn.
/// </para>
/// <para>
/// One entry per repository. The Tasks vocabulary lets an entry name several
/// repositories, but an inbox item assigned to two of them is two pieces of
/// work — one per codebase — rather than one piece of work about both, and a
/// person routing it expects to see two rows. The <c>repo:</c> token carries
/// the registry id verbatim; the ordinary save path resolves it without
/// registering, exactly as a typed token is treated.
/// </para>
/// <para>
/// Siblings know about each other. When an item goes to two or more
/// repositories, each entry's body opens with a "Same capture in:" line for
/// every other entry made from it, and all of them carry the general tag
/// <c>#from-inbox-</c> plus the last eight hex digits of the item's id — a
/// <c>#</c> tag, never a <c>+</c> plan tag, because the siblings are one capture
/// and not a plan. One repository or none writes neither.
/// </para>
/// <para>
/// Known and accepted: a title containing <c>#word</c> or <c>@name</c> is read
/// by the parser as a tag or a person, because that is what those sigils mean
/// on a title line. The capture said it; the entry keeps it.
/// </para>
/// </summary>
internal sealed partial class InboxBacklogTarget(ITaskItems tasks, IRepositoryDirectory repositories) : IInboxBacklogTarget
{
    public async Task<Result<IReadOnlyList<Guid>>> CreateTasksAsync(
        InboxRouteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Appended after everything the person has ranked, the way a fresh row
        // typed at the bottom of the pane is.
        var order = (await tasks.ListAsync(cancellationToken).ConfigureAwait(false)).Count;

        IReadOnlyList<string?> targets = request.RepoIds.Count == 0 ? [null] : [.. request.RepoIds];
        var created = new List<Guid>(targets.Count);

        foreach (var repo in targets)
        {
            var saved = await tasks
                .SaveFromTextAsync(null, Compose(request, repo), order++, request.InboxItemId.ToString("D"), cancellationToken)
                .ConfigureAwait(false);

            if (saved.IsFailure)
            {
                // Not compensated: the entries already made are real work in the
                // backlog and deleting them would be a second surprise. Named
                // instead, so the person can see what is there. Theoretical in
                // practice — the only validation failure on this path needs an
                // empty title, and an inbox item always has one.
                return Result.Failure<IReadOnlyList<Guid>>(created.Count == 0
                    ? saved.Error
                    : saved.Error with
                    {
                        Message = $"{saved.Error.Message} {created.Count} of {targets.Count} entries were created first: "
                            + string.Join(", ", created.Select(id => id.ToString("D"))) + ".",
                    });
            }

            created.Add(saved.Value.Entry.Id);
        }

        return created;
    }

    /// <summary>
    /// The batch as one import document: each item's entries exactly as
    /// <see cref="CreateTasksAsync"/> would write them, plus the batch's plan tag
    /// and an <c>id:</c> naming the item — <c>{item}/{repo}</c> when the item has
    /// several repositories, so every id stays one entry's inside the document.
    /// The ids are what the import's answer is matched back by, and what the
    /// per-entry source map is keyed on, so each entry is stamped with its own
    /// item and born Draft by the same rule as a single route.
    /// <para>
    /// Each item is decided on its own before anything is written, and one that
    /// cannot go is left out — named, with its reason — rather than taking the
    /// batch down with it. Two reasons. Notes that would not stay one entry
    /// inside a document: a top-level heading splits there, an unclosed fence
    /// hides the next item's heading. And a repository the workspace does not
    /// know: Tasks' import registers a <c>repo:</c> it has never seen, where the
    /// single route's save only resolves one, so a batch asks the registry the
    /// same read-only question the save does and never lets the import register.
    /// </para>
    /// <para>
    /// The dependencies the person confirmed become <c>after:</c> tokens, which
    /// is why the document is written only once every item has been decided.
    /// One on another item of the batch names every <c>id:</c> that item went
    /// into the document under — all of its repositories' entries, since the
    /// work waits on all of them — and one on an item left out names nothing and
    /// is dropped, because an <c>after:</c> naming no entry would block the task
    /// for good. One on a task already in the backlog is written as given: its
    /// imported <c>id:</c> or its own id, both of which Tasks' import resolves
    /// against the stored entries (ADR 0007).
    /// </para>
    /// </summary>
    public async Task<InboxBatchTargetResultDto> CreateBatchTasksAsync(
        InboxBatchRouteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sendable = new List<(InboxRouteRequestDto Item, IReadOnlyList<(string? Repo, string ImportItemId)> Entries)>(request.Items.Count);
        var refused = new List<(int Position, InboxBatchFailureDto Failure)>();

        for (var position = 0; position < request.Items.Count; position++)
        {
            var item = request.Items[position];

            if (Unsendable(item, request.PlanTag) is { } reason)
            {
                refused.Add((position, new InboxBatchFailureDto(item.InboxItemId, reason)));
                continue;
            }

            var itemId = item.InboxItemId.ToString("D");
            IReadOnlyList<string?> targets = item.RepoIds.Count == 0 ? [null] : [.. item.RepoIds];
            sendable.Add((item, [.. targets.Select(repo => (repo, ImportItemId(itemId, repo, targets.Count)))]));
        }

        if (sendable.Count == 0) return new InboxBatchTargetResultDto([], Ordered(refused));

        var importItemIdsOf = sendable.ToDictionary(
            entry => entry.Item.InboxItemId,
            entry => entry.Entries.Select(written => written.ImportItemId).ToList());

        var document = new StringBuilder();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var sent = new List<(Guid Item, IReadOnlyList<string> ImportItemIds)>(sendable.Count);

        foreach (var (item, entries) in sendable)
        {
            var itemId = item.InboxItemId.ToString("D");
            var after = After(item, importItemIdsOf);

            foreach (var (repo, importItemId) in entries)
            {
                if (document.Length > 0) document.Append('\n');
                document.Append(Compose(item, repo, request.PlanTag, importItemId, after));
                sources[importItemId] = itemId;
            }

            sent.Add((item.InboxItemId, importItemIdsOf[item.InboxItemId]));
        }

        var imported = await tasks
            .ImportPlanAsync(document.ToString(), defaultRepo: null, repoMatches: null, sourceInboxId: null, sourceInboxIds: sources, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (imported.IsFailure) return new InboxBatchTargetResultDto([], Ordered(refused), imported.Error);

        var byImportItemId = imported.Value.Entries
            .Where(entry => entry.ImportItemId is not null)
            .GroupBy(entry => entry.ImportItemId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

        var routed = new List<InboxRoutedDto>(sent.Count);
        var positions = request.Items.Select((item, index) => (item.InboxItemId, index)).ToDictionary(pair => pair.InboxItemId, pair => pair.index);

        foreach (var (item, importItemIds) in sent)
        {
            var made = importItemIds
                .Where(byImportItemId.ContainsKey)
                .Select(importItemId => byImportItemId[importItemId])
                .ToList();

            // Written already, so not a refusal of the batch: the other items
            // are routed, and whatever was made for this one is named.
            if (made.Count == importItemIds.Count) routed.Add(new InboxRoutedDto(item, made));
            else refused.Add((positions[item], new InboxBatchFailureDto(item, InboxErrors.BatchItemMissing(made))));
        }

        return new InboxBatchTargetResultDto(routed, Ordered(refused));
    }

    /// <summary>Why an item cannot go into a batch's document, or null when it
    /// can: a repository the workspace does not know, or notes that would not
    /// stay one entry inside the document.</summary>
    private Error? Unsendable(InboxRouteRequestDto item, string planTag)
    {
        if (item.RepoIds.FirstOrDefault(repo => repositories.Resolve(repo.Trim()) is null) is { } unknown)
        {
            return InboxErrors.BatchUnknownRepository(unknown.Trim());
        }

        var itemId = item.InboxItemId.ToString("D");
        IReadOnlyList<string?> targets = item.RepoIds.Count == 0 ? [null] : [.. item.RepoIds];

        return targets.All(repo => StandsAlone(Compose(item, repo, planTag, ImportItemId(itemId, repo, targets.Count))))
            ? null
            : InboxErrors.BatchItemNotSeparable;
    }

    private static string ImportItemId(string itemId, string? repo, int targets) =>
        targets > 1 ? $"{itemId}/{repo!.Trim()}" : itemId;

    /// <summary>The <c>after:</c> values one item's entries carry: every
    /// <c>id:</c> of each batch item it waits on that is in the document, then
    /// each task value as given — each once, in that order.</summary>
    private static List<string> After(InboxRouteRequestDto item, Dictionary<Guid, List<string>> importItemIdsOf)
    {
        var after = new List<string>();

        foreach (var target in item.AfterItems ?? [])
        {
            if (target == item.InboxItemId || !importItemIdsOf.TryGetValue(target, out var ids)) continue;
            after.AddRange(ids);
        }

        after.AddRange((item.AfterTasks ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));

        return [.. after.Distinct(StringComparer.Ordinal)];
    }

    private static List<InboxBatchFailureDto> Ordered(List<(int Position, InboxBatchFailureDto Failure)> refused) =>
        [.. refused.OrderBy(entry => entry.Position).Select(entry => entry.Failure)];

    /// <summary>Whether one composed entry stays one entry inside a document:
    /// it splits into no second segment, and it leaves no fence open to hide the
    /// next entry's heading. Asked of the parser the import splits with, and of
    /// fences the way it counts them.</summary>
    private static bool StandsAlone(string entry) =>
        EntryTextParser.SplitSegments(entry).Count == 1
        && entry.Split('\n').Count(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal)) % 2 == 0;

    public async Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(
        string planMarkdown,
        Guid sourceInboxId,
        IReadOnlyList<string> allowedRepoIds,
        string? attachmentPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedRepoIds);

        // Read here, with the same parser the import uses, for the one check the
        // import cannot make: it resolves a `repo:` it does not know by
        // registering it, and a repository the model made up must not become
        // one the workspace has. The drafter was told the item's list; anything
        // outside it is refused whole, before an entry or a registration exists.
        if (FirstUnknownRepository(planMarkdown, allowedRepoIds) is { } unknown)
        {
            return Result.Failure<IReadOnlyList<Guid>>(InboxErrors.PlanUnknownRepository(unknown));
        }

        var imported = await tasks
            .ImportPlanAsync(WithAttachment(planMarkdown, attachmentPath), defaultRepo: null, repoMatches: null, sourceInboxId.ToString("D"), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (imported.IsFailure) return Result.Failure<IReadOnlyList<Guid>>(imported.Error);

        return Result.Success<IReadOnlyList<Guid>>([.. imported.Value.Entries.Select(entry => entry.Id)]);
    }

    /// <summary>The drafted plan with every task entry carrying the item's
    /// attachment folder, or the plan as it came when there is none. Written
    /// entry by entry through Tasks' own writer, as <see cref="Compose"/> writes
    /// a routed entry's, so the path is spelled the way the grammar spells it and
    /// a <c>files:</c> the model wrote is replaced rather than kept beside it. A
    /// <c>plan</c> entry is left as written: it becomes a roadmap item, never a
    /// task, and a segment with no title is dropped by the import anyway.</summary>
    private static string WithAttachment(string planMarkdown, string? attachmentPath)
    {
        if (Attachment.From(attachmentPath) is not { } attachment) return planMarkdown;

        return string.Join("\n\n", EntryTextParser.SplitSegments(planMarkdown).Select(segment =>
            EntryTextParser.Parse(segment) is { Kind: not EntryKind.Plan } parsed && !string.IsNullOrWhiteSpace(parsed.Title)
                ? EntryTextParser.WithAttachment(segment, attachment)
                : segment));
    }

    /// <summary>The first <c>repo:</c> value in the plan that is not one of the
    /// item's, compared the way the registry compares ids, or null when every
    /// entry stays inside the list. Segments without a title are read too:
    /// the import drops them, but a repository named anywhere in the answer is
    /// still a repository the model made up.</summary>
    private static string? FirstUnknownRepository(string planMarkdown, IReadOnlyList<string> allowedRepoIds)
    {
        var allowed = new HashSet<string>(allowedRepoIds, StringComparer.OrdinalIgnoreCase);

        return EntryTextParser.SplitSegments(planMarkdown)
            .Select(EntryTextParser.Parse)
            .SelectMany(parsed => parsed.RepoIds ?? [])
            .FirstOrDefault(repo => !allowed.Contains(repo));
    }

    public Result<IReadOnlyList<InboxBatchEdge>> ReadDraftedOrder(
        string planMarkdown,
        IReadOnlyCollection<Guid> itemIds,
        IReadOnlyList<string> allowedRepoIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(allowedRepoIds);

        // The drafted plan's rule first, in the same words of the same parser:
        // a repository the batch does not go to is a repository made up.
        if (FirstUnknownRepository(planMarkdown ?? string.Empty, allowedRepoIds) is { } unknown)
        {
            return Result.Failure<IReadOnlyList<InboxBatchEdge>>(InboxErrors.OrderUnknownRepository(unknown));
        }

        var batch = itemIds.ToHashSet();
        var edges = new List<InboxBatchEdge>();

        // Segments without a title are dropped by the import and so are not
        // entries; everything else has to be one of the items, or the answer
        // is about something other than this batch.
        foreach (var parsed in EntryTextParser.SplitSegments(planMarkdown ?? string.Empty)
                     .Select(EntryTextParser.Parse)
                     .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Title)))
        {
            if (ItemOf(parsed.ImportItemId, batch) is not { } from)
            {
                return Result.Failure<IReadOnlyList<InboxBatchEdge>>(
                    InboxErrors.OrderUnknownItem(parsed.ImportItemId?.Trim() is { Length: > 0 } id ? id : $"\"{parsed.Title.Trim()}\" has no id"));
            }

            foreach (var value in parsed.DependsOn ?? [])
            {
                if (ItemOf(value, batch) is not { } to)
                {
                    return Result.Failure<IReadOnlyList<InboxBatchEdge>>(InboxErrors.OrderUnknownItem(value.Trim()));
                }

                // An item's several entries all name the same item, and one
                // waiting on itself is not an order.
                var edge = new InboxBatchEdge(from, to);
                if (from != to && !edges.Contains(edge)) edges.Add(edge);
            }
        }

        return edges;
    }

    /// <summary>The batch item an <c>id:</c> or <c>after:</c> value names: the
    /// item's guid, alone or with the <c>/repo</c> a batch writes for an item
    /// with several repositories. Null for anything else.</summary>
    private static Guid? ItemOf(string? value, HashSet<Guid> batch)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        var slash = trimmed.IndexOf('/', StringComparison.Ordinal);
        var head = slash < 0 ? trimmed : trimmed[..slash];

        return Guid.TryParse(head, out var id) && batch.Contains(id) ? id : null;
    }

    /// <summary>
    /// The item as entry text: title line, one metadata line, body, and the
    /// source URL as a trailing line the reader can follow — and, when the item
    /// arrived with files, its folder as the entry's attachment. Born a draft — the
    /// Inbox has decided the thought is work, and the backlog decides when it
    /// is ready. Tags go on as the metadata line writes them, bare and
    /// lower-case, and only those the grammar can read back as a tag (a tag
    /// opens with a letter); the rest would parse as unreadable tokens and be
    /// dropped on the next save, so they are not written at all.
    /// <para>
    /// The title is one line by the grammar's definition — the parser reads
    /// line two as the metadata line — so every run of whitespace in it,
    /// line breaks included, is written as one space. The aggregate stores a
    /// title that way already; this is the adapter refusing to depend on it,
    /// because the layout it writes is its own to keep.
    /// </para>
    /// </summary>
    internal static string Compose(
        InboxRouteRequestDto request,
        string? repo,
        string? planTag = null,
        string? importItemId = null,
        IReadOnlyList<string>? after = null)
    {
        var text = new StringBuilder();

        var title = WhitespaceRun().Replace(request.Title.Trim(), " ");
        var siblings = Siblings(request, repo);

        text.Append("# ").Append(title).Append('\n');
        text.Append("`task` `!draft`");

        IEnumerable<string> tags = request.Tags;
        if (siblings.Count > 0) tags = tags.Append(SiblingTag(request.InboxItemId));

        foreach (var tag in tags
                     .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
                     .Where(tag => tag.Length > 0 && char.IsAsciiLetter(tag[0]))
                     .Distinct(StringComparer.Ordinal))
        {
            text.Append(" `#").Append(tag).Append('`');
        }

        // A batch's tokens: the plan the entry belongs to, sigil and all, the
        // name it goes by inside the batch's document, and what it comes after.
        if (!string.IsNullOrWhiteSpace(planTag)) text.Append(" `").Append(planTag.Trim()).Append('`');
        if (!string.IsNullOrWhiteSpace(repo)) text.Append(" `repo:").Append(repo.Trim()).Append('`');
        if (!string.IsNullOrWhiteSpace(importItemId)) text.Append(" `id:").Append(importItemId).Append('`');
        foreach (var value in after ?? []) text.Append(" `after:").Append(value).Append('`');

        text.Append('\n');

        // Ahead of the person's notes rather than after them: notes ending in an
        // open fence or a `##` chapter would otherwise swallow the lines, as
        // fenced text or as a sub-item's note. And through Tasks' own prose
        // writer, so nothing a title carries can make one a heading, a step or
        // a fence.
        if (siblings.Count > 0)
        {
            text.Append('\n')
                .Append(EntryTextParser.AsProse(string.Join("\n\n", siblings.Select(sibling => $"Same capture in: {sibling} — {title}"))))
                .Append('\n');
        }

        var body = request.BodyMd.Trim();
        if (body.Length > 0) text.Append('\n').Append(body).Append('\n');

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            text.Append('\n').Append("Source: ").Append(request.SourceUrl.Trim()).Append('\n');
        }

        // The attachment through Tasks' own writer rather than a token spelled
        // here: how a path is written on the metadata line is the grammar's
        // business, and a workspace under "My Documents" has spaces in it.
        return Attachment.From(request.AttachmentPath) is { } attachment
            ? EntryTextParser.WithAttachment(text.ToString(), attachment)
            : text.ToString();
    }

    /// <summary>The other repositories an entry for <paramref name="repo"/> has
    /// siblings in, in the item's order — empty for an untargeted entry or an
    /// item with one repository, which have none.</summary>
    private static List<string> Siblings(InboxRouteRequestDto request, string? repo)
    {
        if (string.IsNullOrWhiteSpace(repo) || request.RepoIds.Count < 2) return [];

        var own = repo.Trim();
        return [.. request.RepoIds
            .Select(other => other.Trim())
            .Where(other => other.Length > 0 && !string.Equals(other, own, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The general tag every sibling of one item shares:
    /// <c>from-inbox-</c> and the last eight hex digits of the item's id.</summary>
    internal static string SiblingTag(Guid inboxItemId) => "from-inbox-" + inboxItemId.ToString("N")[^8..];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
