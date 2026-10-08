using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A host's image source, cascaded to a knowledge chapter: it answers the images it
/// claims — a scenario screenshot — and every other image keeps the knowledge rule,
/// which leaves a relative image inert.
/// </summary>
public sealed class DevbookImageSourceTests
{
    [Fact]
    public void A_cascaded_source_draws_the_images_it_claims_and_leaves_the_rest_to_the_rule()
    {
        using var context = new BunitContext();
        var source = new RecordingImages();

        var view = context.Render<CascadingValue<IDevbookImageSource>>(parameters => parameters
            .Add(cascade => cascade.Value, source)
            .Add(cascade => cascade.IsFixed, true)
            .AddChildContent<MarkdownView>(markdown => markdown
                .Add(v => v.Blocks, MarkdownPreview.ParseDocument("# Features\n\n![The cart](scenario:place-an-order#cart-filled) ![A diagram](diagram.png)\n"))
                .Add(v => v.DevbookDocumentPath, ".devbook/domain/orders/features.md")));

        var claimed = Assert.Single(view.FindAll("[data-testid='claimed-image']"));
        Assert.Equal("scenario:place-an-order#cart-filled|The cart|.devbook/domain/orders/features.md", claimed.TextContent);
        Assert.Equal("A diagram", view.Find(".md-image--inert").TextContent);
    }

    [Fact]
    public void Without_a_source_a_scenario_image_is_inert()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument("# Features\n\n![The cart](scenario:place-an-order#cart-filled)\n"))
            .Add(v => v.DevbookDocumentPath, ".devbook/domain/orders/features.md"));

        Assert.Empty(view.FindAll("img"));
    }

    [Fact]
    public void A_scenario_page_is_a_known_domain_file_type_with_its_own_glyph()
    {
        Assert.True(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.File, "scenario", "set-up-and-fill-the-backlog.md"));
        Assert.False(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.Chapter, "scenario"));
        Assert.False(DevbookSchema.IsKnownType(DevbookFolder.Arc42, DevbookMetadataLevel.File, "scenario"));
        Assert.Equal(DevbookTypeMarkers.Scenario, DevbookTypeMarkers.GlyphFor("scenario", "set-up-and-fill-the-backlog.md"));
        Assert.False(DevbookTypeMarkers.IsAdditionalPage("scenario", "set-up-and-fill-the-backlog.md"));

        using var context = new BunitContext();
        var mark = context.Render<DevbookTypeMarker>(parameters => parameters
            .Add(marker => marker.Value, "scenario")
            .Add(marker => marker.Labelled, true));

        Assert.Contains("devbook-type-marker--scenario", mark.Find("svg").ClassList);
        Assert.Equal("type: scenario", mark.Find("svg").GetAttribute("aria-label"));
        Assert.NotEmpty(mark.Find("svg").Children);
    }

    private sealed class RecordingImages : IDevbookImageSource
    {
        public RenderFragment? Image(string target, string caption, string? documentPath) =>
            target.StartsWith("scenario:", StringComparison.Ordinal)
                ? builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "data-testid", "claimed-image");
                    builder.AddContent(2, $"{target}|{caption}|{documentPath}");
                    builder.CloseElement();
                }
                : null;
    }
}
