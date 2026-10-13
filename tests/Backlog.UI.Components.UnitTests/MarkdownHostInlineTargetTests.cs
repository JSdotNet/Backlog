using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A host's own reading of a link, handed to the read view: asked first, it draws
/// the targets it knows — the Brain's <c>repo:</c> chips — and hands every other
/// target on to the knowledge rule, or to plain rendering where there is none.
/// </summary>
public sealed class MarkdownHostInlineTargetTests
{
    private static readonly MarkdownRender.MarkdownInlineTarget RepoChips = (target, text, kind) =>
        kind is MarkdownInlineKind.Link && target.StartsWith("repo:", StringComparison.Ordinal)
            ? builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-testid", "host-chip");
                builder.AddContent(2, text);
                builder.CloseElement();
            }
            : null;

    [Fact]
    public void The_host_draws_what_it_claims_and_the_knowledge_rule_keeps_the_rest()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument("# Plan\n\nSee [the goals](repo:backlog/.devbook/arc42/01-introduction.md) and [a sibling](missing.md).\n"))
            .Add(v => v.RenderDevbookMetadata, true)
            .Add(v => v.HostInlineTarget, RepoChips));

        Assert.Equal("the goals", Assert.Single(view.FindAll("[data-testid='host-chip']")).TextContent);

        // The relative link is the knowledge rule's: no chapter, so words only.
        Assert.DoesNotContain(view.FindAll("a"), anchor => anchor.TextContent == "a sibling");
        Assert.Contains("a sibling", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_knowledge_rule_the_host_alone_answers()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument("See [the goals](repo:backlog/x.md) and [the site](https://example.com).\n"))
            .Add(v => v.HostInlineTarget, RepoChips));

        Assert.Single(view.FindAll("[data-testid='host-chip']"));
        Assert.Equal("https://example.com", view.Find("a").GetAttribute("href"));
    }
}
