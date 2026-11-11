using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The operations of <c>devbook.decision@1</c> Backlog serves over MCP —
/// <c>devbook_get_state</c>, <c>devbook_set_state</c> and the three note tools — as
/// a caller meets them. Approve and accept are the person's, in the app (local ADR
/// 0052 §7); <c>DevbookProposalDecisionTests</c> holds them.
/// <para>
/// What a caller is promised is mostly about shape and refusal: the answer is
/// <c>{ state }</c>, plus <c>approval</c> and <c>acceptance</c> for a proposal, or
/// <c>{ refused, message }</c> with one of the contract's codes, and a refusal
/// writes nothing. Who and when are the host's, never the caller's: every write
/// here is stamped <see cref="DevbookLocalAuthor.AgentViaMcp"/>. So
/// the tests read the answer as the caller would and the store as the app would,
/// and the rules themselves are the shared ones (<see cref="DevbookSubjectRules"/>),
/// applied by the double.
/// </para>
/// </summary>
public sealed class DecisionToolsTests
{
    private const string Repository = "JSdotNet/Backlog";
    private const string DebtPath = ".devbook/arc42/tdr/0003-sqlite-without-migrations.md";
    private const string DecisionPath = ".devbook/arc42/adr/0044-devbook-state-outside-git.md";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    /// <summary>The host's clock, at a moment whose ISO 8601 spelling the tests
    /// write out by hand.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 15, 0, TimeSpan.Zero);

    private static DecisionTools Tools(
        out FakeDevbookSubjectStateStore store,
        TasksRepositoryRef? repository = null,
        IDevbookFolderSource? folders = null) =>
        Tools(out store, out _, repository, folders);

    private static DecisionTools Tools(
        out FakeDevbookSubjectStateStore store,
        out FakeDevbookAnnotationStore notes,
        TasksRepositoryRef? repository = null,
        IDevbookFolderSource? folders = null,
        params DevbookAnnotation[] seeded)
    {
        store = new FakeDevbookSubjectStateStore(new FakeTimeProvider(Now));
        notes = new FakeDevbookAnnotationStore(seeded) { WritesAt = Now };

        // No folder configured and none on disk: the conventional reading, keyed
        // by the path as sent, which is what a host that cannot resolve the folder
        // does.
        return new DecisionTools(
            store,
            new FakeRepositoryDirectory([repository ?? Backlog]),
            folders ?? new FakeDevbookFolderSource(Path.GetTempPath()),
            notes,
            new FakeTimeProvider(Now));
    }

    private static DevbookSubjectArgument Proposal(string name = "add-decision-tools") => new("proposal", Name: name);

    private static DevbookSubjectArgument Debt(string path = DebtPath) => new("debt", Path: path);

    private static DevbookSubjectArgument Decision(int pullRequest, string path = DecisionPath) =>
        new("decision", Path: path, PullRequest: pullRequest);

    [Fact]
    public void A_proposal_the_host_keeps_nothing_for_answers_no_state_and_no_decisions()
    {
        var tools = Tools(out _);

        var answer = tools.GetStateOf(Repository, Proposal());

        Assert.False(answer.IsRefused);
        Assert.Null(answer.State);
        Assert.Null(answer.Approval);
        Assert.Null(answer.Acceptance);
        Assert.Equal("""{"state":null,"approval":null,"acceptance":null}""", Wire(answer));
    }

    [Fact]
    public void Setting_a_proposal_to_draft_keeps_it_under_the_repositorys_alias_by_agent_via_mcp()
    {
        var tools = Tools(out var store);

        var answer = tools.SetStateOf(Repository, Proposal(), "draft");

        Assert.Equal("draft", answer.State);

        var kept = Assert.Single(store.Written);
        Assert.Equal("backlog", kept.RepositoryAlias);
        Assert.Equal(DevbookSubject.Proposal("add-decision-tools"), kept.Subject);
        Assert.Equal(DevbookLocalAuthor.AgentViaMcp, kept.By);
        Assert.NotEqual(DevbookLocalAuthor.Name, kept.By);
        Assert.Equal(Now, kept.At);
    }

