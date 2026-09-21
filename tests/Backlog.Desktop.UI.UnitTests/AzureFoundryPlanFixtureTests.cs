using Backlog.AzureFoundry.TestService;
using Backlog.Infrastructure.AzureFoundry;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The local test service's plan mode: what "Create plan" gets back in the
/// browser harness, where there is no model. It proves the pipe, not the
/// prose — the answer has to be a plan Tasks' import reads as it stands, keyed
/// on the prompt the real client sends, so the whole path from the pane to the
/// backlog can be walked without a key.
/// <para>
/// This project references both the harness and the adapter, which is what
/// lets the marker be compared as two compiled values here; the architecture
/// test compares the same two literals in source, for the day one of the
/// projects stops being referenced from here.
/// </para>
/// </summary>
public sealed class AzureFoundryPlanFixtureTests
{
    [Fact]
    public void The_harness_keys_on_the_same_marker_the_prompt_opens_with()
    {
        Assert.Equal(AzureFoundryPlanPrompt.Marker, LocalAzureFoundryCompletion.PlanPromptMarker);
        Assert.StartsWith(AzureFoundryPlanPrompt.Marker, AzureFoundryPlanPrompt.Text, StringComparison.Ordinal);
    }

    /// <summary>The exact answer for one repository: two entries, the second
    /// after the first, every token in the order the prompt asks for.</summary>
    [Fact]
    public void One_repository_gets_two_entries_in_order()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", AzureFoundryPlanPrompt.Text),
            new AzureFoundryChatMessage("user", AzureFoundryPlanPrompt.User(Request(["acme/web"])))
        ]);

        const string expected =
            "# Fix the login page: step one\n" +
            "`prompt` `!draft` `+fix-login` `id:step-1` `repo:acme/web` `effort:2`\n" +
            "\n" +
            "Backlog plan item step-1 of plan fix-login.\n" +
            "\n" +
            "# Fix the login page: step two\n" +
            "`prompt` `!draft` `+fix-login` `id:step-2` `after:step-1` `repo:acme/web` `effort:3`\n" +
            "\n" +
            "Backlog plan item step-2 of plan fix-login.\n";

        Assert.Equal(expected, answer);
    }

    [Fact]
    public void No_repository_means_no_repo_token()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", AzureFoundryPlanPrompt.Text),
            new AzureFoundryChatMessage("user", AzureFoundryPlanPrompt.User(Request([])))
        ]);

        Assert.DoesNotContain("repo:", answer, StringComparison.Ordinal);
        Assert.Contains("`prompt` `!draft` `+fix-login` `id:step-1` `effort:2`", answer, StringComparison.Ordinal);
        Assert.Contains("`prompt` `!draft` `+fix-login` `id:step-2` `after:step-1` `effort:3`", answer, StringComparison.Ordinal);
    }

    /// <summary>Two repositories, two pairs — and four distinct ids, because the
    /// import refuses a document that uses an id twice.</summary>
    [Fact]
    public void Several_repositories_get_a_pair_each_with_distinct_ids()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", AzureFoundryPlanPrompt.Text),
            new AzureFoundryChatMessage("user", AzureFoundryPlanPrompt.User(Request(["acme/web", "acme/api"])))
        ]);

        var segments = EntryTextParser.SplitSegments(answer);
        Assert.Equal(4, segments.Count);

        var parsed = segments.Select(EntryTextParser.Parse).ToList();
        Assert.Equal(["step-1-1", "step-2-1", "step-1-2", "step-2-2"], parsed.Select(entry => entry.ImportItemId));
        Assert.Equal(["acme/web", "acme/web", "acme/api", "acme/api"], parsed.Select(entry => Assert.Single(entry.RepoIds!)));
        Assert.Equal([null, "step-1-1", null, "step-1-2"], parsed.Select(entry => entry.DependsOn?.SingleOrDefault()));
    }

    /// <summary>What the import actually reads from the fixture: the tag on
    /// every entry, an id on every entry, draft status, the repository, and the
    /// order.</summary>
    [Fact]
    public void The_fixture_parses_as_a_plan_the_import_can_take()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", AzureFoundryPlanPrompt.Text),
            new AzureFoundryChatMessage("user", AzureFoundryPlanPrompt.User(Request(["acme/web"])))
        ]);

        var parsed = EntryTextParser.SplitSegments(answer).Select(EntryTextParser.Parse).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.All(parsed, entry =>
        {
            Assert.Equal(EntryType.Prompt, entry.Type);
            Assert.Equal(EntryStatus.Draft, entry.Status);
            Assert.Contains("+fix-login", entry.Tags);
            Assert.Equal(["acme/web"], entry.RepoIds);
            Assert.NotNull(entry.ImportItemId);
            Assert.NotNull(entry.Effort);
        });
        Assert.Equal("Fix the login page: step one", parsed[0].Title);
        Assert.Equal(["step-1"], parsed[1].DependsOn);
        Assert.StartsWith("Backlog plan item step-1 of plan fix-login.", parsed[0].Body, StringComparison.Ordinal);
    }

    /// <summary>A question is still a question: the plan mode is entered by the
    /// marker alone, not by the shape of the user message.</summary>
    [Fact]
    public void A_question_prompt_still_gets_the_echo_answer()
    {
        var answer = LocalAzureFoundryCompletion.CreateAnswer(
        [
            new AzureFoundryChatMessage("system", "You answer questions about the supplied Backlog content."),
            new AzureFoundryChatMessage("user", "Content:\nItem: not a plan\n\nQuestion:\nWhat matters?")
        ]);

        Assert.StartsWith("Local Azure Foundry test response:", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("`prompt`", answer, StringComparison.Ordinal);
    }

    /// <summary>The slow-model switch: a marker in the question holds the
    /// answer, nothing holds it by default, and a request for longer than the
    /// harness allows gets the ceiling rather than the request. This is what
    /// lets the desktop client's timeout budget be walked from the browser.</summary>
    [Theory]
    [InlineData("Content:\nItem\n\nQuestion:\nWhat matters?", 0)]
    [InlineData("Content:\nItem\n\nQuestion:\ndelay:15s What matters?", 15)]
    [InlineData("Content:\nItem\n\nQuestion:\nWhat matters? DELAY:2S", 2)]
    [InlineData("Content:\nItem\n\nQuestion:\ndelay:999s What matters?", 180)]
    [InlineData("Content:\nItem\n\nQuestion:\nnodelay:15s and undelay:3sx", 0)]
    public void A_delay_marker_in_the_question_holds_the_answer(string userPrompt, int expectedSeconds)
    {
        var delay = LocalAzureFoundryCompletion.RequestedDelay(
        [
            new AzureFoundryChatMessage("system", "You answer questions about the supplied Backlog content."),
            new AzureFoundryChatMessage("user", userPrompt)
        ]);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    private static AzureFoundryPlanRequest Request(IReadOnlyList<string> repositories) => new(
        "Fix the login page",
        "The login page 500s on submit.",
        "https://example.com/login-bug",
        "article",
        ["auth"],
        repositories,
        "fix-login");
}
