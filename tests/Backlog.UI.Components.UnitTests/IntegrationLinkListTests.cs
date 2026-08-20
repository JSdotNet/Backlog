namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Zero, one and many — and the rule that a heading over one group is a heading
/// over nothing.
/// </summary>
public sealed class IntegrationLinkListTests
{
    private static readonly IntegrationRepositoryRef Backlog = new("r1", "jsdotnet/backlog");
    private static readonly IntegrationRepositoryRef Aspire = new("r2", "jsdotnet/aspire-lab");

    private static IntegrationLinkRef Issue(string id, IntegrationRepositoryRef? repository) =>
        IntegrationLinkRef.Issue(id, $"#{id}", IntegrationArtifactState.Open, repository: repository);

    [Fact]
    public void Nothing_linked_yet_is_the_libraries_own_empty_state()
    {
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.EmptyTestId, "empty"));

        var empty = list.Find("[data-testid='empty']");

        Assert.Contains("empty-state", empty.ClassList);
        Assert.Contains("Nothing linked yet.", empty.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_state_carries_whatever_would_create_the_first_one()
    {
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.EmptyTestId, "empty")
            .Add(l => l.EmptyActions, builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "data-testid", "create");
                builder.AddContent(2, "Create GitHub issue");
                builder.CloseElement();
            }));

        Assert.Single(list.FindAll("[data-testid='empty'] [data-testid='create']"));
    }

    // --- Repository identity ---------------------------------------------

    [Fact]
    public void A_headed_group_wears_the_repositorys_identity_mark()
    {
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.GroupTestIdPrefix, "group")
            .Add(l => l.Links, new[]
            {
                Issue("1", Backlog with { Colour = 3 }),
                Issue("2", Aspire with { Colour = 5 })
            }));

        Assert.Contains("repo-mark--3", list.Find("[data-testid='group-r1']").ClassName);
        Assert.Contains("repo-mark--5", list.Find("[data-testid='group-r2']").ClassName);
    }

    [Fact]
    public void An_unheaded_group_wears_no_mark()
    {
        // .design/color-scheme.md#band-identity-tokens requires the alias to be written
        // wherever the hue is shown, and the heading is where it is written. One group
        // gets no heading, so it gets no hue either — a coloured edge with nothing
        // naming it would be colour as the sole carrier.
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.GroupTestIdPrefix, "group")
            .Add(l => l.Links, new[] { Issue("1", Backlog with { Colour = 3 }) }));

        Assert.DoesNotContain("repo-mark", list.Find("[data-testid='group-r1']").ClassName);
    }

    [Fact]
    public void A_repository_the_host_has_not_coloured_wears_no_mark()
    {
        // The library declines to pick one. Which repository is which is a workspace
        // question, and a component that answered it would be a second answer.
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.GroupTestIdPrefix, "group")
            .Add(l => l.Links, new[] { Issue("1", Backlog), Issue("2", Aspire) }));

        Assert.DoesNotContain("repo-mark", list.Find("[data-testid='group-r1']").ClassName);
    }

    [Fact]
    public void One_repository_gets_no_heading()
    {
        // It would say "these are all from somewhere" to a reader who could
        // already see that, and cost a line on every surface that only ever has
        // one.
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[] { Issue("1", Backlog), Issue("2", Backlog) }));

        Assert.Empty(list.FindAll(".integration-link-list__heading"));
        Assert.Equal(2, list.FindAll(".integration-link-list__item").Count);
    }

    [Fact]
    public void More_than_one_repository_gets_headings_in_name_order_with_the_unfiled_last()
    {
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[]
            {
                Issue("1", null),
                Issue("2", Backlog),
                Issue("3", Aspire)
            }));

        Assert.Equal(
            new[] { "jsdotnet/aspire-lab", "jsdotnet/backlog", "Not in a repository" },
            list.FindAll(".integration-link-list__heading").Select(heading => heading.TextContent));
    }

    [Fact]
    public void A_repository_alias_is_a_heading_and_never_a_grouping_key()
    {
        // Two references may name one repository through different aliases, and
        // grouping on what is written on screen would split them.
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[]
            {
                Issue("1", Backlog with { Alias = "Backlog" }),
                Issue("2", Backlog),
                Issue("3", Aspire)
            })
            .Add(l => l.GroupTestIdPrefix, "group"));

        Assert.Single(list.FindAll("[data-testid='group-r1']"));
        Assert.Equal(2, list.FindAll("[data-testid='group-r1'] .integration-link-list__item").Count);
    }

    [Fact]
    public void Grouping_can_be_turned_off_without_losing_a_reference()
    {
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[] { Issue("1", Backlog), Issue("2", Aspire) })
            .Add(l => l.GroupByRepository, false));

        Assert.Empty(list.FindAll(".integration-link-list__heading"));
        Assert.Equal(2, list.FindAll(".integration-link-list__item").Count);
    }

    [Fact]
    public void The_freshness_line_only_appears_when_it_has_something_to_say()
    {
        // A surface that never reads and never offers to would otherwise carry a
        // permanent "Not checked yet" that is true and useless.
        using var context = new BunitContext();

        var silent = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[] { Issue("1", Backlog) })
            .Add(l => l.FreshnessTestId, "freshness"));

        Assert.Empty(silent.FindAll("[data-testid='freshness']"));

        var read = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[] { Issue("1", Backlog) })
            .Add(l => l.Reading, new IntegrationReading("4 minutes ago"))
            .Add(l => l.FreshnessTestId, "freshness"));

        Assert.Contains("as of 4 minutes ago", read.Find("[data-testid='freshness']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_cluster_states_the_reason_and_keeps_the_references()
    {
        // A pull request read last Tuesday is still open today; blanking it
        // because a token expired would tell the reader something false.
        using var context = new BunitContext();

        var list = context.Render<IntegrationLinkList>(parameters => parameters
            .Add(l => l.Links, new[] { Issue("1", Backlog), Issue("2", Backlog) })
            .Add(l => l.Readiness, IntegrationReadiness.NotAuthorized("GitHub")));

        Assert.Contains("GitHub is not connected.", list.Find(".integration-unavailable").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, list.FindAll(".integration-link-list__item").Count);
    }
}