    /// <summary>
    /// Every state but draft is a decision, and decisions are the person's in the
    /// app: refused <c>bad-state</c> with a message saying so, before the store is
    /// asked, for every kind of subject.
    /// </summary>
    [Theory]
    [InlineData("proposal", "approved")]
    [InlineData("proposal", "accepted")]
    [InlineData("proposal", "Draft")]
    [InlineData("proposal", "planned")]
    [InlineData("decision", "proposed")]
    [InlineData("decision", "decided")]
    [InlineData("decision", "declined")]
    [InlineData("debt", "identified")]
    [InlineData("debt", "planned")]
    [InlineData("debt", "in-progress")]
    [InlineData("debt", "resolved")]
    public void Set_state_over_mcp_refuses_every_state_but_draft_and_writes_nothing(string kind, string state)
    {
        var tools = Tools(out var store);
        var subject = kind switch
        {
            "proposal" => Proposal(),
            "decision" => Decision(12),
            _ => Debt()
        };

        var answer = tools.SetStateOf(Repository, subject, state);

        Assert.Equal("bad-state", answer.Refused);
        Assert.Equal(DecisionTools.DecisionsInTheAppMessage, answer.Message);
        Assert.Contains("Backlog app", answer.Message, StringComparison.Ordinal);
        Assert.Empty(store.Calls);
    }

    /// <summary>Draft on a record whose row has none is the store's own
    /// <c>bad-state</c>: the shared rules still answer what the row holds.</summary>
    [Fact]
    public void Draft_on_a_record_is_refused_by_the_rules_and_writes_nothing()
    {
        var tools = Tools(out var store);

        Assert.Equal("bad-state", tools.SetStateOf(Repository, Debt(), "draft").Refused);
        Assert.Empty(store.Written);
    }

    [Fact]
    public void A_debt_records_state_is_answered_without_a_proposals_decisions()
    {
        var tools = Tools(out var store);
        store.Seed(Kept(DevbookSubject.Debt(DebtPath), "planned"));

        var answer = tools.GetStateOf(Repository, Debt());

        Assert.Equal("planned", answer.State);
        Assert.Equal("""{"state":"planned"}""", Wire(answer));
    }

    [Fact]
    public void A_decision_record_is_keyed_by_its_pull_request_too()
    {
        var tools = Tools(out var store);
        store.Seed(Kept(DevbookSubject.Decision(DecisionPath, 12), "decided"));

        Assert.Equal("decided", tools.GetStateOf(Repository, Decision(12)).State);
        Assert.Null(tools.GetStateOf(Repository, Decision(13)).State);
    }

