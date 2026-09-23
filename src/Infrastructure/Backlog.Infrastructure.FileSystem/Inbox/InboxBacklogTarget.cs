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
/// Known and accepted: a title containing <c>#word</c> or <c>@name</c> is read
/// by the parser as a tag or a person, because that is what those sigils mean
/// on a title line. The capture said it; the entry keeps it.
/// </para>
/// </summary>
internal sealed partial class InboxBacklogTarget(ITaskItems tasks) : IInboxBacklogTarget
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

    public async Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(
        string planMarkdown,
        Guid sourceInboxId,
        IReadOnlyList<string> allowedRepoIds,
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
            .ImportPlanAsync(planMarkdown, defaultRepo: null, repoMatches: null, sourceInboxId.ToString("D"), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (imported.IsFailure) return Result.Failure<IReadOnlyList<Guid>>(imported.Error);

        return Result.Success<IReadOnlyList<Guid>>([.. imported.Value.Entries.Select(entry => entry.Id)]);
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

    /// <summary>
    /// The item as entry text: title line, one metadata line, body, and the
    /// source URL as a trailing line the reader can follow. Born a draft — the
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
    internal static string Compose(InboxRouteRequestDto request, string? repo)
    {
        var text = new StringBuilder();

        text.Append("# ").Append(WhitespaceRun().Replace(request.Title.Trim(), " ")).Append('\n');
        text.Append("`task` `!draft`");

        foreach (var tag in request.Tags
                     .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
                     .Where(tag => tag.Length > 0 && char.IsAsciiLetter(tag[0]))
                     .Distinct(StringComparer.Ordinal))
        {
            text.Append(" `#").Append(tag).Append('`');
        }

        if (!string.IsNullOrWhiteSpace(repo)) text.Append(" `repo:").Append(repo.Trim()).Append('`');

        text.Append('\n');

        var body = request.BodyMd.Trim();
        if (body.Length > 0) text.Append('\n').Append(body).Append('\n');

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            text.Append('\n').Append("Source: ").Append(request.SourceUrl.Trim()).Append('\n');
        }

        return text.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
