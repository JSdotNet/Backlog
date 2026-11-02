using System.Security.Cryptography;
using System.Text;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one-time devbook import's plan, read off a repository's default branch on
/// disk: which subjects carry a state the store should start from, which open
/// <c>annotation</c> fences become remarks, and what was passed over and why.
/// <para>
/// Over the checked-in fixture <c>tests/Fixtures/devbook-import/</c>, where each
/// file is one row of the criteria. The planner reads and never writes, so the
/// fixture is read in place.
/// </para>
/// </summary>
public sealed class DevbookMarkdownImportTests
{
    private const string Alias = "jsdotnet/shop";

    private static readonly string Root = RepositoryRoot.Directory("tests", "Fixtures", "devbook-import");

    private static DevbookImportPlan Plan() => DevbookMarkdownImport.Plan(Alias, Root, DevbookFolderSetting.Defaults());

    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_debt_record_imports_its_status()
    {
        var debt = Assert.Single(Plan().Subjects, subject => subject.Kind == DevbookSubjectKind.Debt);

        Assert.Equal(".devbook/arc42/tdr/0001-paging.md", debt.Key);
        Assert.Equal("planned", debt.State);
        Assert.Null(debt.Approval);
        Assert.Null(debt.Acceptance);
    }

    /// <summary>A decision record is keyed by the pull request it is decided in,
    /// which the Markdown does not carry: the plan names the path and the state,
    /// and the pull request is the caller's to resolve.</summary>
    [Fact]
    public void A_proposed_decision_record_is_planned_for_its_pull_request()
    {
        var decisions = Plan().Subjects.Where(subject => subject.Kind == DevbookSubjectKind.Decision).ToList();

        Assert.Equal(
            [".devbook/arc42/adr/0001-pay-by-card.md", ".devbook/arc42/adr/0005-trace-every-order.md"],
            decisions.Select(decision => decision.Key));
        Assert.All(decisions, decision => Assert.Equal("proposed", decision.State));
        Assert.All(decisions, decision => Assert.True(decision.NeedsPullRequest));

        var state = decisions[0].ToState(Alias, new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), pullRequest: 1611);

