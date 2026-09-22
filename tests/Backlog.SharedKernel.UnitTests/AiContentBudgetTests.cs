using Backlog.SharedKernel.Ai;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The one way every Ask AI source fits its records into the budget. Each
/// source trusts this for what "fits" and "relevant" mean, so the rules are
/// pinned here once rather than seven times.
/// </summary>
public class AiContentBudgetTests
{
    private static readonly string[] Records =
    [
        "Alpha: write the sync client",          // 0
        "Beta: fix the roadmap band colours",    // 1
        "Gamma: sync the devbook index nightly", // 2
        "Delta: tidy the settings screen"        // 3
    ];

    [Fact]
    public void Terms_are_lower_case_words_of_three_letters_or_more_without_stop_words()
    {
        var terms = AiContentBudget.Terms("What does the Sync client do when it is offline?");

        Assert.Equal(["sync", "client", "offline"], terms);
    }

    [Fact]
    public void Terms_are_distinct_and_empty_for_nothing_typed()
    {
        Assert.Equal(["sync"], AiContentBudget.Terms("sync, sync, SYNC"));
        Assert.Empty(AiContentBudget.Terms("   "));
        Assert.Empty(AiContentBudget.Terms(null));
    }

    /// <summary>The records that share the most words with the question are
    /// the ones that go when not everything fits — and they come back in the
    /// order they came in, not in rank order.</summary>
    [Fact]
    public void Relevance_chooses_the_records_that_share_words_with_the_question_and_keeps_their_order()
    {
        // Room for two records and the one separator between them, not three.
        var budget = Records[0].Length + AiContentBudget.Separator.Length + Records[2].Length;

        var selection = AiContentBudget.Select(Records, record => record, "what about the sync work?", budget);

        Assert.Equal([Records[0], Records[2]], selection.Selected);
        Assert.Equal(4, selection.Total);
        Assert.True(selection.Trimmed);
    }

    [Fact]
    public void A_pinned_record_goes_first_whatever_the_question_says()
    {
        var budget = Records[3].Length;

        var selection = AiContentBudget.Select(
            Records,
            record => record,
            "sync",
            budget,
            pinned: record => record.StartsWith("Delta", StringComparison.Ordinal));

        Assert.Equal([Records[3]], selection.Selected);
    }

    /// <summary>A record that does not fit is skipped and the walk goes on: a
    /// later, shorter one may fit where it did not.</summary>
    [Fact]
    public void A_record_that_does_not_fit_is_skipped_and_a_shorter_one_after_it_still_goes()
    {
        string[] records = ["short one", new string('x', 60), "tiny"];
        var budget = "short one".Length + AiContentBudget.Separator.Length + "tiny".Length;

        var selection = AiContentBudget.Select(records, record => record, question: null, budget);

        Assert.Equal(["short one", "tiny"], selection.Selected);
        Assert.True(selection.Trimmed);
    }

    [Fact]
    public void Ties_keep_the_original_order_and_nothing_is_trimmed_when_everything_fits()
    {
        var selection = AiContentBudget.Select(Records, record => record, "nothing in common", budgetCharacters: 10_000);

        Assert.Equal(Records, selection.Selected);
        Assert.Equal(4, selection.Total);
        Assert.False(selection.Trimmed);
    }

    [Fact]
    public void A_record_longer_than_the_whole_budget_is_left_out()
    {
        string[] records = [new string('y', 100)];

        var selection = AiContentBudget.Select(records, record => record, "y", budgetCharacters: 50);

        Assert.Empty(selection.Selected);
        Assert.Equal(1, selection.Total);
        Assert.True(selection.Trimmed);
    }

    /// <summary>The first record pays no separator and the last leaves none
    /// dangling, so a budget of exactly the joined length fits and one less does
    /// not.</summary>
    [Fact]
    public void A_record_of_exactly_the_remaining_budget_fits_and_one_character_more_does_not()
    {
        string[] records = ["abc", "defg"];
        var exact = "abc".Length + AiContentBudget.Separator.Length + "defg".Length;

        Assert.Equal(records, AiContentBudget.Select(records, record => record, null, exact).Selected);
        Assert.Equal(["abc"], AiContentBudget.Select(records, record => record, null, exact - 1).Selected);
        Assert.Equal(["abc"], AiContentBudget.Select(["abc"], record => record, null, 3).Selected);
        Assert.Empty(AiContentBudget.Select(["abc"], record => record, null, 2).Selected);
    }

    [Fact]
    public void The_first_line_says_the_count_when_nothing_was_left_out()
    {
        var body = AiContentBudget.Render("Tasks", ["one", "two"], shown: 2, total: 2, trimmed: false);

        Assert.Equal("Tasks: 2 entries.\none\n---\ntwo", body);
    }

    [Fact]
    public void One_entry_is_an_entry_and_not_entries()
    {
        Assert.Equal("Tasks: 1 entry.\none", AiContentBudget.Render("Tasks", ["one"], shown: 1, total: 1, trimmed: false));
        Assert.Equal("Tasks: 1 of 3 entries, selected by relevance to the question.\none", AiContentBudget.Render("Tasks", ["one"], shown: 1, total: 3, trimmed: true));
    }

    /// <summary>The header is charged first, so the whole body — header, line
    /// break, records — is never longer than the budget: a record of exactly what
    /// the header leaves fits, and one character more is left out.</summary>
    [Fact]
    public void Compose_charges_the_header_and_keeps_the_body_within_the_budget()
    {
        const int budget = 100;
        var room = budget - AiContentBudget.HeaderReserve("Tasks", 1);
        var exact = new string('a', room);

        var fits = AiContentBudget.Compose("tasks", "Tasks", [exact], record => record, null, budget);
        var over = AiContentBudget.Compose("tasks", "Tasks", [exact + "b"], record => record, null, budget);

        Assert.Equal($"Tasks: 1 entry.\n{exact}", fits.Body);
        Assert.True(fits.Body.Length <= budget);
        Assert.False(fits.Trimmed);

        Assert.Equal("Tasks: 0 of 1 entries, selected by relevance to the question.", over.Body);
        Assert.True(over.Trimmed);
        Assert.Equal(0, over.Shown);
        Assert.Equal(1, over.Total);
    }

    /// <summary>A source whose list is already a cap on the area says the real
    /// total, and a note about the records as a whole goes under the header.</summary>
    [Fact]
    public void Compose_takes_the_real_total_and_a_note_under_the_header()
    {
        var content = AiContentBudget.Compose(
            "sessions",
            "Sessions",
            ["one", "two"],
            record => record,
            null,
            budgetCharacters: 500,
            total: 842,
            note: "1 session file could not be read.");

        Assert.Equal(
            "Sessions: 2 of 842 entries, selected by relevance to the question.\n1 session file could not be read.\none\n---\ntwo",
            content.Body);
        Assert.Equal(842, content.Total);
        Assert.True(content.Trimmed);
    }

    [Fact]
    public void The_first_line_says_how_many_of_how_many_when_the_budget_cut_the_list()
    {
        var body = AiContentBudget.Render("Inbox", ["one"], shown: 1, total: 9, trimmed: true);

        Assert.Equal("Inbox: 1 of 9 entries, selected by relevance to the question.\none", body);
    }

    [Fact]
    public void An_empty_area_renders_its_first_line_and_nothing_else()
    {
        Assert.Equal("Roadmap: 0 entries.", AiContentBudget.Render("Roadmap", [], shown: 0, total: 0, trimmed: false));
    }
}
