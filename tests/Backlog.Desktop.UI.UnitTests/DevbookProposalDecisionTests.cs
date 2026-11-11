using Backlog.Infrastructure.Devbook.Fingerprints;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The person's approval and acceptance of a change proposal, which local ADR 0052
/// §7 moved off MCP into the devbook view: the shared use case
/// (<see cref="DevbookProposalDecision"/>), the rows the view offers
/// (<see cref="DevbookProposals"/>), and the Instructions area's Proposals tab
/// that draws Approve and Accept. A decision made here keeps the host's local
/// author — it is the person's click — and a record an agent wrote over MCP reads
/// back as it was stored.
/// </summary>
public sealed class DevbookProposalDecisionTests : IDisposable
{
    private const string Alias = "backlog";
    private const string Change = "add-decision-tools";
    private const string Fingerprint = "sha256:0a1b2c3d";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("devbook-proposals-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind costs nothing.
        }
    }

    [Fact]
    public void Approving_records_the_local_author_at_the_fingerprint()
    {
        var (subjects, notes) = Stores();

        var outcome = DevbookProposalDecision.Approve(subjects, notes, Alias, DevbookSubject.Proposal(Change), Fingerprint, DevbookLocalAuthor.Name);

        Assert.False(outcome.IsRefused);
        Assert.Equal("approved", outcome.State!.State);
        Assert.Equal(new DevbookSubjectDecision(DevbookLocalAuthor.Name, Now, Fingerprint), outcome.State.Approval);
    }

    [Fact]
    public void Accepting_at_the_approved_fingerprint_moves_the_proposal_to_accepted()
    {
        var (subjects, notes) = Stores();
        var proposal = DevbookSubject.Proposal(Change);
        DevbookProposalDecision.Approve(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name);

        var outcome = DevbookProposalDecision.Accept(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name);

        Assert.Equal("accepted", outcome.State!.State);
        Assert.Equal(DevbookLocalAuthor.Name, outcome.State.Acceptance!.By);
    }

    [Fact]
    public void Accepting_with_no_approval_or_at_another_fingerprint_is_refused_and_writes_nothing()
    {
        var (subjects, notes) = Stores();
        var proposal = DevbookSubject.Proposal(Change);

        Assert.Equal(
            DevbookSubjectRefusal.NotApproved,
            DevbookProposalDecision.Accept(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name).Refused);
        Assert.Null(subjects.Get(Alias, proposal));

        DevbookProposalDecision.Approve(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name);

        Assert.Equal(
            DevbookSubjectRefusal.FingerprintMismatch,
            DevbookProposalDecision.Accept(subjects, notes, Alias, proposal, "sha256:ffffffff", DevbookLocalAuthor.Name).Refused);
        Assert.Equal("approved", subjects.Get(Alias, proposal)!.State);
    }

    /// <summary>An open question anywhere in the change blocks approve and
    /// accept, as it blocked the retired tools; resolved, it no longer does, and a
    /// comment never does.</summary>
    [Fact]
    public void An_open_question_on_the_change_refuses_approve_and_accept_until_it_is_resolved()
    {
        var (subjects, notes) = Stores();
        var proposal = DevbookSubject.Proposal(Change);
        var question = Note(notes, $"openspec/changes/{Change}/proposal.md", DevbookAnnotationKind.Question, "Is the fee ours?");
        Note(notes, $"openspec/changes/{Change}/proposal.md", DevbookAnnotationKind.Comment, "Reads well.");

        var refused = DevbookProposalDecision.Approve(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name);

        Assert.Equal(DevbookSubjectRefusal.OpenQuestion, refused.Refused);
        Assert.Null(subjects.Get(Alias, proposal));

        notes.SetResolved(question.Id, resolved: true);
        Assert.Equal("approved", DevbookProposalDecision.Approve(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name).State!.State);

        Note(notes, $"openspec/changes/{Change}/specs/tools/spec.md", DevbookAnnotationKind.Question, "And the reply shape?");
        Assert.Equal(
            DevbookSubjectRefusal.OpenQuestion,
            DevbookProposalDecision.Accept(subjects, notes, Alias, proposal, Fingerprint, DevbookLocalAuthor.Name).Refused);
    }

    [Fact]
    public void The_rows_are_the_changes_with_a_proposal_at_their_current_fingerprint()
    {
        WriteChange(Change);
        WriteChange("b-second");
        Directory.CreateDirectory(Path.Combine(_root, "openspec", "changes", "no-proposal"));
        WriteChange("archive");
        var (subjects, _) = Stores();

        var rows = Rows(subjects);

        Assert.Equal([Change, "b-second"], rows.Select(row => row.Name));
        Assert.Equal(DevbookFingerprint.ReadChange(_root, Change), rows[0].Fingerprint);
        Assert.All(rows, row => Assert.Null(row.State));
        Assert.Empty(DevbookChangeFolders.Read(Path.Combine(_root, "nowhere")));
        Assert.Empty(DevbookChangeFolders.Read(null));
    }

    /// <summary>The app path is unchanged: a click is stamped with the local
    /// author, Approve then gives way to Accept, and an archived repository is
    /// refused before anything is written.</summary>
    [Fact]
    public void A_decision_in_the_app_is_stamped_with_the_local_author()
    {
        WriteChange(Change);
        var (subjects, notes) = Stores();
        var row = Assert.Single(Rows(subjects));
        Assert.True(row.CanApprove);
        Assert.False(row.CanAccept);

        Assert.True(DevbookProposals.Approve(subjects, notes, Alias, repositoryArchived: true, row, row.Fingerprint).RepositoryArchived);
        Assert.True(DevbookProposals.Accept(subjects, notes, Alias, repositoryArchived: true, row, row.Fingerprint).RepositoryArchived);
        Assert.Null(subjects.Get(Alias, row.Subject));

        var approved = DevbookProposals.Approve(subjects, notes, Alias, repositoryArchived: false, row, row.Fingerprint);

        Assert.Equal(DevbookLocalAuthor.Name, approved.State!.Approval!.By);
        Assert.Equal(row.Fingerprint, approved.State.Approval.Fingerprint);

        row = Assert.Single(Rows(subjects));
        Assert.False(row.CanApprove);
        Assert.True(row.CanAccept);
        Assert.Equal(DevbookLocalAuthor.Name, DevbookProposals.Accept(subjects, notes, Alias, repositoryArchived: false, row, row.Fingerprint).State!.Acceptance!.By);
    }

    /// <summary>A change that moved since it was approved shows the approval as
    /// lapsed and offers Approve again, not Accept.</summary>
    [Fact]
    public void An_approval_at_another_fingerprint_has_lapsed()
    {
        WriteChange(Change);
        var (subjects, notes) = Stores();
        var first = Assert.Single(Rows(subjects));
        DevbookProposals.Approve(subjects, notes, Alias, repositoryArchived: false, first, first.Fingerprint);

        File.AppendAllText(Path.Combine(_root, "openspec", "changes", Change, "proposal.md"), "\nOne more line.\n");
        var row = Assert.Single(Rows(subjects));

        Assert.True(row.Lapsed(row.State!.Approval));
        Assert.True(row.CanApprove);
        Assert.False(row.CanAccept);
    }

    /// <summary>A change whose files cannot be read has no fingerprint: neither
    /// button is offered, and a decision on it is refused without a
    /// write.</summary>
    [Fact]
    public void A_change_that_cannot_be_read_is_not_decided()
    {
        var (subjects, notes) = Stores();
        var row = new DevbookProposalRow(Change, Fingerprint: null, State: null);

        Assert.False(row.CanApprove);
        Assert.False(row.CanAccept);
        Assert.Equal(DevbookSubjectRefusal.BadState, DevbookProposals.Approve(subjects, notes, Alias, repositoryArchived: false, row, null).Refused);
        Assert.Null(subjects.Get(Alias, row.Subject));
        Assert.Null(DevbookChangeFolders.Fingerprint(_root, Change));
        Assert.Null(DevbookChangeFolders.Fingerprint(_root, "../escape"));
    }

    /// <summary>A note an agent wrote over MCP, and an approval recorded before
    /// this change by another machine, read back with the author they were
    /// stored with — nothing is rewritten.</summary>
    [Fact]
    public void Stored_records_read_back_unchanged()
    {
        Assert.Equal(DevbookLocalAuthor.AgentViaMcp, DevbookLocalAuthor.Display(DevbookLocalAuthor.AgentViaMcp));
        Assert.Equal("OTHER-PC", DevbookLocalAuthor.Display("OTHER-PC"));
        Assert.Equal("You", DevbookLocalAuthor.Display(DevbookLocalAuthor.Name));
    }

    [Fact]
    public async Task The_instructions_area_offers_approve_and_accept_on_a_proposal()
    {
        var subjects = new SessionDevbookSubjectStateStore(new FakeTimeProvider(Now));
        var notes = new SessionDevbookAnnotationStore(new FakeTimeProvider(Now));
        await using var context = PanelContext(subjects, notes, archived: false);
        var panel = OpenProposals(context);

        Assert.True(panel.Find("[data-testid='devbook-proposal-accept']").HasAttribute("disabled"));

        panel.Find("[data-testid='devbook-proposal-approve']").Click();

        panel.WaitForAssertion(() =>
        {
            var state = subjects.Get(Alias, DevbookSubject.Proposal(Change));
            Assert.Equal("approved", state?.State);
            Assert.Equal(DevbookLocalAuthor.Name, state!.Approval!.By);
            Assert.Contains("Approved by You", panel.Find("[data-testid='devbook-proposal-approval']").TextContent, StringComparison.Ordinal);
            Assert.False(panel.Find("[data-testid='devbook-proposal-accept']").HasAttribute("disabled"));
        });

        panel.Find("[data-testid='devbook-proposal-accept']").Click();

        panel.WaitForAssertion(() => Assert.Equal("accepted", subjects.Get(Alias, DevbookSubject.Proposal(Change))?.State));

        // Set back to draft elsewhere — an agent over MCP — the tab follows the
        // store without a re-read of the folder.
        subjects.SetState(Alias, DevbookSubject.Proposal(Change), "draft", DevbookLocalAuthor.AgentViaMcp);
        panel.WaitForAssertion(() =>
        {
            Assert.Equal("draft", panel.Find("[data-testid='devbook-proposal-state']").TextContent);
            Assert.False(panel.Find("[data-testid='devbook-proposal-approve']").HasAttribute("disabled"));
        });

        // An open question on the change: Approve answers with the refusal in the
        // tab's message, and nothing is written.
        notes.Edit(notes.Add(Alias, $"openspec/changes/{Change}/proposal.md", 0, DevbookLocalAuthor.Name, kind: DevbookAnnotationKind.Question).Id, "Why now?");
        panel.Find("[data-testid='devbook-proposal-approve']").Click();
        panel.WaitForAssertion(() =>
            Assert.Contains("open question", panel.Find("[data-testid='devbook-proposals-message']").TextContent, StringComparison.Ordinal));
        Assert.Equal("draft", subjects.Get(Alias, DevbookSubject.Proposal(Change))?.State);
    }

    /// <summary>The fingerprint a click decides at is the change's at the click:
    /// a change edited after the tab was drawn is approved as it stands
    /// now.</summary>
    [Fact]
    public async Task A_click_decides_at_the_fingerprint_the_change_has_now()
    {
        var subjects = new SessionDevbookSubjectStateStore(new FakeTimeProvider(Now));
        await using var context = PanelContext(subjects, new SessionDevbookAnnotationStore(new FakeTimeProvider(Now)), archived: false);
        var panel = OpenProposals(context);

        File.AppendAllText(Path.Combine(_root, "clone", "openspec", "changes", Change, "proposal.md"), "\nEdited.\n");
        panel.Find("[data-testid='devbook-proposal-approve']").Click();

        panel.WaitForAssertion(() => Assert.Equal(
            DevbookFingerprint.ReadChange(Path.Combine(_root, "clone"), Change),
            subjects.Get(Alias, DevbookSubject.Proposal(Change))?.Approval?.Fingerprint));
    }

    /// <summary>An archived repository's proposals are shown and never decided:
    /// both buttons are disabled.</summary>
    [Fact]
    public async Task An_archived_repository_shows_its_proposals_and_offers_no_decision()
    {
        await using var context = PanelContext(
            new SessionDevbookSubjectStateStore(new FakeTimeProvider(Now)),
            new SessionDevbookAnnotationStore(new FakeTimeProvider(Now)),
            archived: true);
        var panel = OpenProposals(context);

        Assert.True(panel.Find("[data-testid='devbook-proposal-approve']").HasAttribute("disabled"));
        Assert.True(panel.Find("[data-testid='devbook-proposal-accept']").HasAttribute("disabled"));
    }

    private BunitContext PanelContext(IDevbookSubjectStateStore subjects, IDevbookAnnotationStore notes, bool archived)
    {
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".github"));
        File.WriteAllText(Path.Combine(clone, ".github", "copilot-instructions.md"), "# Copilot\n");
        Directory.CreateDirectory(Path.Combine(clone, "openspec", "changes", Change));
        File.WriteAllText(Path.Combine(clone, "openspec", "changes", Change, "proposal.md"), "# Add decision tools\n\nWhy.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(_root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        // A second repository, because one has to stay active when the first is
        // archived.
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\nJSdotNet/Other");
        Assert.Empty(errors);
        Assert.Null(gitHub.SetRepositories(
        [
            repositories[0] with { CloneDirectory = clone, DevbookFolders = DevbookFolderSetting.Defaults(), Archived = archived },
            repositories[1]
        ]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton(subjects);
        context.Services.AddSingleton(notes);
        return context;
    }

    private static IRenderedComponent<InstructionsDevbookPanel> OpenProposals(BunitContext context)
    {
        var panel = context.Render<InstructionsDevbookPanel>(parameters => parameters.Add(parameter => parameter.RepositoryAlias, Alias));

        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-testid='instructions-proposals-tab']")));
        panel.Find("[data-testid='instructions-proposals-tab']").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-testid='devbook-proposal']")));
        return panel;
    }

    private static (SessionDevbookSubjectStateStore Subjects, SessionDevbookAnnotationStore Notes) Stores() =>
        (new SessionDevbookSubjectStateStore(new FakeTimeProvider(Now)), new SessionDevbookAnnotationStore(new FakeTimeProvider(Now)));

    private IReadOnlyList<DevbookProposalRow> Rows(IDevbookSubjectStateStore subjects) =>
        DevbookProposals.Rows(DevbookChangeFolders.Read(_root), subjects, Alias);

    private static DevbookAnnotation Note(SessionDevbookAnnotationStore notes, string path, DevbookAnnotationKind kind, string body)
    {
        var note = notes.Add(Alias, path, 0, DevbookLocalAuthor.Name, kind: kind);
        notes.Edit(note.Id, body);
        return note;
    }

    private void WriteChange(string name)
    {
        var folder = Path.Combine(_root, "openspec", "changes", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "proposal.md"), $"# {name}\n\nWhy.\n");
    }
}