    [Fact]
    public void A_refusal_reaches_the_wire_as_refused_and_message_alone()
    {
        var tools = Tools(out _);

        var answer = tools.SetStateOf(Repository, Proposal(), "approved");

        using var document = JsonDocument.Parse(Wire(answer));
        Assert.Equal(
            ["message", "refused"],
            [.. document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
        Assert.Equal("bad-state", document.RootElement.GetProperty("refused").GetString());
    }

    /// <summary>
    /// A subject the contract could not name is answered <c>bad-state</c> before
    /// the store is asked — never thrown, so the caller reads one more refusal
    /// rather than the SDK's "an error occurred".
    /// </summary>
    [Theory]
    [InlineData("Proposal", "add-decision-tools", null, null)]
    [InlineData("chapter", null, DebtPath, null)]
    [InlineData("proposal", null, null, null)]
    [InlineData("proposal", "  ", null, null)]
    [InlineData("proposal", "openspec/changes/add-decision-tools", null, null)]
    [InlineData("proposal", "..", null, null)]
    [InlineData("proposal", "add..tools", null, null)]
    [InlineData("proposal", "archive", null, null)]
    [InlineData("proposal", "add-decision-tools", DebtPath, null)]
    [InlineData("proposal", "add-decision-tools", null, 12)]
    [InlineData("debt", null, null, null)]
    [InlineData("debt", null, DebtPath, 12)]
    [InlineData("debt", "add-decision-tools", DebtPath, null)]
    [InlineData("debt", null, DecisionPath, null)]
    [InlineData("debt", null, ".devbook/arc42/05-building-block-view.md", null)]
    [InlineData("decision", null, DecisionPath, null)]
    [InlineData("decision", null, DecisionPath, 0)]
    [InlineData("decision", null, DecisionPath, -3)]
    [InlineData("decision", null, DebtPath, 12)]
    [InlineData("decision", null, ".devbook/arc42/adr/guidelines/0001-naming.md", 12)]
    [InlineData("decision", null, ".devbook/arc42/adr/README.md", 12)]
    public void A_subject_the_contract_cannot_name_is_refused_bad_state_and_the_store_is_not_asked(
        string kind, string? name, string? path, int? pullRequest)
    {
        var tools = Tools(out var store);
        var subject = new DevbookSubjectArgument(kind, name, path, pullRequest);

        Assert.All(
            [
                tools.GetStateOf(Repository, subject),
                tools.SetStateOf(Repository, subject, "draft")
            ],
            answer =>
            {
                Assert.Equal("bad-state", answer.Refused);
                Assert.False(string.IsNullOrWhiteSpace(answer.Message));
            });

        Assert.Empty(store.Calls);
    }

    [Fact]
    public void No_subject_at_all_is_refused_bad_state()
    {
        var tools = Tools(out var store);

        Assert.Equal("bad-state", tools.GetStateOf(Repository, null!).Refused);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void A_record_under_a_relocated_folder_is_a_subject_by_its_repository_path()
    {
        var folders = new FakeDevbookFolderSource(
            Path.GetTempPath(),
            new DevbookFolderSetting(".arc42", "Architecture", ".arc42") { Path = "docs/arch" });
        var tools = Tools(out var store, folders: folders);
        store.Seed(Kept(DevbookSubject.Debt("docs/arch/tdr/0003-x.md"), "identified"));

        var answer = tools.GetStateOf(Repository, Debt("docs/arch/tdr/0003-x.md"));

        Assert.Equal("identified", answer.State);
        Assert.Equal(DevbookSubject.Debt("docs/arch/tdr/0003-x.md"), Assert.Single(store.Calls).Subject);
    }

    /// <summary>
    /// The classification forgives case and the legacy layout; the key does not,
    /// so each spelling of one record would otherwise be a subject of its own.
    /// Every spelling is filed under the path the repository has — the one the
    /// arc42 panel keys the record's state by — so every spelling reads the state
    /// the panel set.
    /// </summary>
    [Theory]
    [InlineData(".devbook/arc42/tdr/0003-paging.md")]
    [InlineData(".arc42/tdr/0003-paging.md")]
    [InlineData(".devbook/arc42/TDR/0003-PAGING.md")]
    [InlineData(@".devbook\arc42\tdr\0003-paging.md")]
    public void Every_spelling_of_a_record_is_filed_under_the_repositorys_own(string spelled)
    {
        var root = Directory.CreateTempSubdirectory("decision-tools-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".devbook", "arc42", "tdr"));
            File.WriteAllText(Path.Combine(root, ".devbook", "arc42", "tdr", "0003-paging.md"), "# Paging\n");
            var folders = new FakeDevbookFolderSource(root, new DevbookFolderSetting(".arc42", "Architecture", ".devbook/arc42"));
            var tools = Tools(out var store, folders: folders);
            var mine = DevbookSubject.Debt(".devbook/arc42/tdr/0003-paging.md");
            store.Seed(Kept(mine, "planned"));

            var answer = tools.GetStateOf(Repository, Debt(spelled));

            Assert.Equal("planned", answer.State);
            Assert.Equal(mine, Assert.Single(store.Calls).Subject);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A decision record keys by the folder's spelling too, its pull
    /// request alongside.</summary>
    [Fact]
    public void A_decision_record_sent_in_the_legacy_layout_is_filed_under_the_repositorys_own()
    {
        var root = Directory.CreateTempSubdirectory("decision-tools-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".devbook", "arc42", "adr"));
            var folders = new FakeDevbookFolderSource(root, new DevbookFolderSetting(".arc42", "Architecture", ".devbook/arc42"));
            var tools = Tools(out var store, folders: folders);

            tools.GetStateOf(Repository, Decision(12, ".arc42/adr/0044-devbook-state-outside-git.md"));

            Assert.Equal(DevbookSubject.Decision(DecisionPath, 12), Assert.Single(store.Calls).Subject);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A store that answers an archived repository as an outcome reads
    /// over MCP exactly as the check before the store does: thrown as
    /// <c>repository.archived</c>, never put in <c>{ refused }</c>, whose codes
    /// are the contract's.</summary>
    [Fact]
    public void An_archived_outcome_is_answered_as_repository_archived()
    {
        var tools = Tools(out var store);
        store.AnswersArchived = true;

        var failure = Assert.Throws<McpException>(() => tools.SetStateOf(Repository, Proposal(), "draft"));

        Assert.Contains(DevbookTools.ArchivedCode, failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Written);
    }

    [Fact]
    public void The_brain_keeps_no_workflow_state_and_is_refused()
    {
        var tools = Tools(out var store);

        var failures = new[]
        {
            Assert.Throws<McpException>(() => tools.GetStateOf("#brain", Proposal())),
            Assert.Throws<McpException>(() => tools.SetStateOf("#brain", Proposal(), "draft"))
        };

        Assert.All(failures, failure => Assert.Contains(DecisionTools.BrainCode, failure.Message, StringComparison.Ordinal));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void A_repository_the_directory_does_not_know_is_refused()
    {
        var tools = Tools(out var store);

        var failure = Assert.Throws<McpException>(() => tools.GetStateOf("JSdotNet/Elsewhere", Proposal()));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void An_archived_repository_is_read_and_never_written()
    {
        var tools = Tools(out var store, Backlog with { IsArchived = true });
        store.Seed(new DevbookSubjectState(
            DevbookSubject.IdFor("backlog", DevbookSubject.Debt(DebtPath)),
            "backlog",
            DevbookSubject.Debt(DebtPath),
            "planned",
            "SOMEMACHINE",
            Now.AddDays(-3),
            Approval: null,
            Acceptance: null,
            UpdatedAt: Now.AddDays(-3)));

        Assert.Equal("planned", tools.GetStateOf(Repository, Debt()).State);

        var failure = Assert.Throws<McpException>(() => tools.SetStateOf(Repository, Proposal(), "draft"));

        Assert.Contains(DevbookTools.ArchivedCode, failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Written);
    }

    private const string ChangeProposal = "openspec/changes/add-decision-tools/proposal.md";

    [Fact]
    public void Posting_a_note_answers_it_in_the_contracts_shape_by_agent_via_mcp_on_the_default_branch()
    {
        var tools = Tools(out _, out var notes);

        var answer = tools.PostNoteOn(Repository, ChangeProposal, "Is the fee ours to keep?", kind: "question");

        Assert.False(answer.IsRefused);
        var note = answer.Note!;
        Assert.Equal(ChangeProposal, note.Address);
        Assert.Equal("question", note.Kind);
        Assert.Equal("open", note.Status);
        Assert.Equal("Is the fee ours to keep?", note.Body);
        Assert.Equal(DevbookLocalAuthor.AgentViaMcp, note.By);
        Assert.Equal("2026-10-30T09:15:00Z", note.At);
        Assert.Empty(note.Replies);

        var kept = Assert.Single(notes.All);
        Assert.Equal(DevbookLocalAuthor.AgentViaMcp, kept.Author);
        Assert.Equal(Guid.Parse(note.Id), kept.Id);
        Assert.Equal("backlog", kept.RepositoryAlias);
        Assert.Equal(DevbookAnnotationKind.Question, kept.Kind);
        Assert.Null(kept.Branch);
        Assert.False(kept.IsDraft);

        using var document = JsonDocument.Parse(Wire(answer));
        Assert.Equal(
            ["address", "at", "body", "by", "id", "kind", "replies", "status"],
            [.. document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void A_note_posted_without_a_kind_is_a_comment() =>
        Assert.Equal("comment", Tools(out _, out _).PostNoteOn(Repository, ChangeProposal, "Reads well.").Note!.Kind);

    /// <summary>A <c>#slug</c> address lands on that heading's block when the
    /// chapter can be read, and the note answers that address back; listing the
    /// chapter or that section finds it, another section does not.</summary>
    [Fact]
    public void A_note_posted_at_a_section_is_anchored_to_its_heading()
    {
        var root = Directory.CreateTempSubdirectory("decision-notes-").FullName;
        try
        {
            const string Chapter = ".devbook/arc42/06-runtime-view.md";
            Directory.CreateDirectory(Path.Combine(root, ".devbook", "arc42"));
            File.WriteAllText(
                Path.Combine(root, ".devbook", "arc42", "06-runtime-view.md"),
                "# Runtime view\n\nIntro.\n\n## Import on start\n\nThe worker runs once.\n\n## Sync\n\nLater.\n");
            var folders = new FakeDevbookFolderSource(root, new DevbookFolderSetting(".arc42", "Architecture", ".devbook/arc42"));
            var tools = Tools(out _, out var notes, folders: folders);

            var note = tools.PostNoteOn(Repository, Chapter + "#import-on-start", "Once per device?").Note!;

            Assert.Equal(Chapter + "#import-on-start", note.Address);
            Assert.Equal(2, Assert.Single(notes.All).BlockIndex);

            Assert.Equal([note.Id], tools.ListNotesOn(Repository, Chapter).Notes.Select(listed => listed.Id));
            Assert.Equal([note.Id], tools.ListNotesOn(Repository, Chapter + "#import-on-start").Notes.Select(listed => listed.Id));
            Assert.Empty(tools.ListNotesOn(Repository, Chapter + "#sync").Notes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A chapter this host cannot read has no headings to find, so the
    /// note is anchored to the chapter as a whole and says so in its
    /// address.</summary>
    [Fact]
    public void A_note_on_a_section_of_a_chapter_that_cannot_be_read_is_anchored_to_the_chapter()
    {
        var tools = Tools(out _, out var notes);

        var note = tools.PostNoteOn(Repository, ChangeProposal + "#why", "Why now?").Note!;

        Assert.Equal(ChangeProposal, note.Address);
        Assert.Equal(0, Assert.Single(notes.All).BlockIndex);
    }

    /// <summary>
    /// A reply is answered as one of the note's <c>replies</c>, <c>{ by, at,
    /// body }</c>, by agent via MCP and at this host's clock, and the note's own <c>body</c> is
    /// unchanged. The store keeps no thread: the reply is folded into the stored
    /// body, and read back out of it when the note is answered.
    /// </summary>
    [Fact]
    public void A_reply_is_answered_as_one_of_the_notes_replies_and_its_body_is_unchanged()
    {
        var tools = Tools(out _, out var notes);
        var asked = tools.PostNoteOn(Repository, ChangeProposal, "Is the fee ours to keep?", kind: "question").Note!;

        var answer = tools.PostNoteOn(Repository, ChangeProposal, "Yes, per the card contract of 3 December, 2026-12-01: the fee.", replyTo: asked.Id);
        var again = tools.PostNoteOn(Repository, ChangeProposal, "Confirmed.\nBy finance.", replyTo: asked.Id).Note!;

        var note = answer.Note!;
        Assert.Equal(asked.Id, note.Id);
        Assert.Equal("question", note.Kind);
        Assert.Equal("Is the fee ours to keep?", note.Body);
        Assert.Equal(
            [new DevbookNoteReplyPayload(DevbookLocalAuthor.AgentViaMcp, "2026-10-30T09:15:00Z", "Yes, per the card contract of 3 December, 2026-12-01: the fee.")],
            note.Replies);

        Assert.Equal("Is the fee ours to keep?", again.Body);
        Assert.Equal(
            [
                new DevbookNoteReplyPayload(DevbookLocalAuthor.AgentViaMcp, "2026-10-30T09:15:00Z", "Yes, per the card contract of 3 December, 2026-12-01: the fee."),
                new DevbookNoteReplyPayload(DevbookLocalAuthor.AgentViaMcp, "2026-10-30T09:15:00Z", "Confirmed.\nBy finance.")
            ],
            again.Replies);
        Assert.Equal(again.Replies, Assert.Single(tools.ListNotesOn(Repository, ChangeProposal).Notes).Replies);
        Assert.Single(notes.All);

        using var document = JsonDocument.Parse(Wire(answer));
        var reply = Assert.Single(document.RootElement.GetProperty("replies").EnumerateArray());
        Assert.Equal(["at", "body", "by"], [.. reply.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
    }

    /// <summary>A note the one-time import brought in carries its fence's replies
    /// folded the same way, so it answers them as replies too — the fence's author
    /// and date as written, or <c>unknown</c> on the note's date where the fence
    /// named neither. An attribution the author ended a note with stays in its
    /// body.</summary>
    [Fact]
    public void An_imported_notes_folded_replies_are_answered_as_replies()
    {
        var imported = Notes.Note(
            "backlog",
            ChangeProposal,
            0,
            body: "Is the hour measured from payment\nor from placing the order?\n\n— claude/flow-spec, 2026-09-04: From payment.\n— unknown, 2026-09-03: Agreed.");
        var quoted = Notes.Note(
            "backlog",
            ChangeProposal,
            1,
            body: "Make it work, make it right, make it fast.\n\n— Kent Beck");
        var tools = Tools(out _, out _, seeded: [imported, quoted]);

        var notes = tools.ListNotesOn(Repository, ChangeProposal).Notes;

        var note = notes[0];
        Assert.Equal("Is the hour measured from payment\nor from placing the order?", note.Body);
        Assert.Equal(
            [
                new DevbookNoteReplyPayload("claude/flow-spec", "2026-09-04", "From payment."),
                new DevbookNoteReplyPayload("unknown", "2026-09-03", "Agreed.")
            ],
            note.Replies);

        Assert.Equal("Make it work, make it right, make it fast.\n\n— Kent Beck", notes[1].Body);
        Assert.Empty(notes[1].Replies);
    }

    /// <summary>A reply names its note by id and its address by path; a note at
    /// another chapter is not the one the caller meant, and is unknown
    /// there.</summary>
    [Fact]
    public void A_reply_to_a_note_on_another_chapter_is_refused_unknown_note()
    {
        var tools = Tools(out _, out var notes);
        var asked = tools.PostNoteOn(Repository, ChangeProposal, "Why now?", kind: "question").Note!;

        var answer = tools.PostNoteOn(Repository, ".devbook/arc42/06-runtime-view.md", "Because.", replyTo: asked.Id);

        Assert.Equal("unknown-note", answer.Refused);
        Assert.Equal("Why now?", Assert.Single(notes.All).Body);
    }

    [Theory]
    [InlineData("8d3f1e2a-0000-4000-8000-000000000001")]
    [InlineData("not-a-note")]
    public void A_reply_to_no_note_is_refused_unknown_note_and_writes_nothing(string replyTo)
    {
        var tools = Tools(out _, out var notes);

        var answer = tools.PostNoteOn(Repository, ChangeProposal, "Yes.", replyTo: replyTo);

        Assert.Equal("unknown-note", answer.Refused);
        Assert.False(string.IsNullOrWhiteSpace(answer.Message));
        Assert.Empty(notes.All);

        using var document = JsonDocument.Parse(Wire(answer));
        Assert.Equal(
            ["message", "refused"],
            [.. document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
    }

    /// <summary>Open and resolved notes are listed; a draft, a deleted note, one on
    /// a named branch and another repository's are not.</summary>
    [Fact]
    public void Listing_answers_the_default_branchs_notes_at_the_address()
    {
        var open = Notes.Note("backlog", ChangeProposal, 0, body: "Open.");
        var resolved = Notes.Note("backlog", ChangeProposal, 0, body: "Done.", resolved: true, createdAt: Now.AddMinutes(1));
        var tools = Tools(
            out _,
            out _,
            seeded:
            [
                open,
                resolved,
                Notes.Note("backlog", ChangeProposal, 0, body: string.Empty),
                Notes.Note("backlog", ChangeProposal, 0, deletedAt: Now),
                Notes.Note("backlog", ChangeProposal, 0) with { Branch = "feature/cards" },
                Notes.Note("other", ChangeProposal, 0)
            ]);

        var answer = tools.ListNotesOn(Repository, ChangeProposal);

        Assert.Equal([open.Id.ToString(), resolved.Id.ToString()], answer.Notes.Select(note => note.Id));
        Assert.Equal(["open", "resolved"], answer.Notes.Select(note => note.Status));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(answer, McpJsonUtilities.DefaultOptions));
        Assert.Equal(["notes"], [.. document.RootElement.EnumerateObject().Select(property => property.Name)]);
    }

    [Fact]
    public void Resolving_a_note_answers_it_resolved()
    {
        var tools = Tools(out _, out var notes);
        var posted = tools.PostNoteOn(Repository, ChangeProposal, "Why now?", kind: "question").Note!;

        var answer = tools.ResolveNoteFor(Repository, posted.Id);

        Assert.Equal("resolved", answer.Note!.Status);
        Assert.Equal(posted.Id, answer.Note.Id);
        Assert.True(Assert.Single(notes.All).Resolved);
    }

    [Theory]
    [InlineData("8d3f1e2a-0000-4000-8000-000000000001")]
    [InlineData("not-a-note")]
    public void Resolving_no_note_is_refused_unknown_note(string id)
    {
        var tools = Tools(out _, out var notes);

        Assert.Equal("unknown-note", tools.ResolveNoteFor(Repository, id).Refused);
        Assert.Empty(notes.Resolved);
    }

    [Fact]
    public void Another_repositorys_note_is_unknown_here()
    {
        var elsewhere = Notes.Note("other", ChangeProposal, 0);
        var tools = Tools(out _, out var notes, seeded: [elsewhere]);

        Assert.Equal("unknown-note", tools.ResolveNoteFor(Repository, elsewhere.Id.ToString()).Refused);
        Assert.Equal("unknown-note", tools.PostNoteOn(Repository, ChangeProposal, "Yes.", replyTo: elsewhere.Id.ToString()).Refused);
        Assert.Empty(notes.Resolved);
    }

    /// <summary>What is not the contract's question — no body, a kind outside the
    /// four, a kind and a reply at once, no address — is thrown, as a subject
    /// this host never reached is.</summary>
    [Fact]
    public void A_note_the_caller_has_not_said_is_thrown_and_writes_nothing()
    {
        var tools = Tools(out _, out var notes);

        Assert.Throws<McpException>(() => tools.PostNoteOn(Repository, ChangeProposal, "  "));
        Assert.Throws<McpException>(() => tools.PostNoteOn(Repository, ChangeProposal, "Hm.", kind: "remark"));
        Assert.Throws<McpException>(() => tools.PostNoteOn(Repository, ChangeProposal, "Hm.", kind: "question", replyTo: Guid.NewGuid().ToString()));
        Assert.Throws<McpException>(() => tools.PostNoteOn(Repository, "  ", "Hm."));
        Assert.Throws<McpException>(() => tools.ListNotesOn(Repository, "#slug-only"));

        Assert.Empty(notes.All);
    }

    [Fact]
    public void An_archived_repositorys_notes_are_listed_and_never_written()
    {
        var held = Notes.Note("backlog", ChangeProposal, 0);
        var tools = Tools(out _, out var notes, Backlog with { IsArchived = true }, seeded: [held]);

        Assert.Single(tools.ListNotesOn(Repository, ChangeProposal).Notes);

        Assert.All(
            new Action[]
            {
                () => tools.PostNoteOn(Repository, ChangeProposal, "Still?"),
                () => tools.PostNoteOn(Repository, ChangeProposal, "Yes.", replyTo: held.Id.ToString()),
                () => tools.ResolveNoteFor(Repository, held.Id.ToString())
            },
            write => Assert.Contains(
                DevbookTools.ArchivedCode,
                Assert.Throws<McpException>(write).Message,
                StringComparison.Ordinal));

        Assert.Single(notes.All);
        Assert.Empty(notes.Resolved);
    }

    [Fact]
    public void The_brain_keeps_no_review_notes_and_is_refused()
    {
        var tools = Tools(out _, out var notes);

        var failures = new[]
        {
            Assert.Throws<McpException>(() => tools.ListNotesOn("#brain", "projects/plan.md")),
            Assert.Throws<McpException>(() => tools.PostNoteOn("#brain", "projects/plan.md", "Hm.")),
            Assert.Throws<McpException>(() => tools.ResolveNoteFor("#brain", Guid.NewGuid().ToString()))
        };

        Assert.All(failures, failure => Assert.Contains(DecisionTools.BrainCode, failure.Message, StringComparison.Ordinal));
        Assert.Empty(notes.All);
    }

    /// <summary>A state the app set for <paramref name="subject"/>, as the store
    /// keeps it.</summary>
    private static DevbookSubjectState Kept(DevbookSubject subject, string state) => new(
        DevbookSubject.IdFor("backlog", subject),
        "backlog",
        subject,
        state,
        DevbookLocalAuthor.Name,
        Now.AddDays(-1),
        Approval: null,
        Acceptance: null,
        UpdatedAt: Now.AddDays(-1));

    /// <summary>The answer as the SDK puts it on the wire: its own serializer
    /// options, which is what the converter on the payload has to hold under.</summary>
    private static string Wire(DevbookStatePayload answer) =>
        JsonSerializer.Serialize(answer, McpJsonUtilities.DefaultOptions);

    private static string Wire(DevbookNoteAnswer answer) =>
        JsonSerializer.Serialize(answer, McpJsonUtilities.DefaultOptions);
}
