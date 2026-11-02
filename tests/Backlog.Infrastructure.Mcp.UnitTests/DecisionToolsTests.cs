using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The four state operations of <c>devbook.decision@1</c> — <c>devbook_get_state</c>,
/// <c>devbook_set_state</c>, <c>devbook_approve</c>, <c>devbook_accept</c> — as a
/// caller meets them over MCP.
/// <para>
/// What a caller is promised is mostly about shape and refusal: the answer is
/// <c>{ state }</c>, plus <c>approval</c> and <c>acceptance</c> for a proposal, or
/// <c>{ refused, message }</c> with one of the contract's codes, and a refusal
/// writes nothing. Who decided and when are the host's, never the caller's. So
/// the tests read the answer as the caller would and the store as the app would,
/// and the rules themselves are the shared ones (<see cref="DevbookSubjectRules"/>),
/// applied by the double.
/// </para>
/// </summary>
public sealed class DecisionToolsTests
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Fingerprint = "sha256:0a1b2c3d";
    private const string DebtPath = ".devbook/arc42/tdr/0003-sqlite-without-migrations.md";
    private const string DecisionPath = ".devbook/arc42/adr/0044-devbook-state-outside-git.md";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    /// <summary>The host's clock, at a moment whose ISO 8601 spelling the tests
    /// write out by hand.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 15, 0, TimeSpan.Zero);

    private static DecisionTools Tools(
        out FakeDevbookSubjectStateStore store,
        TasksRepositoryRef? repository = null,
        IDevbookFolderSource? folders = null)
    {
        store = new FakeDevbookSubjectStateStore(new FakeTimeProvider(Now));

        // No folder configured and none on disk: the conventional reading, keyed
        // by the path as sent, which is what a host that cannot resolve the folder
        // does.
        return new DecisionTools(
            store,
            new FakeRepositoryDirectory([repository ?? Backlog]),
            folders ?? new FakeDevbookFolderSource(Path.GetTempPath()));
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
    public void Setting_a_proposal_to_draft_keeps_it_under_the_repositorys_alias_by_this_host()
    {
        var tools = Tools(out var store);

        var answer = tools.SetStateOf(Repository, Proposal(), "draft");

        Assert.Equal("draft", answer.State);

        var kept = Assert.Single(store.Written);
        Assert.Equal("backlog", kept.RepositoryAlias);
        Assert.Equal(DevbookSubject.Proposal("add-decision-tools"), kept.Subject);
        Assert.Equal(Environment.MachineName, kept.By);
        Assert.Equal(Now, kept.At);
    }

    [Fact]
    public void Approving_a_proposal_records_who_when_and_at_which_fingerprint()
    {
        var tools = Tools(out _);

        var answer = tools.ApproveProposal(Repository, Proposal(), Fingerprint);

        Assert.Equal("approved", answer.State);
        Assert.Equal(new DevbookDecisionPayload(Environment.MachineName, "2026-10-30T09:15:00Z", Fingerprint), answer.Approval);
        Assert.Null(answer.Acceptance);
    }

    [Fact]
    public void Accepting_at_the_approved_fingerprint_moves_the_proposal_to_accepted()
    {
        var tools = Tools(out _);
        tools.ApproveProposal(Repository, Proposal(), Fingerprint);

        var answer = tools.AcceptProposal(Repository, Proposal(), Fingerprint);

        Assert.Equal("accepted", answer.State);
        Assert.Equal(Fingerprint, answer.Approval?.Fingerprint);
        Assert.Equal(new DevbookDecisionPayload(Environment.MachineName, "2026-10-30T09:15:00Z", Fingerprint), answer.Acceptance);

        // And get_state answers it the same way, which is what set_state, approve
        // and accept promise: "the state as get_state answers it".
        Assert.Equal(Wire(answer), Wire(tools.GetStateOf(Repository, Proposal())));
    }

    [Fact]
    public void Accepting_with_no_approval_is_refused_not_approved_and_writes_nothing()
    {
        var tools = Tools(out var store);

        var answer = tools.AcceptProposal(Repository, Proposal(), Fingerprint);

        Assert.Equal("not-approved", answer.Refused);
        Assert.Empty(store.Written);
    }

    [Fact]
    public void Accepting_at_another_fingerprint_is_refused_fingerprint_mismatch()
    {
        var tools = Tools(out var store);
        tools.ApproveProposal(Repository, Proposal(), Fingerprint);

        var answer = tools.AcceptProposal(Repository, Proposal(), "sha256:ffffffff");

        Assert.Equal("fingerprint-mismatch", answer.Refused);
        Assert.Single(store.Written);
        Assert.Equal("approved", tools.GetStateOf(Repository, Proposal()).State);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("accepted")]
    [InlineData("planned")]
    public void Set_state_refuses_a_state_outside_the_row_or_reached_only_by_a_decision(string state)
    {
        var tools = Tools(out var store);

        var answer = tools.SetStateOf(Repository, Proposal(), state);

        Assert.Equal("bad-state", answer.Refused);
        Assert.False(string.IsNullOrWhiteSpace(answer.Message));
        Assert.Empty(store.Written);
    }

    [Fact]
    public void Approve_and_accept_take_a_proposal_alone()
    {
        var tools = Tools(out var store);

        Assert.Equal("bad-state", tools.ApproveProposal(Repository, Debt(), Fingerprint).Refused);
        Assert.Equal("bad-state", tools.AcceptProposal(Repository, Decision(12), Fingerprint).Refused);
        Assert.Empty(store.Written);
    }

    [Fact]
    public void A_fingerprint_that_is_not_one_is_refused_bad_state()
    {
        var tools = Tools(out var store);

        var answer = tools.ApproveProposal(Repository, Proposal(), "0a1b2c3d");

        Assert.Equal("bad-state", answer.Refused);
        Assert.Empty(store.Written);
    }

    [Fact]
    public void A_debt_records_state_is_answered_without_a_proposals_decisions()
    {
        var tools = Tools(out _);

        var answer = tools.SetStateOf(Repository, Debt(), "planned");

        Assert.Equal("planned", answer.State);
        Assert.Equal("""{"state":"planned"}""", Wire(answer));
    }

    [Fact]
    public void A_decision_record_is_keyed_by_its_pull_request_too()
    {
        var tools = Tools(out var store);

        tools.SetStateOf(Repository, Decision(12), "decided");

        Assert.Equal("decided", tools.GetStateOf(Repository, Decision(12)).State);
        Assert.Null(tools.GetStateOf(Repository, Decision(13)).State);
        Assert.Equal(DevbookSubject.Decision(DecisionPath, 12), Assert.Single(store.Written).Subject);
    }

    [Fact]
    public void A_refusal_reaches_the_wire_as_refused_and_message_alone()
    {
        var tools = Tools(out _);

        var answer = tools.AcceptProposal(Repository, Proposal(), Fingerprint);

        using var document = JsonDocument.Parse(Wire(answer));
        Assert.Equal(
            ["message", "refused"],
            [.. document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
        Assert.Equal("not-approved", document.RootElement.GetProperty("refused").GetString());
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
                tools.SetStateOf(Repository, subject, "draft"),
                tools.ApproveProposal(Repository, subject, Fingerprint),
                tools.AcceptProposal(Repository, subject, Fingerprint)
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

        var answer = tools.SetStateOf(Repository, Debt("docs/arch/tdr/0003-x.md"), "identified");

        Assert.Equal("identified", answer.State);
        Assert.Equal(DevbookSubject.Debt("docs/arch/tdr/0003-x.md"), Assert.Single(store.Written).Subject);
    }

    /// <summary>
    /// The classification forgives case and the legacy layout; the key does not,
    /// so each spelling of one record would otherwise be a subject of its own.
    /// Every spelling is filed under the path the repository has — the one the
    /// arc42 panel keys the record's state by — so the second write lands on the
    /// first one's state.
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

            tools.SetStateOf(Repository, Debt(".devbook/arc42/tdr/0003-paging.md"), "identified");
            var answer = tools.SetStateOf(Repository, Debt(spelled), "planned");

            Assert.Equal("planned", answer.State);
            Assert.All(store.Written, written => Assert.Equal(mine, written.Subject));
            Assert.Equal("planned", tools.GetStateOf(Repository, Debt(".devbook/arc42/tdr/0003-paging.md")).State);
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

            tools.SetStateOf(Repository, Decision(12, ".arc42/adr/0044-devbook-state-outside-git.md"), "decided");

            Assert.Equal(DevbookSubject.Decision(DecisionPath, 12), Assert.Single(store.Written).Subject);
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

        Assert.All(
            new Action[]
            {
                () => tools.SetStateOf(Repository, Debt(), "resolved"),
                () => tools.ApproveProposal(Repository, Proposal(), Fingerprint),
                () => tools.AcceptProposal(Repository, Proposal(), Fingerprint)
            },
            write => Assert.Contains(
                DevbookTools.ArchivedCode,
                Assert.Throws<McpException>(write).Message,
                StringComparison.Ordinal));

        Assert.Empty(store.Written);
    }

    [Fact]
    public void The_brain_keeps_no_workflow_state_and_is_refused()
    {
        var tools = Tools(out var store);

        var failures = new[]
        {
            Assert.Throws<McpException>(() => tools.GetStateOf("#brain", Proposal())),
            Assert.Throws<McpException>(() => tools.SetStateOf("#brain", Proposal(), "draft")),
            Assert.Throws<McpException>(() => tools.ApproveProposal("#brain", Proposal(), Fingerprint)),
            Assert.Throws<McpException>(() => tools.AcceptProposal("#brain", Proposal(), Fingerprint))
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

        Assert.All(
            new Action[]
            {
                () => tools.SetStateOf(Repository, Debt(), "resolved"),
                () => tools.ApproveProposal(Repository, Proposal(), Fingerprint),
                () => tools.AcceptProposal(Repository, Proposal(), Fingerprint)
            },
            write => Assert.Contains(
                DevbookTools.ArchivedCode,
                Assert.Throws<McpException>(write).Message,
                StringComparison.Ordinal));

        Assert.Empty(store.Written);
    }

    /// <summary>The answer as the SDK puts it on the wire: its own serializer
    /// options, which is what the converter on the payload has to hold under.</summary>
    private static string Wire(DevbookStatePayload answer) =>
        JsonSerializer.Serialize(answer, McpJsonUtilities.DefaultOptions);
}
