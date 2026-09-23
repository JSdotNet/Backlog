using System.ComponentModel;
using System.Text.RegularExpressions;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The operations a session performs on the tracker: find the entry it is about
/// to work on, read it, move it along the lifecycle, leave a note on it, record
/// the pull request its work produced, and file a new one.
/// <para>
/// <b>A second class rather than six more methods on <see cref="WorkTools"/>.</b>
/// That class's own doc says what it is: "one read: the entries, narrowed two
/// ways". These are not a narrowing of anything and four of them write, so
/// putting them there would make its description false about half its surface —
/// and a tool class's description is what a model reads to decide what it is
/// looking at.
/// </para>
/// <para>
/// <b>One group, the same feature key.</b> These are registered as their own
/// <see cref="BacklogMcpTools.McpToolGroup"/> carrying
/// <see cref="TasksFeatures.Tasks"/>, which <see cref="BacklogMcpTools.Work"/>
/// also carries. Local ADR 0012 §7 asks for one feature check per group, not one
/// group per key: a key names an <em>area</em>, and reading the backlog and
/// moving it are one area. See the group's own doc for why the split is by class
/// and not by flag.
/// </para>
/// <para>
/// <b>That key cannot currently answer no, and saying so is the point.</b>
/// <c>AppFeatures</c> declares <see cref="TasksFeatures.Tasks"/>
/// <c>AlwaysEnabled</c> — the settings screen draws its checkbox ticked and
/// disabled, and the store refuses to clear it — so today the check in front of
/// these tools always passes. The group carries the key regardless, for two
/// reasons. §7 asks the question per group, and a group that asked nothing would
/// be the one place the rule is not applied. And the whole surface is already
/// behind a switch that does answer no: <c>AppFeatures.McpServer</c> is
/// <c>EnabledByDefault: false</c> and stops the listener outright, which is how a
/// person actually turns these tools off. Whether the backlog itself should
/// become switchable is a question about the Tasks area rather than about these
/// tools, and it is not settled here.
/// </para>
/// <para>
/// <b>There is no delete tool, and its absence is a decision.</b>
/// <see cref="ITaskItems.DeleteAsync"/> exists and is never reached from this
/// assembly. A session that has misunderstood an instruction can undo a status
/// change, a comment and a link by making the opposite request; a deleted entry
/// is gone from every device the moment the tombstone replicates, and no
/// sequence of tool calls brings it back. <c>TrackerToolsTests</c> holds the
/// absence rather than leaving it to be noticed.
/// </para>
/// <para>
/// <b>An instance per call</b>, for the reason <see cref="WorkTools"/> states at
/// length: <c>ITaskItems</c> is registered <c>AddScoped</c> and the SDK builds
/// this class out of the request's own scope on every invocation. Nothing here
/// may be held on a singleton.
/// </para>
/// </summary>
/// <param name="entries">The Tasks port. Every write in this class goes through
/// it, and every one of those goes through the text grammar — local ADR 0002 put
/// the whole use-case surface behind one grammar, so a tool that set a field
/// would be a second place an entry is decided.</param>
/// <param name="repositories">Where an <c>owner/name</c> becomes a repository
/// this product knows. Resolved, never registered (ADR 0012 §4).</param>
/// <param name="clock">Where "today" comes from when a comment is dated.
/// Defaulted rather than required because the solution registers no
/// <see cref="TimeProvider"/> in the container the tools are built from — the
/// one registration that exists is <c>TryAddSingleton</c> inside the Cosmos
/// replica wiring, which a desktop without sync never runs. The default is the
/// shape every other adapter here uses for the same reason (
/// <c>DevbookAnnotationStore</c>, <c>GhCliAccountSource</c>,
/// <c>GitHubSettingsStore</c>): optional, so a test can pin the date, and
/// <see cref="TimeProvider.System"/> when nobody does.</param>
[McpServerToolType]
public sealed class TrackerTools(ITaskItems entries, IRepositoryDirectory repositories, TimeProvider? clock = null)
{
    internal const string FindItem = "find_item";
    internal const string ReadItem = "read_item";
    internal const string Transition = "transition";
    internal const string Comment = "comment";
    internal const string LinkChange = "link_change";
    internal const string CreateItem = "create_item";

