namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The rules of <c>devbook.decision@1</c>'s four state operations, as one pure
/// copy every store applies: approve and accept take a proposal alone, accept
/// stands on an approval at the same fingerprint, <c>draft</c> withdraws both,
/// and a state outside a subject's row is refused — and every refusal hands back
/// no state to write.
/// </summary>
public sealed class DevbookSubjectRulesTests
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Host = "DEV-TOWER";
    private const string Fingerprint = "sha256:2b11c532";

    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    private static readonly DevbookSubject Change = DevbookSubject.Proposal("pay-by-card");
    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");
    private static readonly DevbookSubject Record = DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1607);

    [Fact]
    public void A_debt_record_takes_a_state_from_its_row_stamped_by_the_host()
    {
        var outcome = DevbookSubjectRules.SetState(null, Repository, Debt, "planned", Host, Monday);

        Assert.False(outcome.IsRefused);
        var state = outcome.State!;
        Assert.Equal(DevbookSubject.IdFor(Repository, Debt), state.Id);
        Assert.Equal(DevbookSubject.AliasKey(Repository), state.RepositoryAlias);
        Assert.Equal(Debt, state.Subject);
        Assert.Equal("planned", state.State);
        Assert.Equal(Host, state.By);
        Assert.Equal(Monday, state.At);
        Assert.Equal(Monday, state.UpdatedAt);
        Assert.Null(state.Approval);
        Assert.Null(state.Acceptance);
    }

    [Theory]
    [InlineData("decided")]
    [InlineData("draft")]
    [InlineData("Planned")]
    [InlineData("")]
    public void A_state_outside_the_subjects_row_is_refused(string state)
    {
        var outcome = DevbookSubjectRules.SetState(null, Repository, Debt, state, Host, Monday);

        Assert.Equal(DevbookSubjectRefusal.BadState, outcome.Refused);
        Assert.Null(outcome.State);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("accepted")]
    public void A_proposal_is_never_set_to_approved_or_accepted(string state) =>
        Assert.Equal(DevbookSubjectRefusal.BadState, DevbookSubjectRules.SetState(null, Repository, Change, state, Host, Monday).Refused);

    [Fact]
    public void A_decision_record_moves_through_its_own_row()
    {
        var proposed = DevbookSubjectRules.SetState(null, Repository, Record, "proposed", Host, Monday).State;

        var decided = DevbookSubjectRules.SetState(proposed, Repository, Record, "decided", "OTHER-PC", Tuesday).State!;

        Assert.Equal("decided", decided.State);
        Assert.Equal("OTHER-PC", decided.By);
        Assert.Equal(Tuesday, decided.At);
    }

    [Fact]
    public void Approving_a_proposal_records_who_when_and_at_which_fingerprint()
    {
        var outcome = DevbookSubjectRules.Approve(null, Repository, Change, Fingerprint, Host, Monday);

        Assert.Equal("approved", outcome.State!.State);
        Assert.Equal(new DevbookSubjectDecision(Host, Monday, Fingerprint), outcome.State.Approval);
        Assert.Null(outcome.State.Acceptance);
    }

    [Fact]
    public void Accepting_stands_on_an_approval_at_the_same_fingerprint()
    {
        var approved = DevbookSubjectRules.Approve(null, Repository, Change, Fingerprint, Host, Monday).State;

        var accepted = DevbookSubjectRules.Accept(approved, Repository, Change, Fingerprint, "OTHER-PC", Tuesday).State!;

        Assert.Equal("accepted", accepted.State);
        Assert.Equal(new DevbookSubjectDecision(Host, Monday, Fingerprint), accepted.Approval);
        Assert.Equal(new DevbookSubjectDecision("OTHER-PC", Tuesday, Fingerprint), accepted.Acceptance);
    }

    [Fact]
    public void Accepting_without_an_approval_is_refused_not_approved()
    {
        var draft = DevbookSubjectRules.SetState(null, Repository, Change, "draft", Host, Monday).State;

        Assert.Equal(DevbookSubjectRefusal.NotApproved, DevbookSubjectRules.Accept(null, Repository, Change, Fingerprint, Host, Tuesday).Refused);
        Assert.Equal(DevbookSubjectRefusal.NotApproved, DevbookSubjectRules.Accept(draft, Repository, Change, Fingerprint, Host, Tuesday).Refused);
    }

    [Fact]
    public void Accepting_at_another_fingerprint_is_refused_as_a_mismatch()
    {
        var approved = DevbookSubjectRules.Approve(null, Repository, Change, Fingerprint, Host, Monday).State;

        var outcome = DevbookSubjectRules.Accept(approved, Repository, Change, "sha256:28a64736", Host, Tuesday);

        Assert.Equal(DevbookSubjectRefusal.FingerprintMismatch, outcome.Refused);
        Assert.Null(outcome.State);
    }

    [Fact]
    public void Approving_again_withdraws_an_acceptance_at_the_old_fingerprint()
    {
        var approved = DevbookSubjectRules.Approve(null, Repository, Change, Fingerprint, Host, Monday).State;
        var accepted = DevbookSubjectRules.Accept(approved, Repository, Change, Fingerprint, Host, Monday).State;

        var reapproved = DevbookSubjectRules.Approve(accepted, Repository, Change, "sha256:28a64736", Host, Tuesday).State!;

        Assert.Equal("approved", reapproved.State);
        Assert.Equal("sha256:28a64736", reapproved.Approval!.Fingerprint);
        Assert.Null(reapproved.Acceptance);
    }

    [Fact]
    public void Setting_a_proposal_back_to_draft_withdraws_approval_and_acceptance()
    {
        var approved = DevbookSubjectRules.Approve(null, Repository, Change, Fingerprint, Host, Monday).State;
        var accepted = DevbookSubjectRules.Accept(approved, Repository, Change, Fingerprint, Host, Monday).State;

        var draft = DevbookSubjectRules.SetState(accepted, Repository, Change, "draft", Host, Tuesday).State!;

        Assert.Equal("draft", draft.State);
        Assert.Null(draft.Approval);
        Assert.Null(draft.Acceptance);
    }

    [Fact]
    public void Approve_and_accept_take_a_proposal_alone()
    {
        Assert.Equal(DevbookSubjectRefusal.BadState, DevbookSubjectRules.Approve(null, Repository, Debt, Fingerprint, Host, Monday).Refused);
        Assert.Equal(DevbookSubjectRefusal.BadState, DevbookSubjectRules.Accept(null, Repository, Record, Fingerprint, Host, Monday).Refused);
    }

    [Theory]
    [InlineData("2b11c532")]
    [InlineData("sha256:2B11C532")]
    [InlineData("sha256:2b11c53")]
    [InlineData("sha256:2b11c5321")]
    public void A_fingerprint_that_is_no_fingerprint_is_refused(string fingerprint)
    {
        var outcome = DevbookSubjectRules.Approve(null, Repository, Change, fingerprint, Host, Monday);

        Assert.Equal(DevbookSubjectRefusal.BadState, outcome.Refused);
        Assert.Contains("fingerprint", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unscoped_subject_is_filed_under_the_empty_alias()
    {
        var state = DevbookSubjectRules.SetState(null, null, Debt, "planned", Host, Monday).State!;

        Assert.Equal(string.Empty, state.RepositoryAlias);
        Assert.Equal(DevbookSubject.IdFor(null, Debt), state.Id);
    }
}
