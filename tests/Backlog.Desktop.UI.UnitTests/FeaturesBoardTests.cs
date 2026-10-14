using AngleSharp.Dom;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Features page as the design canvas's Features board: a headline that
/// counts what is on, three figure tiles, a filter over the rows, and one card per
/// section of the catalog with its own count.
///
/// <para>Every expected count is worked out from the store the page reads, not
/// written down, so the tests do not move when a feature ships or a new one is
/// catalogued. What they pin is the arithmetic and the wording.</para>
/// </summary>
public sealed class FeaturesBoardTests
{
    [Fact]
    public void The_headline_counts_what_is_on_and_how_much_of_it_is_unfinished()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features => store = features);

        Assert.Equal(ExpectedHeadline(store!), Headline(context));
    }

    [Fact]
    public void The_headline_follows_a_switch()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features =>
        {
            store = features;
            _ = features.SetEnabled(AppFeatures.InboxPane, false);
        });
        var before = Counts(store!);

        Switch(context, AppFeatures.InboxPane).Click();

        // Inbox pane is Dev, so turning it on moves both numbers.
        Assert.Equal(before.On + 1, Counts(store!).On);
        Assert.Equal(before.UnfinishedOn + 1, Counts(store!).UnfinishedOn);
        Assert.Equal(ExpectedHeadline(store!), Headline(context));
    }

    [Fact]
    public void The_tiles_show_the_on_count_the_unfinished_count_and_the_always_available_names()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features => store = features);
        var (on, unfinishedOn) = Counts(store!);

        var tiles = context.Component.Find("[data-testid='feature-tiles']");
        Assert.Equal(3, tiles.QuerySelectorAll(".metric-tile").Length);

        AssertTile(context, "feature-tile-on", "Turned on", on.ToString(), $"of {AppFeatures.All.Count}");
        AssertTile(context, "feature-tile-unfinished", "Unfinished and on", unfinishedOn.ToString(), "in development or beta");
        AssertTile(
            context,
            "feature-tile-always",
            "Always available",
            string.Join(", ", AppFeatures.All.Where(f => f.AlwaysEnabled).Select(f => f.Name)),
            null);
        Assert.Contains("Tasks", Tile(context, "feature-tile-always").QuerySelector(".metric-tile__value")!.TextContent);
    }

    [Fact]
    public void The_filter_is_a_radio_group_that_opens_on_All()
    {
        using var context = FeatureStatusIndicatorTests.RenderSettings();

        var group = context.Component.Find("[data-testid='feature-filter']");
        var chips = group.QuerySelectorAll("[role='radio']");

        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal(["All", "On", "Off", "Unfinished"], chips.Select(chip => chip.TextContent.Trim()));
        Assert.Equal("true", Chip(context, "all").GetAttribute("aria-checked"));
        Assert.Contains("feature-filter__option--checked", Chip(context, "all").ClassList);
        Assert.Equal(AppFeatures.All.Count, context.Component.FindAll(".feature-flag").Count);
    }

    [Fact]
    public void On_and_Off_show_only_the_switches_in_that_position()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features => store = features);

        Chip(context, "on").Click();
        Assert.Equal("true", Chip(context, "on").GetAttribute("aria-checked"));
        Assert.Equal(
            AppFeatures.All.Where(f => store!.IsEnabled(f.Key)).Select(f => f.Key),
            RowKeys(context));

        Chip(context, "off").Click();
        Assert.Equal(
            AppFeatures.All.Where(f => !store!.IsEnabled(f.Key)).Select(f => f.Key),
            RowKeys(context));
    }

    [Fact]
    public void Unfinished_shows_dev_and_beta_features_whether_on_or_off()
    {
        using var context = FeatureStatusIndicatorTests.RenderSettings(features =>
        {
            // One unfinished feature each way, so the filter cannot be reading
            // the switch.
            _ = features.SetEnabled(AppFeatures.InboxPane, true);
            _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        });

        Chip(context, "unfinished").Click();

        var expected = AppFeatures.All
            .Where(f => f.Status is AppFeatureStatus.Dev or AppFeatureStatus.Beta)
            .Select(f => f.Key)
            .ToList();
        Assert.Equal(expected, RowKeys(context));
        Assert.Contains(AppFeatures.InboxPane, expected);
        Assert.Contains(AppFeatures.UsageMetrics, expected);
    }

    [Fact]
    public void A_card_the_filter_empties_says_so()
    {
        var crossCutting = AppFeatures.Sections.Single(s => s.Group == AppFeatureGroup.CrossCutting);
        using var context = FeatureStatusIndicatorTests.RenderSettings(features =>
        {
            foreach (var feature in crossCutting.Features)
            {
                _ = features.SetEnabled(feature.Key, false);
            }
        });

        Chip(context, "on").Click();

        var card = context.Component.Find("[data-testid='feature-card-crosscutting']");
        Assert.Empty(card.QuerySelectorAll(".feature-flag"));
        Assert.Equal("Nothing here matches this filter.", card.QuerySelector("[data-testid='feature-card-empty']")!.TextContent.Trim());

        var domain = context.Component.Find("[data-testid='feature-card-domain']");
        Assert.Empty(domain.QuerySelectorAll("[data-testid='feature-card-empty']"));
    }

    [Fact]
    public void Each_card_counts_its_own_switches_that_are_on()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features => store = features);

        foreach (var section in AppFeatures.Sections)
        {
            var card = context.Component.Find($"[data-testid='feature-card-{section.Group.ToString().ToLowerInvariant()}']");
            var on = section.Features.Count(f => store!.IsEnabled(f.Key));

            Assert.Equal(
                $"{on} of {section.Features.Count} on",
                card.QuerySelector(".settings-card__action [data-testid='feature-card-count']")!.TextContent.Trim());
        }
    }

    [Fact]
    public void A_locked_feature_says_it_is_always_available_on_its_name_line()
    {
        using var context = FeatureStatusIndicatorTests.RenderSettings();

        var row = Row(context, TasksFeatures.Tasks);

        Assert.Contains("feature-flag--locked", row.ClassList);
        Assert.Equal("Always available", row.QuerySelector(".toggle__label-row .feature-flag__status")!.TextContent.Trim());

        var locked = Switch(context, TasksFeatures.Tasks);
        Assert.True(locked.HasAttribute("disabled"));
        Assert.Equal("true", locked.GetAttribute("aria-checked"));

        // A disabled switch ignores the press: the feature stays on.
        locked.Click();
        Assert.Equal("true", Switch(context, TasksFeatures.Tasks).GetAttribute("aria-checked"));
    }

    /// <summary>Every row is Toggle's switch shape — a real <c>button</c>, so
    /// Space and Enter flip it from the keyboard — named by the feature's title
    /// alone, and showing the store's position.</summary>
    [Fact]
    public void Every_row_is_a_switch_named_by_its_feature()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features => store = features);

        Assert.Empty(context.Component.FindAll(".feature-flag input[type='checkbox']"));

        foreach (var feature in AppFeatures.All)
        {
            var control = Switch(context, feature.Key);
            var label = context.Component.Find($"#{control.GetAttribute("aria-labelledby")}");

            Assert.Equal("BUTTON", control.TagName);
            Assert.Equal("button", control.GetAttribute("type"));
            Assert.Equal(feature.Name, label.TextContent.Trim());
            Assert.Equal(store!.IsEnabled(feature.Key) ? "true" : "false", control.GetAttribute("aria-checked"));
        }
    }

    [Fact]
    public void Pressing_a_switch_turns_its_feature_on_and_off_again()
    {
        AppFeatureSettingsStore? store = null;
        using var context = FeatureStatusIndicatorTests.RenderSettings(features =>
        {
            store = features;
            _ = features.SetEnabled(AppFeatures.InboxPane, false);
        });

        Switch(context, AppFeatures.InboxPane).Click();
        Assert.True(store!.IsEnabled(AppFeatures.InboxPane));
        Assert.Equal("true", Switch(context, AppFeatures.InboxPane).GetAttribute("aria-checked"));

        Switch(context, AppFeatures.InboxPane).Click();
        Assert.False(store.IsEnabled(AppFeatures.InboxPane));
        Assert.Equal("false", Switch(context, AppFeatures.InboxPane).GetAttribute("aria-checked"));
    }

    [Fact]
    public void The_usage_metrics_note_links_to_the_AI_providers_page()
    {
        using var context = FeatureStatusIndicatorTests.RenderSettings(features =>
            features.SetEnabled(AppFeatures.UsageMetrics, true));

        var link = context.Component.Find("[data-testid='usage-api-endpoints-context-note'] a");

        Assert.Equal("settings/ai", link.GetAttribute("href"));
        Assert.Equal("AI providers", link.TextContent.Trim());
    }

    [Fact]
    public void The_search_list_declares_the_pages_card_titles()
    {
        var page = Assert.Single(SettingsPages.BuiltIn, p => p.Id == SettingsPages.Features);

        Assert.Equal(["Product areas", "Cross-cutting"], page.CardTitles);
    }

    private static (int On, int UnfinishedOn) Counts(AppFeatureSettingsStore store) =>
        (AppFeatures.All.Count(f => store.IsEnabled(f.Key)),
         AppFeatures.All.Count(f => f.Status is AppFeatureStatus.Dev or AppFeatureStatus.Beta && store.IsEnabled(f.Key)));

    private static string ExpectedHeadline(AppFeatureSettingsStore store)
    {
        var (on, unfinishedOn) = Counts(store);
        return $"{on} of {AppFeatures.All.Count} features are on — {unfinishedOn} of them still unfinished.";
    }

    private static string Headline(FeatureStatusIndicatorTests.SettingsRenderContext context) =>
        context.Component.Find("[data-testid='settings-headline']").TextContent.Trim();

    private static IElement Chip(FeatureStatusIndicatorTests.SettingsRenderContext context, string filter) =>
        context.Component.Find($"[data-testid='feature-filter-{filter}']");

    private static IElement Row(FeatureStatusIndicatorTests.SettingsRenderContext context, string key) =>
        context.Component.Find($".feature-flag[data-feature='{key}']");

    private static IElement Switch(FeatureStatusIndicatorTests.SettingsRenderContext context, string key) =>
        context.Component.Find($".feature-flag[data-feature='{key}'] > [role='switch']");

    private static IEnumerable<string> RowKeys(FeatureStatusIndicatorTests.SettingsRenderContext context) =>
        [.. context.Component.FindAll(".feature-flag").Select(row => row.GetAttribute("data-feature")!)];

    private static IElement Tile(FeatureStatusIndicatorTests.SettingsRenderContext context, string testId) =>
        context.Component.Find($"[data-testid='{testId}']");

    private static void AssertTile(
        FeatureStatusIndicatorTests.SettingsRenderContext context,
        string testId,
        string label,
        string value,
        string? footnote)
    {
        var tile = Tile(context, testId);

        Assert.Equal(label, tile.QuerySelector(".metric-tile__label")!.TextContent.Trim());
        Assert.Equal(value, tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());
        if (footnote is null)
        {
            Assert.Null(tile.QuerySelector(".metric-tile__footnote"));
        }
        else
        {
            Assert.Equal(footnote, tile.QuerySelector(".metric-tile__footnote")!.TextContent.Trim());
        }
    }
}
