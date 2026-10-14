using Backlog.Modules.Sessions.UI;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Model prices settings page: empty until somebody fills it in, a row saved as
/// soon as it names a model, a rate that is not a number refused with nothing saved,
/// and a removed row gone from the table.
/// </summary>
public sealed class ModelPriceSettingsTests
{
    private static (BunitContext Context, SessionsPaneDetailTests.StubPriceStore Store) Context(ModelPriceTable? table = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = new SessionsPaneDetailTests.StubPriceStore(table ?? ModelPriceTable.Empty);
        context.Services.AddSingleton<IModelPriceStore>(store);

        return (context, store);
    }

    [Fact]
    public void The_table_is_empty_until_somebody_fills_it_in()
    {
        var (context, _) = Context();
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        Assert.NotNull(page.Find("[data-testid='model-prices-empty']"));
        Assert.Empty(page.FindAll("[data-testid='model-price-row']"));
    }

    [Fact]
    public void A_row_is_saved_once_it_names_a_model_and_every_rate_reads_as_a_price()
    {
        var (context, store) = Context();
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        page.Find("[data-testid='model-price-add']").Click();

        // A row with no model yet is not saved, and is not refused either.
        page.Find("input[data-testid='model-price-input']").Change("5");
        Assert.Empty(store.Table.Prices);

        page.Find("input[data-testid='model-price-model']").Change("claude-opus-5-5");
        page.Find("input[data-testid='model-price-output']").Change("$25");
        page.Find("input[data-testid='model-price-cache-read']").Change("0.50");

        var price = Assert.Single(store.Table.Prices);
        Assert.Equal(new ModelPrice("claude-opus-5-5", 5m, 25m, 0.5m, null), price);
        page.WaitForAssertion(() => Assert.Contains("Saved", page.Find("[data-testid='model-prices-status']").TextContent));
    }

    [Fact]
    public void A_rate_that_is_not_a_number_is_refused_and_nothing_is_saved()
    {
        var (context, store) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]));
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        Assert.Equal("opus", page.Find("input[data-testid='model-price-model']").GetAttribute("value"));

        page.Find("input[data-testid='model-price-input']").Change("five");

        Assert.Contains("is not a price", page.Find("[data-testid='model-prices-status']").TextContent);
        Assert.Equal(5m, Assert.Single(store.Table.Prices).InputPerMTok);
    }

    [Theory]
    [InlineData("0,30")]
    [InlineData("1,000")]
    [InlineData("-1")]
    public void A_rate_with_a_comma_or_a_sign_is_refused_rather_than_read_another_way(string typed)
    {
        var (context, store) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]));
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        page.Find("input[data-testid='model-price-cache-read']").Change(typed);

        Assert.Contains("is not a price", page.Find("[data-testid='model-prices-status']").TextContent);
        Assert.Equal(0.5m, Assert.Single(store.Table.Prices).CacheReadPerMTok);
    }

    [Fact]
    public void Two_rows_for_one_model_are_refused()
    {
        var (context, store) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]));
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        page.Find("[data-testid='model-price-add']").Click();
        page.FindAll("input[data-testid='model-price-model']")[1].Change("Opus");

        Assert.Contains("has two rows", page.Find("[data-testid='model-prices-status']").TextContent);
        Assert.Single(store.Table.Prices);
    }

    [Fact]
    public void A_removed_row_is_gone_from_the_table()
    {
        var (context, store) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m), new ModelPrice("sonnet", 3m, 15m, 0.3m, 3.75m)]));
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        page.FindAll("[data-testid='model-price-remove']")[0].Click();

        Assert.Equal(["sonnet"], store.Table.Prices.Select(price => price.Model));
    }

    /// <summary>The headline counts the prices in force: what the store holds, not the
    /// rows on screen, so a row still being typed or a refused rate leaves it alone.</summary>
    [Fact]
    public void The_headline_counts_the_prices_in_force()
    {
        var (context, _) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m), new ModelPrice("sonnet", 3m, 15m, 0.3m, 3.75m)]));
        using var _context = context;

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Model prices")
            .AddChildContent<ModelPriceSettings>());

        string Headline() => frame.Find("[data-testid='settings-headline']").TextContent.Trim();

        frame.WaitForAssertion(() => Assert.Equal("2 model prices price the runs Claude Code reported no cost for.", Headline()));

        frame.Find("[data-testid='model-price-add']").Click();
        frame.FindAll("input[data-testid='model-price-input']")[0].Change("five");
        Assert.Equal("2 model prices price the runs Claude Code reported no cost for.", Headline());

        frame.FindAll("input[data-testid='model-price-input']")[0].Change("5");
        frame.FindAll("[data-testid='model-price-remove']")[0].Click();

        frame.WaitForAssertion(() => Assert.Equal("1 model price prices the runs Claude Code reported no cost for.", Headline()));
    }

    [Fact]
    public void An_empty_table_says_no_prices_price_anything_yet()
    {
        Assert.Equal("No model prices price the runs Claude Code reported no cost for yet.", ModelPriceSettings.Headline(0));
        Assert.Equal("12 model prices price the runs Claude Code reported no cost for.", ModelPriceSettings.Headline(12));
    }

    /// <summary>One card, the one its section declares, with Add a model in its
    /// header's corner and the table labelled by the card's heading.</summary>
    [Fact]
    public void The_card_on_the_page_is_the_one_its_section_declares()
    {
        var services = new ServiceCollection();
        services.AddSessionsSettings();
        var section = services.Select(d => d.ImplementationInstance).OfType<SettingsSection>().Single(s => s.Id == "model-prices");

        var (context, _) = Context(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]));
        using var _context = context;

        var page = context.Render<ModelPriceSettings>();

        Assert.Equal(["Model prices"], section.CardTitles!);
        Assert.Equal(section.CardTitles!, page.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.NotNull(page.Find(".settings-card__action [data-testid='model-price-add']"));
        Assert.Equal("model-prices-title", page.Find(".settings-card__title").GetAttribute("id"));
        Assert.Equal("model-prices-title", page.Find("[data-testid='model-prices']").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void The_page_is_registered_beside_the_Claude_Code_page_behind_the_sessions_switch()
    {
        var services = new ServiceCollection();
        services.AddSessionsSettings();

        var sections = services.Where(descriptor => descriptor.ServiceType == typeof(SettingsSection))
            .Select(descriptor => (SettingsSection)descriptor.ImplementationInstance!)
            .ToList();

        var prices = Assert.Single(sections, section => section.Id == "model-prices");
        Assert.Equal(typeof(ModelPriceSettings), prices.Component);
        Assert.Equal(SessionFeatures.Sessions, prices.FeatureKey);
        Assert.Equal(SessionsSettingsRegistration.Order + 1, prices.Order);
    }
}