    /// <summary>
    /// What a comment records in the entry's usage history.
    /// <para>
    /// A constant rather than a literal at the call site, and the same shape
    /// <c>TasksCopilotCli.UsageAction</c> uses: usage actions are read back as a
    /// set, so a value spelled at its only call site is a value that becomes two
    /// values the day a second caller appears.
    /// </para>
    /// </summary>
    internal const string CommentUsageAction = "mcp-comment";

    /// <summary>
    /// The line shapes a comment may not contain, because in this product the
    /// text <em>is</em> the entry: a comment is spliced into the note region,
    /// which sits above the entry's own chapters, so a line the parser reads as
    /// structure becomes structure.
    /// <para>
    /// Three of them, and they fail in two different directions. A heading is the
    /// loud one — <c>##</c> and <c>###</c> become sub-items, and a bare <c>#</c>
    /// starts a whole new entry when the text is re-parsed
    /// (<see cref="EntryTextParser.SplitSegments"/>) — and a <c>- [ ]</c> line
    /// becomes a sub-item the same way. An opening fence is the quiet one: it adds
    /// nothing and instead <em>swallows</em>, because <c>LocateSubItems</c> tracks
    /// fences, so one unmatched <c>```</c> in a note turns every real chapter
    /// below it into fenced prose. The first inflates
    /// <see cref="CommentPayload"/>'s sub-item count; the second hides it. Both
    /// make a note a structural edit nobody asked for.
    /// </para>
    /// <para>
    /// Refused rather than escaped, and that is the decision. Indenting the line
    /// out of the grammar would make it a code block; stripping the marker would
    /// store something other than what the caller said. Either is this tool
    /// editing prose it was handed to record, and a tool that quietly rewrites
    /// its input is worse than one that says no. A tag is not a heading and stays
    /// allowed — <c>#deploy</c> has no space after the hash, which is exactly the
    /// distinction <c>HeadingRegex</c> already draws.
    /// </para>
    /// </summary>
    private static readonly Regex Structure = new(
        @"^(?:#{1,6}[ \t]|[-*][ \t]+\[[ xX]\][ \t]|```)",
        RegexOptions.Compiled);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [McpServerTool(Name = FindItem, Title = "Find one backlog entry", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The one backlog entry matching exactly one selector - its id, the repository and number of an external "
        + "object linked to it, or its whole title - optionally narrowed by status, repository or tag. Answers with "
        + "one entry or refuses; it never chooses between two matches. Read-only.")]
    public async Task<EntryRefPayload> FindItemAsync(
        [Description("Selector. The entry's own id, as a GUID.")]
        Guid? id = null,
        [Description(
            "Selector, with externalId. The repository an external object was linked under, in owner/name form, "
            + "exactly as the link recorded it.")]
        string? repoId = null,
        [Description("Selector, with repoId. The number or key of the linked object, e.g. an issue number.")]
        string? externalId = null,
        [Description(
            "Selector. The entry's whole title. Matched trimmed and without regard to case, never as a substring.")]
        string? title = null,
        [Description("Optional filter. A status token: draft, ready, in-progress, done or archived.")]
        string? status = null,
        [Description("Optional filter. The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string? repository = null,
        [Description("Optional filter. A tag the entry carries, without its # sigil.")]
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var selector = Selector(id, repoId, externalId, title);
        var wanted = status is null ? null : (EntryStatus?)ParseStatus(status);

        // Resolved before the backlog is read, so an owner/name nobody registered
        // is the same refusal here as in every other tool rather than an empty
        // list that reads as "no such entry".
        var scope = string.IsNullOrWhiteSpace(repository)
            ? null
            : RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        var matched = all
            .Where(selector)
            .Where(entry => wanted is null || entry.Status == wanted)
            .Where(entry => scope is null
                || (entry.RepoIds is { } ids && ids.Contains(scope.Id, StringComparer.OrdinalIgnoreCase)))
            .Where(entry => string.IsNullOrWhiteSpace(tag)
                || entry.Tags.Any(carried => string.Equals(carried.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Order)
            .ToList();

        if (matched.Count == 0)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "item.not_found",
                "No backlog entry matches that selector and those filters."));
        }

        // Never the first of several. A session asking to move "the entry called
        // Fix the parser" when there are two of them has not said which piece of
        // work it means, and picking one is a guess whose consequence is a status
        // change on somebody else's entry. The candidates come back so the next
        // call can be made by id, which is the one selector that cannot be
        // ambiguous.
        if (matched.Count > 1)
        {
            var candidates = string.Join("; ", matched.Select(entry => $"{entry.Id} {entry.Title}"));

            throw RepositoryScope.Failure(Error.Conflict(
                "item.ambiguous",
                $"{matched.Count} backlog entries match. Ask again with one of these ids: {candidates}."));
        }

        return Reference(matched[0]);
    }

    [McpServerTool(Name = ReadItem, Title = "Read a backlog entry", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "One backlog entry as its markdown, metadata line and sub-items included - byte for byte what the app "
        + "would save. Read-only.")]
    public async Task<EntryTextPayload> ReadItemAsync(
        [Description("The entry's id, as a GUID. find_item is how you get one.")]
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entry = await RequireAsync(id, cancellationToken).ConfigureAwait(false);

        // ToRawText and not the Body alone. The body omits the title and the
        // metadata line, so a session editing what it was given here would hand
        // back an entry with no status, no priority and no repository - and the
        // grammar would read that as an edit that removed them.
        return new EntryTextPayload(
            entry.Id,
            entry.Title,
            EnumMap.ToWire(entry.Status),
            EntryTextParser.ToRawText(entry));
    }

    /// <summary>
    /// Moves an entry along the lifecycle, or explains why it stayed.
    /// <para>
    /// <b>A refusal is an answer.</b> It comes back as a
    /// <see cref="TransitionPayload"/> carrying the status the entry kept and
    /// where it may go instead, not as an <see cref="ModelContextProtocol.McpException"/>
    /// — the same way an unknown repository is an ordinary answer. A jump the
    /// lifecycle does not draw is not a malformed request or a broken tool; it is
    /// the graph doing its job, and a session that meets it has a next move to
    /// make rather than a failure to report.
    /// </para>
    /// <para>
    /// <b>The graph is asked before anything is rewritten.</b> The save path
    /// applies status through <c>TaskItem.SetStatus</c>, which walks past the
    /// graph on purpose — a person's typed <c>!done</c> is an edit. So the refusal
    /// is this tool's to make, and it has to be made first: a rewrite followed by
    /// a check would have already written the entry it was about to refuse.
    /// </para>
    /// </summary>
    [McpServerTool(Name = Transition, Title = "Move a backlog entry along its lifecycle", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Moves one backlog entry to a status the lifecycle allows from where it stands. A move the lifecycle does "
        + "not draw changes nothing and comes back with the status the entry kept and the moves that were legal. "
        + "Sub-items are left alone.")]
    public async Task<TransitionPayload> TransitionAsync(
        [Description("The entry's id, as a GUID.")]
        Guid id,
        [Description("The status to move to: draft, ready, in-progress, done or archived.")]
        string status,
        CancellationToken cancellationToken = default)
    {
        var entry = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        var target = ParseStatus(status);

        if (!EntryStatusFlow.IsAllowed(entry.Status, target))
        {
            return Standing(
                entry.Id,
                entry.Status,
                changed: false,
                $"A {EnumMap.ToWire(entry.Status)} entry cannot move straight to {EnumMap.ToWire(target)}.");
        }

        // Allowed, and already there. EntryStatusFlow lets a status reach itself
        // so that a repeated request is an answer rather than an error, and the
        // honest answer is "nothing happened": saving would restamp the entry,
        // which is a change every other device would pull, for an edit nobody
        // made. This is what makes the tool idempotent in the sense the hint
        // claims - the second call is a read.
        if (entry.Status == target) return Standing(entry.Id, entry.Status, changed: false, refusal: null);

        // cascadeSubItems stays false. The pane cascades when a person ticks the
        // entry itself, because that is a person saying they are finished with
        // the whole of it; a session moving an entry to in-progress is saying
        // where the work is, and silently marking every step done would destroy
        // the record of which ones actually were.
        var rewritten = EntryTextParser.WithStatus(EntryTextParser.ToRawText(entry), target);

        var saved = await entries
            .SaveFromTextAsync(entry.Id, rewritten, entry.Order, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = saved.ValueOrThrow();

        return Standing(result.Entry.Id, result.Entry.Status, changed: true, refusal: null);
    }

    /// <summary>
    /// Appends a dated line to an entry's own prose, leaving its steps where they
    /// are.
    /// <para>
    /// <b>The text is rebuilt rather than written through a note-scoped
    /// helper.</b> <c>EntryTextParser.WithNote</c> was deliberately deleted, and
    /// the comment standing in its place says why: a writer scoped to the note
    /// region "would be a supported-looking way to discard half of" an entry
    /// whose body is a block over the whole document. So this cuts the parent
    /// block out with <c>GetParentText</c>, appends to that, and splices it back
    /// with <c>ReplaceParentText</c> — the pair that is defined in terms of each
    /// other and therefore cannot disagree about where the sub-items start.
    /// <c>A_comment_keeps_every_sub_item</c> is the assertion, because the failure
    /// this shape avoids is silent: the save succeeds and the steps are gone.
    /// </para>
    /// <para>
    /// <b>The usage event is recorded after the save, not instead of it.</b>
    /// Acceptance asks for both: the note is what a person reads, the usage event
    /// is what the entry's history shows. Recording usage on a save that failed
    /// would claim the entry was used for something it never was.
    /// </para>
    /// </summary>
    [McpServerTool(Name = Comment, Title = "Comment on a backlog entry", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Appends a dated line to one backlog entry's notes and records that the entry was used. Sub-items and "
        + "everything already written are kept. Calling it twice leaves two lines.")]
    public async Task<CommentPayload> CommentAsync(
        [Description("The entry's id, as a GUID.")]
        Guid id,
        [Description(
            "What to record, as prose. It is dated for you. It may run to several lines, but no line may be a "
            + "markdown heading, a `- [ ]` checklist item or a ``` fence - those are how the entry's own structure "
            + "is written, and a comment carrying one is refused rather than allowed to rewrite the entry.")]
        string text,
        CancellationToken cancellationToken = default)
    {
        var entry = await RequireAsync(id, cancellationToken).ConfigureAwait(false);

        var note = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

        if (note.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "comment.required",
                "A comment needs something to say."));
        }

        if (Array.Find(note.Split('\n'), line => Structure.IsMatch(line.TrimStart())) is { } structural)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "comment.not_prose",
                $"A comment is prose, and this line would become part of the entry itself: '{structural.Trim()}'. "
                + "Headings, checklist items and fences are how chapters and sub-items are written, so a note "
                + "carrying one would restructure the entry rather than annotate it. Say it without the marker."));
        }

        var raw = EntryTextParser.ToRawText(entry);
        var parent = EntryTextParser.GetParentText(raw).TrimEnd('\n');
        var today = EntryTextParser.DateToken(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime));

        // A blank line between the date line and whatever precedes it, so the
        // note reads as markdown rather than running on into the last paragraph
        // - and no bullet in front of it, because `- [ ]` is a sub-item in this
        // grammar and a comment is not a step somebody has to tick.
        var appended = parent.Length == 0
            ? $"{today}: {note}"
            : $"{parent}\n\n{today}: {note}";

        var saved = await entries
            .SaveFromTextAsync(
                entry.Id,
                EntryTextParser.ReplaceParentText(raw, appended),
                entry.Order,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = saved.ValueOrThrow();

        await entries.RecordUsageAsync(entry.Id, CommentUsageAction, cancellationToken).ConfigureAwait(false);

        return new CommentPayload(
            result.Entry.Id,
            result.Entry.Title,
            EnumMap.ToWire(result.Entry.Status),
            result.Entry.TotalSubItems);
    }

    /// <summary>
    /// Records that the work on an entry opened a pull request.
    /// <para>
    /// <b>Not idempotent, and the hint says so.</b>
    /// <c>TaskItem.AddProjectionRef</c> appends without looking at what is
    /// already there, so linking the same pull request twice leaves two identical
    /// projections on the entry. The honest hint is the one that matches the
    /// aggregate; claiming idempotency here would invite a client to retry a call
    /// whose retry is a second row.
    /// </para>
    /// <para>
    /// <b>The target type is <c>pull-request</c>, never <c>issue</c>.</b>
    /// <c>TasksIssues.FindLink</c> filters projections on
    /// <see cref="EntryProjectionDto.IssueTargetType"/>, so a pull request
    /// recorded under that value would show the detail pane a GitHub issue number
    /// that is a different object, and would take away its offer to file the
    /// issue that was never filed.
    /// </para>
    /// </summary>
    [McpServerTool(Name = LinkChange, Title = "Link a pull request to a backlog entry", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Records the pull request one backlog entry's work produced, against that entry. Not idempotent: linking "
        + "the same pull request twice records it twice.")]
    public async Task<LinkPayload> LinkChangeAsync(
        [Description("The entry's id, as a GUID.")]
        Guid id,
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository,
        [Description("The pull request number, e.g. 582.")]
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();
        var number = (externalId ?? string.Empty).Trim();

        if (number.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "change.required",
                "A pull request number is required."));
        }

        // The entry's existence is the module's answer rather than a read of our
        // own: LinkToIssueAsync already refuses a missing id with
        // `entry.not_found`, and a pre-read here would be a second opinion about
        // the same fact that could disagree with it under a concurrent delete.
        var linked = await entries
            .LinkToIssueAsync(id, scope.Id, number, EntryProjectionDto.PullRequestTargetType, cancellationToken)
            .ConfigureAwait(false);

        var entry = linked.ValueOrThrow();

        return new LinkPayload(entry.Id, scope.Id, number, EntryProjectionDto.PullRequestTargetType);
    }

    /// <summary>
    /// Files a new entry from a block of entry markdown.
    /// <para>
    /// <b>The text is the entry, so the text is the argument.</b> There is no
    /// title parameter and no status parameter: <c>SaveFromTextAsync</c> with a
    /// null id is the one way an entry is created in this product, and a tool
    /// taking fields would be a second grammar for what an entry is — the exact
    /// thing local ADR 0002 rules out. A block with no title comes back as the
    /// module's own validation error, unaltered.
    /// </para>
    /// <para>
    /// <b><paramref name="repository"/> is a default and not an override</b>,
    /// which is the rule <c>ImportPlanCommand</c> already applies to the Import
    /// dialog's target repository: it reaches an entry whose own text names no
    /// <c>repo:</c>, and an entry that names one keeps what it says. A session
    /// that pasted a plan item complete with its repository token should not have
    /// that token rewritten by an argument it supplied for the entries that had
    /// none.
    /// </para>
    /// </summary>
    [McpServerTool(Name = CreateItem, Title = "Create a backlog entry", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Files a new backlog entry from a block of entry markdown - a `# Title` line, an optional metadata line of "
        + "`task` `*high` `!ready` tokens, prose, and `##` headings for steps. Appended to the end of the backlog. "
        + "Not idempotent: calling it twice files two entries.")]
    public async Task<EntryRefPayload> CreateItemAsync(
        [Description("The entry as markdown, starting with a `# Title` line.")]
        string rawText,
        [Description(
            "Optional. The repository in owner/name form to file it against, applied only when the text names no "
            + "`repo:` token of its own.")]
        string? repository = null,
        CancellationToken cancellationToken = default)
    {
        var scope = string.IsNullOrWhiteSpace(repository)
            ? null
            : RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        var text = rawText ?? string.Empty;

        if (scope is not null && (EntryTextParser.Parse(text).RepoIds ?? []).Count == 0)
        {
            text = EntryTextParser.WithRepoIds(text, [scope.Id]);
        }

        // The count and not the highest order plus one. Order is a rank the pane
        // rewrites wholesale on a drag, so what "append" means is "after
        // everything currently listed" - and a new entry the person has not
        // ranked belongs at the bottom, not wherever an old gap in the numbering
        // happens to put it.
        var saved = await entries
            .SaveFromTextAsync(null, text, all.Count, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Reference(saved.ValueOrThrow().Entry);
    }

    /// <summary>The entry, or the refusal that names the id. One read of the port
    /// for the four tools that start with "the entry you mean is this one".</summary>
    private async Task<TaskItemDto> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        return all.FirstOrDefault(entry => entry.Id == id)
            ?? throw RepositoryScope.Failure(Error.NotFound(
                "item.not_found",
                $"No backlog entry has the id '{id}'."));
    }

    /// <summary>
    /// Exactly one selector, as a predicate over the backlog.
    /// <para>
    /// The count is checked before anything is read, and a request naming none or
    /// two is refused with the rule spelled out rather than resolved by
    /// precedence. A tool that preferred the id when both were given would answer
    /// a question the caller did not ask, and would do it silently.
    /// </para>
    /// <para>
    /// <paramref name="repoId"/> is compared against what the projection stored
    /// rather than resolved through the directory, which is why it is a separate
    /// argument from the <c>repository</c> filter. A projection records the
    /// coordinate the link was made under; resolving it would refuse an
    /// <c>owner/name</c> the registry has since forgotten while the entry still
    /// names it, and then answer "no such entry" about an entry that is right
    /// there.
    /// </para>
    /// </summary>
    private static Func<TaskItemDto, bool> Selector(Guid? id, string? repoId, string? externalId, string? title)
    {
        var byId = id is not null;
        var byLink = !string.IsNullOrWhiteSpace(repoId) || !string.IsNullOrWhiteSpace(externalId);
        var byTitle = !string.IsNullOrWhiteSpace(title);

        if (new[] { byId, byLink, byTitle }.Count(given => given) != 1)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "selector.required",
                "Name exactly one selector: id, or repoId and externalId together, or title."));
        }

        if (byId) return entry => entry.Id == id;

        if (byLink)
        {
            if (string.IsNullOrWhiteSpace(repoId) || string.IsNullOrWhiteSpace(externalId))
            {
                throw RepositoryScope.Failure(Error.Validation(
                    "selector.incomplete",
                    "repoId and externalId are one selector and have to be given together."));
            }

            // Both fields, on one projection. Matching them against the entry
            // separately would find an entry linked to issue 42 in one repository
            // and to something else in the one asked about, and call it a hit.
            return entry => entry.Projections.Any(projection =>
                string.Equals(projection.RepoId, repoId.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(projection.ExternalId, externalId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // The whole title, never a substring. A substring that happens to return
        // one row today returns two the day somebody files a longer entry with
        // the same words in it, and the one it would have picked is whichever
        // sorted first. Trimmed and case-insensitive because those are typing,
        // not identity.
        return entry => string.Equals(entry.Title.Trim(), title!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The wire token as a status, or the refusal naming it. Deliberately
    /// this assembly's error rather than the <c>FormatException</c>
    /// <see cref="EnumMap.ParseStatus"/> throws: an exception that is not an
    /// <c>McpException</c> reaches the client as a generic failure with no code in
    /// it, and "unknown status" is precisely the sort of thing a model needs told
    /// in words it can act on.</summary>
    private static EntryStatus ParseStatus(string status)
    {
        try
        {
            return EnumMap.ParseStatus(status);
        }
        catch (FormatException)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "status.unknown",
                $"'{status}' is not a status. Use draft, ready, in-progress, done or archived."));
        }
    }

    /// <summary>Where an entry stands and where it may go, as the one shape both
    /// halves of <see cref="TransitionAsync"/> answer in — so a refusal and a move
    /// are the same record with different values rather than two shapes a caller
    /// has to tell apart.</summary>
    private static TransitionPayload Standing(Guid id, EntryStatus status, bool changed, string? refusal) =>
        new(
            id,
            EnumMap.ToWire(status),
            changed,
            refusal,
            [.. EntryStatusFlow.NextFrom(status).Select(EnumMap.ToWire)]);

    private static EntryRefPayload Reference(TaskItemDto entry) =>
        new(entry.Id, entry.Title, EnumMap.ToWire(entry.Status));
}
