using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The gate's handoff laid out for a phone before it becomes the body of the run's Personal
/// Validation question: the labelled criteria a checklist, the evidence and its open points
/// bullet lists, one clause a line, and everything else as it was written
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>).
/// </summary>
public sealed class HandoffTextTests
{
    [Theory]
    [InlineData("Acceptance criteria: the toast shows; it closes after 5 s, unless hovered; it is announced — politely.", "Acceptance criteria")]
    [InlineData("acceptance criteria: the toast shows; it closes after 5 s, unless hovered; it is announced — politely.", "acceptance criteria")]
    [InlineData("Criteria: the toast shows; it closes after 5 s, unless hovered; it is announced — politely.", "Criteria")]
    [InlineData("What to check: the toast shows; it closes after 5 s, unless hovered; it is announced — politely.", "What to check")]
    public void A_handoffs_acceptance_criteria_read_as_a_checklist_one_clause_a_line(string handoff, string label) =>
        Assert.Equal(
            $"**{label}**\n\n- [ ] the toast shows\n- [ ] it closes after 5 s, unless hovered\n- [ ] it is announced — politely",
            HandoffText.Structure(handoff));

    [Fact]
    public void A_handoffs_evidence_reads_as_bullets_and_an_open_at_a_sentence_boundary_as_its_own_list() =>
        Assert.Equal(
            "**Evidence**\n\n- .qa-workspace/toast.png\n- build green\n- 1 unverified fixed\n\n**Open**\n\n- cosmetic spacing\n- the docs link",
            HandoffText.Structure("Evidence: .qa-workspace/toast.png; build green; 1 unverified fixed. Open: cosmetic spacing; the docs link"));

    [Fact]
    public void An_open_paragraph_of_its_own_reads_as_bullets() =>
        Assert.Equal(
            "**Evidence**\n\n- build green\n\n**Open**\n\n- cosmetic spacing\n- the docs link",
            HandoffText.Structure("Evidence: build green\n\nOpen: cosmetic spacing; the docs link"));

    [Theory]
    [InlineData("Acceptance criteria: the toast shows; it closes\nEvidence: .qa-workspace/toast.png", "**Acceptance criteria**\n\n- [ ] the toast shows\n- [ ] it closes\n\n**Evidence**\n\n- .qa-workspace/toast.png")]
    [InlineData("Evidence: a; b\nOpen: c", "**Evidence**\n\n- a\n- b\n\n**Open**\n\n- c")]
    [InlineData("Check the toast.\nEvidence: a; b", "Check the toast.\n\n**Evidence**\n\n- a\n- b")]
    public void A_label_that_opens_a_later_line_of_a_paragraph_starts_its_own_list(string handoff, string laid) =>
        Assert.Equal(laid, HandoffText.Structure(handoff));

    [Fact]
    public void A_semicolon_inside_parentheses_brackets_or_code_does_not_split_a_clause() =>
        Assert.Equal(
            "**Evidence**\n\n- draft PR not opened (classifier denied gh pr create; retry)\n- `a; b` quoted\n- [x; y](https://example.com/a;b)\n- ``c ` d; e``",
            HandoffText.Structure("Evidence: draft PR not opened (classifier denied gh pr create; retry); `a; b` quoted; [x; y](https://example.com/a;b); ``c ` d; e``"));

    [Fact]
    public void A_semicolon_inside_emphasis_does_not_split_a_clause() =>
        Assert.Equal(
            "**Evidence**\n\n- **build green; 4255 tests**\n- *a; b*\n- ~~old; gone~~\n- x * y\n- z",
            HandoffText.Structure("Evidence: **build green; 4255 tests**; *a; b*; ~~old; gone~~; x * y; z"));

    [Fact]
    public void A_semicolon_inside_a_bare_address_or_an_autolink_does_not_split_a_clause() =>
        Assert.Equal(
            "**Evidence**\n\n- https://host/a;b=c\n- <https://host/x;y>\n- see (https://h/p;q)\n- done",
            HandoffText.Structure("Evidence: https://host/a;b=c; <https://host/x;y>; see (https://h/p;q); done"));

