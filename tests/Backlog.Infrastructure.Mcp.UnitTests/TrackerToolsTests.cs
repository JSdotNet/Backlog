using System.Reflection;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The six operations a session performs on the tracker: what each one does,
/// and — more to the point — what each one refuses to do.
/// </summary>
public class TrackerToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly TasksRepositoryRef Other = new("other", "JSdotNet", "Other");

    // --- find_item ----------------------------------------------------------

    /// <summary>The id selector is the one that cannot be ambiguous, and the one
    /// every other tool here expects to be handed.</summary>
    [Fact]
    public async Task Find_item_answers_with_the_entry_the_id_names()
    {
        var wanted = Guid.NewGuid();
        var tools = Tools(
            Entries.Entry("Something else"),
            Entries.Entry("The one", id: wanted, status: EntryStatus.InProgress));

        var answer = await tools.FindItemAsync(id: wanted, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(wanted, answer.Id);
        Assert.Equal("The one", answer.Title);
        Assert.Equal("in_progress", answer.Status);
    }

    /// <summary>
    /// Two matches is a question, not an answer. The tool never returns the
    /// first: picking one would be a guess about which piece of work the caller
    /// meant, and the caller would never find out a guess had been made.
    /// </summary>
    [Fact]
    public async Task Two_matches_are_refused_with_both_candidates_named()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var tools = Tools(
            Entries.Entry("Fix the parser", order: 0, id: first),
            Entries.Entry("Fix the parser", order: 1, id: second));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.FindItemAsync(title: "Fix the parser", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("item.ambiguous", failure.Message, StringComparison.Ordinal);
        Assert.Contains(first.ToString(), failure.Message, StringComparison.Ordinal);
        Assert.Contains(second.ToString(), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A title matches whole or not at all. A substring that happens to return one
    /// row today returns two the day somebody files a longer entry with the same
    /// words in it — so the narrow rule is the one that keeps answering the same
    /// question as the backlog grows.
    /// </summary>
    [Fact]
    public async Task A_title_matches_whole_trimmed_and_without_regard_to_case()
    {
        var tools = Tools(Entries.Entry("Fix the parser"));

        var answer = await tools.FindItemAsync(
            title: "  fix THE parser ",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Fix the parser", answer.Title);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.FindItemAsync(title: "parser", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("item.not_found", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both fields, on one projection. Matching them against the entry separately
    /// would call an entry linked to issue 42 somewhere else and to something
    /// else here a hit for "42 here".
    /// </summary>
    [Fact]
    public async Task The_link_selector_matches_one_projection_on_both_fields()
    {
        var wanted = Guid.NewGuid();
        var tools = Tools(
            Entries.Entry(
                "Right repository, wrong number",
                projections: [new EntryProjectionDto("JSdotNet/Backlog", "7", EntryProjectionDto.IssueTargetType)]),
            Entries.Entry(
                "Right number, wrong repository",
                projections: [new EntryProjectionDto("JSdotNet/Other", "42", EntryProjectionDto.IssueTargetType)]),
            Entries.Entry(
                "Both",
                id: wanted,
                projections:
                [
                    new EntryProjectionDto("JSdotNet/Other", "7", EntryProjectionDto.IssueTargetType),
                    new EntryProjectionDto("JSdotNet/Backlog", "42", EntryProjectionDto.IssueTargetType)
                ]));

        var answer = await tools.FindItemAsync(
            repoId: "JSdotNet/Backlog",
            externalId: "42",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(wanted, answer.Id);
    }

    /// <summary>No selector and two selectors are the same refusal, and it spells
    /// the rule out rather than resolving the ambiguity by precedence.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Naming_no_selector_or_two_is_refused_with_the_rule(bool withId, bool withTitle)
    {
        var id = Guid.NewGuid();
        var tools = Tools(Entries.Entry("The one", id: id));

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.FindItemAsync(
            id: withId ? id : null,
            title: withTitle ? "The one" : null,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("selector.required", failure.Message, StringComparison.Ordinal);
        Assert.Contains("exactly one selector", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Half the link selector is not a selector. Answering on the
    /// repository alone would return every entry ever linked there.</summary>
    [Fact]
    public async Task Half_the_link_selector_is_refused()
    {
        var tools = Tools(Entries.Entry("The one"));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.FindItemAsync(repoId: "JSdotNet/Backlog", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("selector.incomplete", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The filters run after the selector and narrow it — they are how a
    /// title shared by a done entry and a ready one stops being ambiguous.</summary>
    [Fact]
    public async Task The_filters_narrow_what_the_selector_found()
    {
        var ready = Guid.NewGuid();
        var tools = Tools(
            Entries.Entry("Fix the parser", status: EntryStatus.Done, repoIds: ["JSdotNet/Backlog"], tags: ["deploy"]),
            Entries.Entry("Fix the parser", id: ready, status: EntryStatus.Ready, repoIds: ["JSdotNet/Backlog"], tags: ["deploy"]));

        var answer = await tools.FindItemAsync(
            title: "Fix the parser",
            status: "ready",
            repository: "JSdotNet/Backlog",
            tag: "deploy",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ready, answer.Id);
    }

    /// <summary>An unknown status token is refused rather than ignored. A filter
    /// that quietly dropped itself would answer a wider question than the one
    /// asked and look like it had answered the right one.</summary>
    [Fact]
    public async Task An_unknown_status_token_is_refused()
    {
        var tools = Tools(Entries.Entry("The one"));

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.FindItemAsync(
            title: "The one",
            status: "in_progres",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("status.unknown", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An <c>owner/name</c> the registry has never seen is an ordinary
    /// answer that says so, and never a registration (local ADR 0012 §4).</summary>
    [Fact]
    public async Task An_unknown_repository_filter_is_refused_and_never_registered()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);
        var tools = new TrackerTools(new FakeTaskItems(Entries.Entry("The one")), directory);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.FindItemAsync(
            title: "The one",
            repository: "JSdotNet/Nowhere",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(directory.Registered);
    }

    /// <summary>Finding is a read. Nothing about it reaches a write on the
    /// port, which is what the double's recordings are there to show.</summary>
    [Fact]
    public async Task Finding_an_entry_writes_nothing()
    {
        var entries = new FakeTaskItems(Entries.Entry("The one"));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        await tools.FindItemAsync(title: "The one", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(entries.Saves);
        Assert.Empty(entries.Links);
        Assert.Empty(entries.Usages);
    }

    // --- read_item ----------------------------------------------------------

    /// <summary>
    /// The metadata line comes with it. The body alone omits the title, the type,
    /// the priority and the status, so a session editing what it was given would
    /// hand back an entry the grammar reads as having lost all four.
    /// </summary>
    [Fact]
    public async Task Read_item_answers_with_the_markdown_the_app_would_save()
    {
        var id = Guid.NewGuid();
        var entry = Entries.Entry(
            "Fix the parser",
            id: id,
            status: EntryStatus.InProgress,
            priority: Priority.High,
            body: "Some prose.\n\n## A step\nNotes for it.");

        var tools = Tools(entry);

        var answer = await tools.ReadItemAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(EntryTextParser.ToRawText(entry), answer.Markdown);
        Assert.Contains("`!in-progress`", answer.Markdown, StringComparison.Ordinal);
        Assert.Contains("## A step", answer.Markdown, StringComparison.Ordinal);
        Assert.Equal("in_progress", answer.Status);
    }

    /// <summary>An id nothing answers to is a named refusal, not an empty
    /// payload a session would read as an empty entry.</summary>
    [Fact]
    public async Task An_id_nothing_answers_to_is_refused()
    {
        var tools = Tools(Entries.Entry("The one"));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.ReadItemAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Contains("item.not_found", failure.Message, StringComparison.Ordinal);
    }

    // --- transition ---------------------------------------------------------

    /// <summary>The two moves a delivery session makes: starting work, and
    /// opening the pull request that finishes it (local ADR 0012 §5).</summary>
    [Theory]
    [InlineData(EntryStatus.Ready, "in-progress", "in_progress")]
    [InlineData(EntryStatus.InProgress, "done", "done")]
    public async Task A_legal_move_rewrites_the_status_token_and_saves(EntryStatus from, string token, string expected)
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id, order: 7, status: from));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.TransitionAsync(id, token, TestContext.Current.CancellationToken);

        Assert.True(answer.Changed);
        Assert.Null(answer.Refusal);
        Assert.Equal(expected, answer.Status);

        var save = Assert.Single(entries.Saves);
        Assert.Equal(id, save.Id);
        Assert.Equal(7, save.Order);
        Assert.Contains($"`!{EntryTextParser.StatusToken(EnumMap.ParseStatus(token))}`", save.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A jump the lifecycle does not draw changes nothing and comes back as a
    /// payload — the kept status, and where the entry may actually go. A refusal
    /// is the graph doing its job, not a malformed request, and a session that
    /// meets it has a next move to make rather than a failure to report.
    /// </summary>
    [Fact]
    public async Task A_refused_move_changes_nothing_and_answers_with_the_kept_status()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id, status: EntryStatus.Draft));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.TransitionAsync(id, "done", TestContext.Current.CancellationToken);

        Assert.False(answer.Changed);
        Assert.Equal("draft", answer.Status);
        Assert.NotNull(answer.Refusal);
        Assert.Equal(["ready"], answer.NextStatuses);
        Assert.Empty(entries.Saves);
    }

    /// <summary>The legal next steps ride on a success too. A session that has
    /// just started work wants to know that <c>done</c> is next as much as one
    /// that was stopped wants to know why.</summary>
    [Fact]
    public async Task A_successful_move_still_says_where_the_entry_may_go_next()
    {
        var id = Guid.NewGuid();
        var tools = Tools(Entries.Entry("Fix the parser", id: id, status: EntryStatus.Ready));

        var answer = await tools.TransitionAsync(id, "in-progress", TestContext.Current.CancellationToken);

        Assert.Equal(["done", "ready"], answer.NextStatuses);
    }

    /// <summary>
    /// Moving an entry to where it already is is an answer, not an error and not
    /// a write. The graph allows it so a repeated request settles quietly; saving
    /// anyway would restamp the entry and push an edit nobody made to every other
    /// device.
    /// </summary>
    [Fact]
    public async Task Moving_an_entry_to_the_status_it_has_writes_nothing()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id, status: EntryStatus.InProgress));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.TransitionAsync(id, "in-progress", TestContext.Current.CancellationToken);

        Assert.False(answer.Changed);
        Assert.Null(answer.Refusal);
        Assert.Equal("in_progress", answer.Status);
        Assert.Empty(entries.Saves);
    }

    /// <summary>
    /// A transition rewrites the entry's own status token and leaves its steps
    /// alone. The pane cascades when a person ticks the entry itself; a session
    /// saying where the work is has not said anything about which steps are done.
    /// </summary>
    [Fact]
    public async Task A_transition_does_not_cascade_to_the_sub_items()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry(
            "Fix the parser",
            id: id,
            status: EntryStatus.Ready,
            body: "## First step\n`!ready`\n\n## Second step\n`!ready`"));

        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        await tools.TransitionAsync(id, "in-progress", TestContext.Current.CancellationToken);

        var save = Assert.Single(entries.Saves);
        var parsed = EntryTextParser.Parse(save.RawText);

        Assert.Equal(EntryStatus.InProgress, parsed.Status);
        Assert.Equal(2, parsed.SubItems.Count);
        Assert.All(parsed.SubItems, subItem => Assert.Equal(EntryStatus.Ready, subItem.Status));
    }

    // --- comment ------------------------------------------------------------

    /// <summary>
    /// The failure the deleted <c>WithNote</c> existed to prevent, held as an
    /// assertion. A note-scoped write over a body that is one block would discard
    /// everything below the prose — every step, silently, on a save that
    /// succeeded.
    /// </summary>
    [Fact]
    public async Task A_comment_keeps_every_sub_item()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry(
            "Fix the parser",
            id: id,
            body: "The parser drops trailing tokens.\n\n## Reproduce it\nNotes.\n\n## Fix it\nMore notes.\n\n- [ ] And a checklist line"));

        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        var answer = await tools.CommentAsync(id, "Picked this up.", TestContext.Current.CancellationToken);

        var save = Assert.Single(entries.Saves);
        var parsed = EntryTextParser.Parse(save.RawText);

        Assert.Equal(["Reproduce it", "Fix it", "And a checklist line"], parsed.SubItems.Select(subItem => subItem.Title));
        Assert.Equal(3, answer.SubItems);

        // And the prose that was already there is still there, with the new line
        // after it rather than instead of it.
        Assert.Contains("The parser drops trailing tokens.", save.RawText, StringComparison.Ordinal);
        Assert.Contains("2026-09-23: Picked this up.", save.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A comment is spliced in above the entry's own chapters, so a line the
    /// grammar reads as structure would become structure. Each of these three
    /// breaks the entry a different way — a heading and a checklist line add a
    /// sub-item nobody created, and an opening fence swallows the real ones —
    /// and the tool refuses all of them rather than editing the caller's words
    /// into something safe.
    /// <para>
    /// Asserting <c>Assert.Empty(entries.Saves)</c> is the half that matters: a
    /// refusal that still saved would be the same corruption with an error
    /// message on top.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Picked this up.\n## Injected step\nwith notes", "## Injected step")]
    [InlineData("Picked this up.\n- [ ] Injected step", "- [ ] Injected step")]
    [InlineData("Picked this up.\n```\nnot really code", "```")]
    [InlineData("# A new entry entirely", "# A new entry entirely")]
    public async Task A_comment_that_would_restructure_the_entry_is_refused(string text, string offending)
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry(
            "Fix the parser",
            id: id,
            body: "The parser drops trailing tokens.\n\n## Reproduce it\nNotes."));

        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        var refusal = await Assert.ThrowsAsync<McpException>(
            () => tools.CommentAsync(id, text, TestContext.Current.CancellationToken));

        Assert.Contains("comment.not_prose", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(offending, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(entries.Saves);
    }

    /// <summary>
    /// Prose over several lines is not structure and is kept whole. The guard is
    /// about the grammar's markers, not about the newline — a session explaining
    /// itself in two sentences must not be made to squash them onto one line.
    /// <para>
    /// <c>#deploy</c> rides along because it is the case the guard most easily
    /// gets wrong: a tag is not a heading, and the parser tells them apart by the
    /// space the hash is missing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_comment_may_run_to_several_lines_of_prose()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry(
            "Fix the parser",
            id: id,
            body: "The parser drops trailing tokens.\n\n## Reproduce it\nNotes."));

        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        var answer = await tools.CommentAsync(
            id,
            "Picked this up.\nThe cause is the trailing-token branch, filed under #deploy.",
            TestContext.Current.CancellationToken);

        var save = Assert.Single(entries.Saves);
        var parsed = EntryTextParser.Parse(save.RawText);

        Assert.Equal(["Reproduce it"], parsed.SubItems.Select(subItem => subItem.Title));
        Assert.Equal(1, answer.SubItems);
        Assert.Contains("The cause is the trailing-token branch, filed under #deploy.", save.RawText, StringComparison.Ordinal);
    }

    /// <summary>The date is the module's one canonical format, off a clock a test
    /// can pin — never <c>DateTime.Now</c> read inside the tool.</summary>
    [Fact]
    public async Task A_comment_is_dated_in_the_modules_canonical_format()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        await tools.CommentAsync(id, "Opened the pull request.", TestContext.Current.CancellationToken);

        var save = Assert.Single(entries.Saves);

        Assert.Contains(
            $"{EntryTextParser.DateToken(new DateOnly(2026, 9, 23))}: Opened the pull request.",
            save.RawText,
            StringComparison.Ordinal);
    }

    /// <summary>Both halves, in that order. The note is what a person reads; the
    /// usage event is what the entry's history shows — and recording usage for a
    /// save that failed would claim the entry was used for something it was
    /// not.</summary>
    [Fact]
    public async Task A_comment_records_that_the_entry_was_used()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        await tools.CommentAsync(id, "Picked this up.", TestContext.Current.CancellationToken);

        var usage = Assert.Single(entries.Usages);

        Assert.Equal(id, usage.Id);
        Assert.Equal(TrackerTools.CommentUsageAction, usage.Action);
    }

    /// <summary>Two comments leave two lines. This tool is not idempotent and its
    /// hint says so, which is what stops a client retrying it as though it
    /// were.</summary>
    [Fact]
    public async Task Two_comments_leave_two_lines()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        await tools.CommentAsync(id, "First.", TestContext.Current.CancellationToken);
        await tools.CommentAsync(id, "Second.", TestContext.Current.CancellationToken);

        var last = entries.Saves[^1].RawText;

        Assert.Contains("2026-09-23: First.", last, StringComparison.Ordinal);
        Assert.Contains("2026-09-23: Second.", last, StringComparison.Ordinal);
    }

    /// <summary>A comment with nothing to say is refused before anything is
    /// written, rather than appending a bare date to somebody's entry.</summary>
    [Fact]
    public async Task An_empty_comment_is_refused_and_writes_nothing()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]), Clock());

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.CommentAsync(id, "   ", TestContext.Current.CancellationToken));

        Assert.Contains("comment.required", failure.Message, StringComparison.Ordinal);
        Assert.Empty(entries.Saves);
        Assert.Empty(entries.Usages);
    }

    // --- link_change --------------------------------------------------------

    /// <summary>
    /// A pull request, recorded as one. <c>TasksIssues.FindLink</c> filters on
    /// <c>issue</c>, so recording this under that value would show the detail pane
    /// a GitHub issue number belonging to a different object.
    /// </summary>
    [Fact]
    public async Task Link_change_records_a_pull_request_and_not_an_issue()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog, Other]));

        var answer = await tools.LinkChangeAsync(id, "JSdotNet/Backlog", "582", TestContext.Current.CancellationToken);

        var link = Assert.Single(entries.Links);

        Assert.Equal(EntryProjectionDto.PullRequestTargetType, link.TargetType);
        Assert.NotEqual(EntryProjectionDto.IssueTargetType, link.TargetType);
        Assert.Equal("JSdotNet/Backlog", link.RepoId);
        Assert.Equal("582", link.ExternalId);
        Assert.Equal(EntryProjectionDto.PullRequestTargetType, answer.TargetType);
    }

    /// <summary>The <c>owner/name</c> is resolved to the registry's id before it
    /// is written down, so an entry never acquires a coordinate spelled the way
    /// one caller happened to type it.</summary>
    [Fact]
    public async Task The_repository_is_resolved_before_it_is_recorded()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        await tools.LinkChangeAsync(id, "jsdotnet/backlog", "582", TestContext.Current.CancellationToken);

        Assert.Equal("JSdotNet/Backlog", Assert.Single(entries.Links).RepoId);
    }

    /// <summary>An unknown repository is refused before the port is touched, so a
    /// mistyped remote leaves the entry exactly as it was.</summary>
    [Fact]
    public async Task An_unknown_repository_refuses_the_link_before_anything_is_written()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var directory = new FakeRepositoryDirectory([Backlog]);
        var tools = new TrackerTools(entries, directory);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.LinkChangeAsync(id, "JSdotNet/Nowhere", "582", TestContext.Current.CancellationToken));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(entries.Links);
        Assert.Empty(directory.Registered);
    }

    /// <summary>The module's own refusal, surfaced rather than second-guessed:
    /// the tool does not pre-read the entry, so a missing id comes back as
    /// <c>entry.not_found</c> from the port that would have written it.</summary>
    [Fact]
    public async Task Linking_an_entry_that_is_gone_carries_the_modules_refusal()
    {
        var tools = Tools(Entries.Entry("Fix the parser"));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.LinkChangeAsync(Guid.NewGuid(), "JSdotNet/Backlog", "582", TestContext.Current.CancellationToken));

        Assert.Contains("entry.not_found", failure.Message, StringComparison.Ordinal);
    }

    // --- link_session -------------------------------------------------------

    /// <summary>A session, recorded as one, under the resolved repository — the
    /// vocabulary the task pane reads its session links back out of.</summary>
    [Fact]
    public async Task Link_session_records_the_session_under_its_own_target_type()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.LinkSessionAsync(id, "jsdotnet/backlog", " e711d47d-3e09 ", TestContext.Current.CancellationToken);

        var link = Assert.Single(entries.Links);

        Assert.Equal(EntryProjectionDto.SessionTargetType, link.TargetType);
        Assert.Equal("JSdotNet/Backlog", link.RepoId);
        Assert.Equal("e711d47d-3e09", link.ExternalId);
        Assert.False(answer.AlreadyLinked);
        Assert.Equal("e711d47d-3e09", answer.SessionId);
    }

    /// <summary>A session records itself at the start of its work and may do so
    /// again on a resume or a re-paste; the second call is answered, not written.</summary>
    [Fact]
    public async Task Linking_the_same_session_twice_writes_it_once()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        await tools.LinkSessionAsync(id, "JSdotNet/Backlog", "abc", TestContext.Current.CancellationToken);
        var second = await tools.LinkSessionAsync(id, "JSdotNet/Backlog", "ABC", TestContext.Current.CancellationToken);

        Assert.Single(entries.Links);
        Assert.True(second.AlreadyLinked);
    }

    /// <summary>An entry worked across two sessions names both.</summary>
    [Fact]
    public async Task A_second_session_is_a_second_link()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        await tools.LinkSessionAsync(id, "JSdotNet/Backlog", "first", TestContext.Current.CancellationToken);
        await tools.LinkSessionAsync(id, "JSdotNet/Backlog", "second", TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], entries.Links.Select(link => link.ExternalId));
    }

    /// <summary>A pull request with the same id as the session is a different
    /// fact, so it does not make the session read as already linked.</summary>
    [Fact]
    public async Task Only_a_session_projection_counts_as_already_linked()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry(
            "Fix the parser",
            id: id,
            projections: [new EntryProjectionDto("JSdotNet/Backlog", "582", EntryProjectionDto.PullRequestTargetType)]));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.LinkSessionAsync(id, "JSdotNet/Backlog", "582", TestContext.Current.CancellationToken);

        Assert.False(answer.AlreadyLinked);
        Assert.Single(entries.Links);
    }

    [Fact]
    public async Task An_empty_session_id_is_refused_before_anything_is_written()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser", id: id));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.LinkSessionAsync(id, "JSdotNet/Backlog", "  ", TestContext.Current.CancellationToken));

        Assert.Contains("session.required", failure.Message, StringComparison.Ordinal);
        Assert.Empty(entries.Links);
    }

    [Fact]
    public async Task Linking_a_session_to_an_entry_that_is_gone_is_refused()
    {
        var entries = new FakeTaskItems(Entries.Entry("Fix the parser"));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.LinkSessionAsync(Guid.NewGuid(), "JSdotNet/Backlog", "abc", TestContext.Current.CancellationToken));

        Assert.Contains("item.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(entries.Links);
    }

    // --- create_item --------------------------------------------------------

    /// <summary>A null id is what creates an entry, and the order is the count —
    /// appended after everything currently listed.</summary>
    [Fact]
    public async Task Create_item_saves_with_a_null_id_at_the_end_of_the_backlog()
    {
        var entries = new FakeTaskItems(Entries.Entry("First"), Entries.Entry("Second"));
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.CreateItemAsync(
            "# Write the release notes\n`task` `*high` `!ready`\n\nThe ones for 0.9.",
            cancellationToken: TestContext.Current.CancellationToken);

        var save = Assert.Single(entries.Saves);

        Assert.Null(save.Id);
        Assert.Equal(2, save.Order);
        Assert.Equal("Write the release notes", answer.Title);
        Assert.Equal("ready", answer.Status);
        Assert.NotEqual(Guid.Empty, answer.Id);
    }

    /// <summary>The repository argument is a default, the rule the Import dialog's
    /// target repository already follows: it reaches an entry whose text names
    /// none, and an entry that names one keeps what it says.</summary>
    [Fact]
    public async Task The_repository_argument_fills_a_gap_and_never_overrides()
    {
        var entries = new FakeTaskItems();
        var tools = new TrackerTools(entries, new FakeRepositoryDirectory([Backlog, Other]));

        await tools.CreateItemAsync(
            "# Needs a home\n`task` `!ready`",
            "JSdotNet/Backlog",
            TestContext.Current.CancellationToken);

        await tools.CreateItemAsync(
            "# Already has one\n`task` `!ready` `repo:JSdotNet/Other`",
            "JSdotNet/Backlog",
            TestContext.Current.CancellationToken);

        Assert.Equal(["JSdotNet/Backlog"], EntryTextParser.Parse(entries.Saves[0].RawText).RepoIds);
        Assert.Equal(["JSdotNet/Other"], EntryTextParser.Parse(entries.Saves[1].RawText).RepoIds);
    }

    /// <summary>A block with no title comes back as the module's own validation
    /// error rather than one this assembly invented, because the grammar is the
    /// module's and so is the verdict on it.</summary>
    [Fact]
    public async Task Text_with_no_title_carries_the_modules_validation_error()
    {
        var tools = Tools();

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.CreateItemAsync("   ", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("entry.title_required", failure.Message, StringComparison.Ordinal);
    }

    // --- the shape of the surface -------------------------------------------

    /// <summary>
    /// No delete tool, and no route to one.
    /// <para>
    /// Enforced rather than merely true today. A session that has misunderstood an
    /// instruction can undo a status change, a comment and a link by asking for
    /// the opposite; a deleted entry is gone from every device the moment the
    /// tombstone replicates, and no sequence of tool calls brings it back.
    /// </para>
    /// <para>
    /// Asserted two ways because neither is enough on its own: the catalog
    /// publishes no name that could be a delete, and calling every tool in the
    /// assembly reaches no <c>DeleteAsync</c> — which is what
    /// <see cref="FakeTaskItems"/> throws on, so any tool that ever grew one
    /// would fail its own tests rather than this one.
    /// </para>
    /// </summary>
    [Fact]
    public void No_tool_in_the_catalog_is_a_delete()
    {
        Assert.DoesNotContain(
            BacklogMcpTools.ToolNames,
            name => name.Contains("delete", StringComparison.OrdinalIgnoreCase)
                || name.Contains("remove", StringComparison.OrdinalIgnoreCase)
                || name.Contains("archive", StringComparison.OrdinalIgnoreCase));

        // And no method on any tool class names it either, which is the half a
        // renamed tool would slip past.
        var sources = BacklogMcpTools.Groups
            .Select(group => group.ToolType)
            .Distinct()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(method => method.Name);

        Assert.DoesNotContain(sources, name => name.Contains("Delete", StringComparison.Ordinal));
    }

    /// <summary>The whole group behind one key, and the key is the backlog's.
    /// Switching Tasks off takes the operations with the reads.</summary>
    [Fact]
    public void Every_tracker_tool_is_gated_on_the_tasks_key()
    {
        var features = new FakeAppFeatureSettings([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey).Distinct(StringComparer.Ordinal)]);

        Assert.All(BacklogMcpTools.Tracker.ToolNames, name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.All(BacklogMcpTools.Tracker.ToolNames, name => Assert.False(BacklogMcpTools.IsExposed(name, features)));
    }

    /// <summary>
    /// A fixed day, so a dated line is something a test can assert on rather than
    /// reconstruct.
    /// <para>
    /// Pinned to UTC as well as to an instant, and at 9am rather than at
    /// midnight. The tool reads the local now, because a comment is dated in the
    /// day the person writing it is living in; a fake left on the build agent's
    /// zone would put this test's date one day either side of the assertion
    /// depending on where the agent stands.
    /// </para>
    /// </summary>
    private static TimeProvider Clock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        return clock;
    }

    private static TrackerTools Tools(params TaskItemDto[] entries) =>
        new(new FakeTaskItems(entries), new FakeRepositoryDirectory([Backlog, Other]), Clock());
}
