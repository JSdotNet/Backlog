using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The words the AI providers page puts above its cards: the headline built from
/// each provider's state, each tile's figure, and the line under it.
/// </summary>
public sealed class AiProvidersOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_headline_names_what_reports_usage_and_what_is_not_set_up()
    {
        var headline = AiProvidersOverview.Headline(
        [
            new AiProviderState(AiProvider.Claude, true, 2),
            new AiProviderState(AiProvider.GitHub, true, 1),
            new AiProviderState(AiProvider.AzureFoundry, false)
        ]);

        Assert.Equal("Two Claude organizations and GitHub report usage; Azure Foundry is not set up.", headline);
    }

    [Fact]
    public void The_headline_says_so_when_no_provider_is_set_up()
    {
        var headline = AiProvidersOverview.Headline(
        [
            new AiProviderState(AiProvider.Claude, false),
            new AiProviderState(AiProvider.GitHub, false),
            new AiProviderState(AiProvider.AzureFoundry, false)
        ]);

        Assert.Equal(AiProvidersOverview.NoneConfigured, headline);
        Assert.Equal("No AI provider is set up yet.", headline);
    }

    [Fact]
    public void Every_provider_set_up_ends_the_sentence_at_report_usage()
    {
        var headline = AiProvidersOverview.Headline(
        [
            new AiProviderState(AiProvider.Claude, true, 1),
            new AiProviderState(AiProvider.GitHub, true, 2),
            new AiProviderState(AiProvider.AzureFoundry, true)
        ]);

        Assert.Equal("One Claude organization, GitHub and Azure Foundry report usage.", headline);
    }

    [Fact]
    public void One_subject_takes_the_singular_verb_and_two_unconfigured_the_plural()
    {
        var headline = AiProvidersOverview.Headline(
        [
            new AiProviderState(AiProvider.Claude, false),
            new AiProviderState(AiProvider.GitHub, false),
            new AiProviderState(AiProvider.AzureFoundry, true)
        ]);

        Assert.Equal("Azure Foundry reports usage; Claude and GitHub are not set up.", headline);
    }

    [Fact]
    public void A_single_provider_with_several_organizations_takes_the_plural_verb()
    {
        var headline = AiProvidersOverview.Headline([new AiProviderState(AiProvider.Claude, true, 3)]);

        Assert.Equal("Three Claude organizations report usage.", headline);
    }

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "one")]
    [InlineData(9, "nine")]
    [InlineData(10, "10")]
    [InlineData(12, "12")]
    public void Counts_up_to_nine_are_spelled_out(int count, string expected) =>
        Assert.Equal(expected, AiProvidersOverview.Number(count));

    [Fact]
    public void Ten_claude_organizations_are_written_in_digits()
    {
        var headline = AiProvidersOverview.Headline([new AiProviderState(AiProvider.Claude, true, 10)]);

        Assert.Equal("10 Claude organizations report usage.", headline);
    }

    [Fact]
    public void Each_tile_figure_follows_its_provider()
    {
        Assert.Equal("2 organizations", AiProvidersOverview.Figure(new AiProviderState(AiProvider.Claude, true, 2)));
        Assert.Equal("1 organization", AiProvidersOverview.Figure(new AiProviderState(AiProvider.GitHub, true, 1)));
        Assert.Equal("Ready", AiProvidersOverview.Figure(new AiProviderState(AiProvider.AzureFoundry, true)));
        Assert.Equal("Not set up", AiProvidersOverview.Figure(new AiProviderState(AiProvider.AzureFoundry, false)));
        Assert.Equal("Not set up", AiProvidersOverview.Figure(new AiProviderState(AiProvider.Claude, false)));
    }

    [Fact]
    public void A_successful_read_says_how_long_ago()
    {
        var line = AiProvidersOverview.ReadLine(new UsageReadStatus(Now.AddMinutes(-6), null, null), Now);

        Assert.NotNull(line);
        Assert.Equal("Read 6 min ago", line.Text);
        Assert.False(line.Failed);
    }

    [Fact]
    public void A_failed_read_says_why_and_wins_over_an_older_success()
    {
        var line = AiProvidersOverview.ReadLine(new UsageReadStatus(Now.AddDays(-2), Now.AddMinutes(-1), "401 Unauthorized"), Now);

        Assert.NotNull(line);
        Assert.Equal("Last read failed: 401 Unauthorized", line.Text);
        Assert.True(line.Failed);
    }

    [Fact]
    public void Never_read_has_no_line() =>
        Assert.Null(AiProvidersOverview.ReadLine(null, Now));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(5, "5 min ago")]
    [InlineData(150, "2 h ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 3, "3 days ago")]
    public void Relative_times_read_the_way_in_progress_says_them(int minutesAgo, string expected) =>
        Assert.Equal(expected, AiProvidersOverview.Ago(Now.AddMinutes(-minutesAgo), Now));

    [Fact]
    public void Several_organizations_combine_into_the_latest_failure_or_else_the_latest_success()
    {
        var older = new UsageReadStatus(Now.AddHours(-3), null, null);
        var newer = new UsageReadStatus(Now.AddMinutes(-10), null, null);
        var failed = new UsageReadStatus(Now.AddDays(-1), Now.AddHours(-5), "timeout");

        Assert.Same(newer, AiProvidersOverview.Combine([older, newer, null]));
        Assert.Same(failed, AiProvidersOverview.Combine([newer, failed]));
        Assert.Null(AiProvidersOverview.Combine([null, null]));
    }
}