    [Theory]
    [InlineData("Evidence: smile :( ; b; c. Open: d", "**Evidence**\n\n- smile :(\n- b\n- c\n\n**Open**\n\n- d")]
    [InlineData("Evidence: a \\( b; c [d; e", "**Evidence**\n\n- a \\( b\n- c [d\n- e")]
    public void A_bracket_that_never_closes_is_text_and_does_not_hold_the_rest_together(string handoff, string laid) =>
        Assert.Equal(laid, HandoffText.Structure(handoff));

    [Fact]
    public void An_open_inside_parentheses_or_mid_sentence_stays_in_the_evidence() =>
        Assert.Equal(
            "**Evidence**\n\n- the toast (done. Open: nothing)\n- left Open: as is",
            HandoffText.Structure("Evidence: the toast (done. Open: nothing); left Open: as is"));

    [Theory]
    [InlineData("evidence: a. open: b", "**evidence**\n\n- a\n\n**open**\n\n- b")]
    [InlineData("Evidence: a? Open: b", "**Evidence**\n\n- a?\n\n**Open**\n\n- b")]
    [InlineData("Evidence: a! Open: b", "**Evidence**\n\n- a!\n\n**Open**\n\n- b")]
    public void An_open_after_any_sentence_end_in_any_case_is_lifted_out(string handoff, string laid) =>
        Assert.Equal(laid, HandoffText.Structure(handoff));

    [Fact]
    public void Every_clause_loses_its_closing_full_stop_alike() =>
        Assert.Equal(
            "**Criteria**\n\n- [ ] a\n- [ ] b\n- [ ] wait...",
            HandoffText.Structure("Criteria: a.; b.; wait..."));

    [Fact]
    public void A_labelled_paragraph_with_one_clause_is_the_label_and_a_single_item() =>
        Assert.Equal("**Evidence**\n\n- build green, 4255 tests", HandoffText.Structure("Evidence: build green, 4255 tests"));

    [Fact]
    public void A_labelled_paragraph_written_over_several_lines_is_read_as_one() =>
        Assert.Equal(
            "**Acceptance criteria**\n\n- [ ] the toast shows\n- [ ] it closes",
            HandoffText.Structure("Acceptance criteria: the toast\nshows; it closes"));

    [Fact]
    public void A_plus_line_is_not_a_list_to_markdown_preview_so_it_is_part_of_the_paragraph() =>
        Assert.Equal("**Criteria**\n\n- [ ] a\n- [ ] b + c", HandoffText.Structure("Criteria: a; b\n+ c"));

    [Theory]
    [InlineData("Check the toast. Criteria: AC 1-3. Evidence: .qa-workspace/toast.png")]
    [InlineData("Acceptance criteria:\n- [ ] the toast shows\n- [ ] it closes")]
    [InlineData("```text\nEvidence: a; b\n```")]
    [InlineData("~~~\nCriteria: a; b\n~~~\n")]
    [InlineData("Notes first.\r\n\r\nNothing labelled; here.\r\n")]
    [InlineData("Evidence:   ")]
    [InlineData("Evidently: a; b")]
    [InlineData("")]
    public void A_handoff_without_those_labels_is_shown_as_written(string handoff) =>
        Assert.Equal(handoff, HandoffText.Structure(handoff));

    [Fact]
    public void Only_the_labelled_paragraphs_change_and_the_rest_keeps_its_place()
    {
        const string handoff = "Check the toast.\n\nCriteria: a; b\n\n```\nEvidence: c; d\n```\n\nThanks.";

        Assert.Equal(
            "Check the toast.\n\n**Criteria**\n\n- [ ] a\n- [ ] b\n\n```\nEvidence: c; d\n```\n\nThanks.",
            HandoffText.Structure(handoff));
    }

    [Fact]
    public void A_handoff_already_laid_out_is_unchanged()
    {
        var laid = HandoffText.Structure("Acceptance criteria: a; b\n\nEvidence: c; d. Open: e");

        Assert.Equal(laid, HandoffText.Structure(laid));
    }
}