        Assert.Equal(DevbookSubject.Decision(".devbook/arc42/adr/0001-pay-by-card.md", 1611), state.Subject);
        Assert.Equal(DevbookSubject.IdFor(Alias, state.Subject), state.Id);
        Assert.Equal("proposed", state.State);
        Assert.Equal(DevbookMarkdownImport.ImportAuthor, state.By);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), state.At);
        Assert.Equal(state.At, state.UpdatedAt);
    }

    /// <summary>The approval and acceptance keep who, when and the fingerprint the
    /// repository authored — not this host's name and clock — and the state is
    /// stamped with the acceptance, so a person's later write wins.</summary>
    [Fact]
    public void An_accepted_proposal_keeps_its_authored_approval_and_acceptance()
    {
        var proposal = Assert.Single(Plan().Subjects, subject => subject.Key == "pay-by-card");

        Assert.Equal(DevbookSubjectKind.Proposal, proposal.Kind);
        Assert.Equal("accepted", proposal.State);
        Assert.False(proposal.NeedsPullRequest);
        Assert.False(proposal.NeedsCommitTime);

        var state = proposal.ToState(Alias, committedAt: null);

        Assert.Equal(new DevbookSubjectDecision("Job Schepers", Day(9, 10), "sha256:2b11c532"), state.Approval);
        Assert.Equal(
            new DevbookSubjectDecision("Ada Reviewer", new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero), "sha256:2b11c532"),
            state.Acceptance);
        Assert.Equal("Ada Reviewer", state.By);
        Assert.Equal(state.Acceptance!.At, state.At);
        Assert.Equal(state.Acceptance.At, state.UpdatedAt);
        Assert.Equal(DevbookSubject.Proposal("pay-by-card"), state.Subject);
    }

    /// <summary>A status with no record to say who set it is set by the import —
    /// never this host, which would read "set by You" — at the commit time the
    /// caller looks up.</summary>
    [Fact]
    public void A_draft_proposal_has_no_record_and_takes_the_commit_time()
    {
        var draft = Assert.Single(Plan().Subjects, subject => subject.Key == "split-payments");

        Assert.Equal("draft", draft.State);
        Assert.True(draft.NeedsCommitTime);
        Assert.Equal("openspec/changes/split-payments/proposal.md", draft.Path);

        var state = draft.ToState(Alias, Day(9, 19));

        Assert.Equal(DevbookMarkdownImport.ImportAuthor, state.By);
        Assert.Equal(Day(9, 19), state.At);
    }

    [Fact]
    public void Ordinary_chapters_resting_records_guidelines_indexes_and_archived_changes_plan_no_state()
    {
        var keys = Plan().Subjects.Select(subject => subject.Key).ToList();

        Assert.Equal(
            [
                ".devbook/arc42/adr/0001-pay-by-card.md",
                ".devbook/arc42/adr/0005-trace-every-order.md",
                ".devbook/arc42/tdr/0001-paging.md",
                "pay-by-card",
                "split-payments",
            ],
            keys);
    }

    /// <summary>A value outside its subject's row is no state the store can hold:
    /// passed over, and said so, with the reason.</summary>
    [Fact]
    public void A_state_outside_the_subjects_row_is_skipped_with_a_reason()
    {
        var skipped = Plan().Skipped;

        Assert.Equal(
            [
                ".devbook/arc42/adr/0004-retire-the-queue.md",
                ".devbook/arc42/tdr/0002-flaky-tests.md",
                "openspec/changes/gift-cards/proposal.md",
            ],
            skipped.Select(skip => skip.Path));
        Assert.Contains("deprecated", skipped[0].Reason);
        Assert.Contains("decided", skipped[1].Reason);
        Assert.Contains("fingerprint", skipped[2].Reason);
    }

    /// <summary>Each open fence is one remark: the fence's kind, the default
    /// branch, the fence's author and date, anchored to the block above it.</summary>
    [Fact]
    public void An_open_fence_becomes_a_remark_anchored_to_the_block_above_it()
    {
        var note = Plan().Notes.First(note => note.ChapterPath == ".arc42/01-introduction.md");

        Assert.Equal(5, note.BlockIndex);
        Assert.Equal(Digest("The shop is local first."), note.BlockHash);

        var remark = note.ToAnnotation(Alias, committedAt: null);

        Assert.Equal(DevbookAnnotationKind.Comment, remark.Kind);
        Assert.Null(remark.Branch);
        Assert.Equal("jobsc", remark.Author);
        Assert.Equal("Local first for the cart too?", remark.Body);
        Assert.Equal(Day(9, 2), remark.CreatedAt);
        Assert.Equal(Day(9, 2), remark.UpdatedAt);
        Assert.Equal(Alias, remark.RepositoryAlias);
        Assert.False(remark.Resolved);
        Assert.Null(remark.DeletedAt);
    }

    [Fact]
    public void A_question_keeps_its_kind_and_its_replies_fold_into_the_body()
    {
        var question = Plan().Notes.Single(note => note.ChapterPath == ".arc42/01-introduction.md" && note.Kind == DevbookAnnotationKind.Question);

        Assert.Equal(7, question.BlockIndex);
        Assert.Equal("Is the hour measured from payment\nor from placing the order?", question.Body);

        var remark = question.ToAnnotation(Alias, committedAt: null);

        Assert.Equal(
            "Is the hour measured from payment\nor from placing the order?\n\n— claude/flow-spec, 2026-09-04: From payment.",
            remark.Body);
        Assert.Equal(
            [new DevbookNoteReply("claude/flow-spec", "2026-09-04", "From payment.")],
            DevbookNoteReplies.Split(remark.Body).Replies);
    }

    /// <summary>A fence reply that names nobody or no readable date still folds
    /// in the shape the note tools read back as a reply: by <c>unknown</c>, on
    /// the note's own date — or the commit's, for a note with none.</summary>
    [Theory]
    [InlineData("date: 2026-09-03\n", null, "2026-09-03")]
    [InlineData("", "2026-09-20T10:00:00Z", "2026-09-20")]
    public void A_reply_naming_no_one_and_no_date_still_reads_back_as_a_reply(string fenceDate, string? committed, string at)
    {
        var root = Path.Combine(Path.GetTempPath(), "devbook-import-undated-" + Guid.NewGuid().ToString("N"));
        var chapter = Path.Combine(root, ".devbook", "arc42", "01-introduction.md");
        Directory.CreateDirectory(Path.GetDirectoryName(chapter)!);

        try
        {
            File.WriteAllText(
                chapter,
                "# Introduction\n\nOrders ship in an hour.\n\n```annotation\nkind: question\nauthor: jobsc\n" + fenceDate
                + "body: From payment?\nreplies:\n  - body: Yes, from payment.\n```\n");

            var note = Assert.Single(DevbookMarkdownImport.Plan(Alias, root, DevbookFolderSetting.Defaults()).Notes);
            var remark = note.ToAnnotation(Alias, committed is null ? null : DateTimeOffset.Parse(committed, System.Globalization.CultureInfo.InvariantCulture));

            var (body, replies) = DevbookNoteReplies.Split(remark.Body);

            Assert.Equal("From payment?", body);
            Assert.Equal([new DevbookNoteReply(DevbookMarkdownImport.UnknownReplyAuthor, at, "Yes, from payment.")], replies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_unknown_kind_reads_as_a_comment_and_a_resolved_fence_imports_nothing()
    {
        var introduction = Plan().Notes.Where(note => note.ChapterPath == ".arc42/01-introduction.md").ToList();

        Assert.Equal(
            [DevbookAnnotationKind.Comment, DevbookAnnotationKind.Question, DevbookAnnotationKind.Comment],
            introduction.Select(note => note.Kind));
        Assert.DoesNotContain(introduction, note => note.Body.Contains("swept", StringComparison.Ordinal));
        Assert.Equal(10, introduction[2].BlockIndex);
    }

    /// <summary>Consecutive fences all annotate the passage above the first of
    /// them, per the fence's own placement rule.</summary>
    [Fact]
    public void Stacked_fences_all_anchor_to_the_passage_above_them()
    {
        var shop = Plan().Notes.Where(note => note.ChapterPath == ".domain/shop/domain.md").ToList();

        Assert.Equal([DevbookAnnotationKind.Flag, DevbookAnnotationKind.Suggestion], shop.Select(note => note.Kind));
        Assert.All(shop, note => Assert.Equal(4, note.BlockIndex));
        Assert.All(shop, note => Assert.Equal(Digest("An order is placed once and paid once."), note.BlockHash));
    }

    [Fact]
    public void A_note_on_a_change_is_kept_under_its_path_and_an_archived_change_imports_none()
    {
        var notes = Plan().Notes;

        Assert.Equal(6, notes.Count);
        var question = Assert.Single(notes, note => note.ChapterPath.StartsWith("openspec/", StringComparison.Ordinal));
        Assert.Equal("openspec/changes/split-payments/proposal.md", question.ChapterPath);
        Assert.Equal(DevbookAnnotationKind.Question, question.Kind);
        Assert.DoesNotContain(notes, note => note.ChapterPath.Contains("archive", StringComparison.Ordinal));
    }

    /// <summary>Every device imports for itself, so the ids have to be the same on
    /// each of them — the same plan twice, the same ids — and one per note.</summary>
    [Fact]
    public void The_ids_are_the_same_on_a_second_plan_and_one_per_note()
    {
        var first = Plan();
        var second = Plan();

        Assert.Equal(first.Notes.Select(note => note.Id), second.Notes.Select(note => note.Id));
        Assert.Equal(first.Notes.Count, first.Notes.Select(note => note.Id).Distinct().Count());
        Assert.Equal(
            first.Subjects.Select(subject => subject.ToState(Alias, Day(9, 1), subject.NeedsPullRequest ? 1 : null).Id),
            second.Subjects.Select(subject => subject.ToState(Alias, Day(9, 1), subject.NeedsPullRequest ? 1 : null).Id));
    }

    /// <summary>The import reads only: nothing in the repository it planned from
    /// changes.</summary>
    [Fact]
    public void Planning_writes_nothing_into_the_repository()
    {
        var before = Snapshot();

        Plan();

        Assert.Equal(before, Snapshot());

        static string Snapshot() => string.Join(
            "\n",
            Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => path + "|" + File.GetLastWriteTimeUtc(path).Ticks));
    }

    [Fact]
    public void The_snapshot_selection_names_both_layouts_and_the_changes_but_not_their_archive()
    {
        var selection = DevbookMarkdownImport.Selection(DevbookFolderSetting.Defaults());

        Assert.Contains(".devbook/arc42/", selection);
        Assert.Contains(".arc42/", selection);
        Assert.Contains(".devbook/domain/", selection);
        Assert.Contains(".domain/", selection);
        Assert.Contains("openspec/changes/", selection);
        Assert.Contains("!archive", selection);
        Assert.DoesNotContain("/", selection);
        Assert.Contains("!" + DevbookSnapshotSelection.RenderedArtifactsFolder, selection);
    }

    /// <summary>A reply written into a fence later — <c>handle-remarks</c> does — is
    /// the same note: two devices importing either side of it file one remark, not
    /// two. The id is the note's own fields, never the thread under it.</summary>
    [Fact]
    public void A_reply_added_to_a_note_does_not_change_its_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "devbook-import-reply-" + Guid.NewGuid().ToString("N"));
        var chapter = Path.Combine(root, ".devbook", "arc42", "01-introduction.md");
        Directory.CreateDirectory(Path.GetDirectoryName(chapter)!);

        try
        {
            const string Before = "# Introduction\n\nOrders ship in an hour.\n\n```annotation\nkind: question\nauthor: jobsc\ndate: 2026-09-03\nbody: From payment?\n```\n";
            const string After = "# Introduction\n\nOrders ship in an hour.\n\n```annotation\nkind: question\nauthor: jobsc\ndate: 2026-09-03\nbody: From payment?\nreplies:\n  - author: claude/handle-remarks\n    date: 2026-09-04\n    body: Yes.\n```\n";

            File.WriteAllText(chapter, Before);
            var before = Assert.Single(DevbookMarkdownImport.Plan(Alias, root, DevbookFolderSetting.Defaults()).Notes);

            File.WriteAllText(chapter, After);
            var after = Assert.Single(DevbookMarkdownImport.Plan(Alias, root, DevbookFolderSetting.Defaults()).Notes);

            Assert.Equal(before.Id, after.Id);
            Assert.NotEqual(before.ToAnnotation(Alias, null).Body, after.ToAnnotation(Alias, null).Body);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Eight hex characters of SHA-256 over a paragraph's text — the anchor
    /// digest's documented shape, taken here from the literal sentence.</summary>
    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
