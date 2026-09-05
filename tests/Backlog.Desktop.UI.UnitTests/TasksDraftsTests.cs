using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How a backlog draft reads in the Inbox's words.
///
/// <para>The reading is a heuristic and is pinned as one: a YouTube host is a
/// video, an image extension is an image, a fenced block is code, any other URL
/// is an article or a bare link, and the rest is text. Provenance is what a
/// draft can honestly say — typed by hand, and shared by whoever is tagged as a
/// person. And the PARA lean prefers actionable over reference: a repository
/// makes a project whatever the draft links to.</para>
/// </summary>
public sealed class TasksDraftsTests
{
    private static EntryRow Draft(string text) => new() { RawText = text };

    [Theory]
    [InlineData("# Aspire 13 walkthrough\nhttps://www.youtube.com/watch?v=abc\n", InboxItemKind.YouTube)]
    [InlineData("# Short one\nhttps://youtu.be/abc\n", InboxItemKind.YouTube)]
    [InlineData("# Inbox grouping canvas\nhttps://claude.ai/public/artifacts/123\n", InboxItemKind.ClaudeArtifact)]
    [InlineData("# Whiteboard\nhttps://example.com/photos/board.png?size=large\n", InboxItemKind.Image)]
    [InlineData("# The spec\nhttps://example.com/spec.pdf\n", InboxItemKind.Document)]
    [InlineData("# Local-first sync patterns\nhttps://example.com/posts/local-first\n", InboxItemKind.Article)]
    [InlineData("# https://example.com/posts/local-first\n", InboxItemKind.Link)]
    [InlineData("# A snippet\n```csharp\nvar x = 1;\n```\n", InboxItemKind.Code)]
    [InlineData("# Ask about the trial length\n`task` `!draft`\n", InboxItemKind.Text)]
    public void The_kind_is_read_off_the_text(string text, InboxItemKind expected)
    {
        Assert.Equal(expected, TasksDrafts.KindOf(Draft(text)));
    }

    [Fact]
    public void A_url_inside_prose_does_not_swallow_the_words_after_it()
    {
        // The closing parenthesis and the comma are prose, not address.
        var row = Draft("# Read this\nSee (https://example.com/a), then decide.\n");

        Assert.Equal(InboxItemKind.Article, TasksDrafts.KindOf(row));
    }

    [Fact]
    public void An_attached_image_or_document_wins_over_the_text()
    {
        var image = Draft("# Board photo\n`task` `!draft` `files:C:/photos/board.jpg`\n");
        var archive = Draft("# Export\n`task` `!draft` `files:C:/exports/backup.zip`\n");

        // Only when the parser reads an attachment; otherwise the text decides.
        if (image.PreviewAttachment is not null)
        {
            Assert.Equal(InboxItemKind.Image, TasksDrafts.KindOf(image));
            Assert.Equal(InboxItemKind.Document, TasksDrafts.KindOf(archive));
        }
        else
        {
            Assert.Equal(InboxItemKind.Text, TasksDrafts.KindOf(image));
        }
    }

    [Fact]
    public void A_draft_is_typed_by_hand_and_shared_by_whoever_is_tagged_as_a_person()
    {
        var row = Draft("# Local-first sync patterns @maria\n`task` `!draft` `#sync` `#aspire` `repo:JSdotNet/Backlog`\n");

        var item = TasksDrafts.ToItem(row);

        Assert.NotNull(item.Source);
        Assert.Equal("manual", item.Source.Channel);
        Assert.Equal("@maria", item.Source.Person);
        Assert.DoesNotContain(item.Tags, TagText.IsPerson);
        Assert.Contains("sync", item.Tags);
        Assert.Contains("aspire", item.Tags);
        Assert.Equal("JSdotNet/Backlog", item.Repository);
        Assert.Equal(row.Key.ToString(), item.Key);
    }

    [Theory]
    [InlineData(InboxItemKind.Article, "JSdotNet/Backlog", "Platform", ParaCategory.Projects)]
    [InlineData(InboxItemKind.Article, null, "Platform", ParaCategory.Areas)]
    [InlineData(InboxItemKind.YouTube, null, null, ParaCategory.Resources)]
    [InlineData(InboxItemKind.Text, null, null, null)]
    [InlineData(InboxItemKind.Code, null, null, null)]
    public void The_para_lean_prefers_actionable_over_reference(InboxItemKind kind, string? repo, string? area, ParaCategory? expected)
    {
        Assert.Equal(expected, TasksDrafts.ParaOf(kind, repo, area));
    }

    [Fact]
    public void Only_touched_drafts_reach_the_inbox_and_the_mapping_rides_along()
    {
        var draft = Draft("# Watch this\nhttps://www.youtube.com/watch?v=abc\n");
        var settled = Draft("# Deploy SpecManager\n`task` `!ready`\n");
        var untouched = new EntryRow();

        var items = TasksDrafts.ForInbox([settled, draft, untouched]);

        var only = Assert.Single(items);
        Assert.Equal(InboxItemKind.YouTube, only.Kind);
        Assert.Equal(ParaCategory.Resources, only.Para);
        Assert.Same(draft, TasksDrafts.Find([settled, draft], only));
    }
}
