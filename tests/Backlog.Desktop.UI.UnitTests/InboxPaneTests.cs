using AngleSharp.Dom;
using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox pane on its own: the side menu and its counts, the slice a row
/// belongs to, the kind chips, what a row says, what the detail shows per kind,
/// and the acts on an item.
///
/// <para>Rendered without the shell around it and over an in-memory module,
/// because everything here is the pane's. What the shell does when an item is
/// routed is <see cref="HomeInboxWiringTests"/>; what the module does when
/// asked is the module's own test project.</para>
/// </summary>
public sealed class InboxPaneTests
{
    private const string Repo = "JSdotNet/Backlog";

    // --- The side menu ------------------------------------------------------

    [Fact]
    public async Task The_side_menu_shows_the_inbox_the_seeded_groups_and_the_lists_with_their_counts()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");
        var areas = harness.Inbox.SeedGroup("Areas");
        var platform = harness.Inbox.SeedList("Platform", areas.Id);
        harness.Inbox.Seed("Unfiled one");
        harness.Inbox.Seed("Unfiled two");
        harness.Inbox.Seed("Filed", listId: resources.Id);
        harness.Inbox.Seed("Under an area", listId: platform.Id);
        harness.Inbox.Seed("Archived and not counted", listId: platform.Id, status: InboxStatus.Archived);

        var pane = await harness.RenderAsync();

        Assert.Equal("2", pane.Find("[data-testid='inbox-nav-inbox-count']").TextContent.Trim());
        Assert.Equal("1", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}-count']").TextContent.Trim());
        Assert.Equal("1", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(platform.Id)}-count']").TextContent.Trim());

        var group = pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']");
        Assert.Contains("Areas", group.TextContent);
        // Open by default: a To Do side menu shows its lists until somebody folds them.
        Assert.Equal("true", group.GetAttribute("aria-expanded"));

        // The inbox is the selected slice on first open.
        Assert.Equal("true", pane.Find("[data-testid='inbox-nav-inbox']").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task The_starter_organiser_is_seeded_on_initialise()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        Assert.Equal(1, harness.Inbox.EnsureDefaultOrganizerCalls);
        Assert.Equal(["Areas", "Projects", "Archive"], harness.Inbox.Groups.Select(group => group.Name));
        Assert.Equal(["Resources", "Someday/Maybe", "Updates", "Wishlist"], harness.Inbox.Lists.Select(list => list.Name));
        Assert.Equal(4, pane.FindAll("[data-testid='inbox-nav-ungrouped'] .list-nav__row").Count);
    }

    [Fact]
    public async Task Selecting_a_list_shows_only_the_rows_filed_in_it()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");
        harness.Inbox.Seed("Unfiled");
        harness.Inbox.Seed("Filed", listId: resources.Id);

        var pane = await harness.RenderAsync();

        Assert.Equal(["Unfiled"], Titles(pane));

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ClickAsync(new());

        Assert.Equal(["Filed"], Titles(pane));
        Assert.Equal("Resources", pane.Find("[data-testid='inbox-pane-list']").GetAttribute("aria-label"));
        Assert.Equal("true", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Rows_are_newest_capture_first()
    {
        using var harness = Harness.Create();
        var now = harness.Inbox.Now;
        harness.Inbox.Seed("Older", capturedAt: now.AddHours(-2));
        harness.Inbox.Seed("Newest", capturedAt: now);
        harness.Inbox.Seed("Middle", capturedAt: now.AddHours(-1));

        var pane = await harness.RenderAsync();

        Assert.Equal(["Newest", "Middle", "Older"], Titles(pane));
    }

    // --- Kind chips ---------------------------------------------------------

    [Fact]
    public async Task Kind_chips_show_the_kinds_in_the_slice_with_counts_and_toggle_the_rows()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("A video", ContentKind.YouTube, sourceUrl: "https://www.youtube.com/watch?v=abc123");
        harness.Inbox.Seed("Another video", ContentKind.YouTube, sourceUrl: "https://youtu.be/def456");
        harness.Inbox.Seed("An article", ContentKind.Article, sourceUrl: "https://example.test/post");
        harness.Inbox.Seed("A note");

        var pane = await harness.RenderAsync();

        // Library order: text, article, youtube — and only the kinds present.
        Assert.Equal(
            ["inbox-kind-text", "inbox-kind-article", "inbox-kind-youtube"],
            pane.FindAll("[data-testid='inbox-pane-filters'] [aria-pressed]").Select(chip => chip.GetAttribute("data-testid")));
        Assert.Equal("2", pane.Find("[data-testid='inbox-kind-youtube-count']").TextContent.Trim());
        Assert.Equal("false", pane.Find("[data-testid='inbox-kind-youtube']").GetAttribute("aria-pressed"));

        await pane.Find("[data-testid='inbox-kind-youtube']").ClickAsync(new());

        Assert.Equal("true", pane.Find("[data-testid='inbox-kind-youtube']").GetAttribute("aria-pressed"));
        Assert.Equal(2, Titles(pane).Count);
        Assert.All(Titles(pane), title => Assert.Contains("video", title));

        // A second chip widens the filter rather than replacing it, and the
        // counts stay what they were before any chip was pressed.
        await pane.Find("[data-testid='inbox-kind-article']").ClickAsync(new());

        Assert.Equal(3, Titles(pane).Count);
        Assert.Equal("1", pane.Find("[data-testid='inbox-kind-article-count']").TextContent.Trim());

        await pane.Find("[data-testid='inbox-kind-clear']").ClickAsync(new());

        Assert.Equal(4, Titles(pane).Count);
        Assert.Empty(pane.FindAll("[data-testid='inbox-kind-clear']"));
    }

    [Fact]
    public async Task A_filter_that_empties_the_slice_offers_to_clear_itself()
    {
        using var harness = Harness.Create();
        var video = harness.Inbox.Seed("A video", ContentKind.YouTube);
        harness.Inbox.Seed("A note");

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-kind-youtube']").ClickAsync(new());
        await harness.State.ArchiveAfterSelectingAsync(pane, video.Id);

        var empty = pane.Find("[data-testid='inbox-pane-empty']");
        Assert.Equal("filtered", empty.GetAttribute("data-inbox-empty"));
        Assert.Contains("No youtube items", empty.TextContent);

        await pane.Find("[data-testid='inbox-empty-clear']").ClickAsync(new());

        Assert.Equal(["A note"], Titles(pane));
    }

    [Fact]
    public async Task An_empty_inbox_reads_as_first_use_and_an_empty_list_as_a_list()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");

        var pane = await harness.RenderAsync();

        Assert.Equal("first-use", pane.Find("[data-testid='inbox-pane-empty']").GetAttribute("data-inbox-empty"));

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ClickAsync(new());

        var empty = pane.Find("[data-testid='inbox-pane-empty']");
        Assert.Equal("list", empty.GetAttribute("data-inbox-empty"));
        Assert.Contains("Resources", empty.TextContent);
    }

    // --- What a row says ----------------------------------------------------

    [Fact]
    public async Task A_row_says_its_kind_with_a_mark_and_the_word_its_channel_its_person_its_tags_and_its_repositories()
    {
        using var harness = Harness.Create();
        harness.GitHub.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]);
        harness.Inbox.Seed(
            "Local-first sync patterns",
            ContentKind.Article,
            channel: "web_clipper",
            person: "@maria",
            sourceUrl: "https://example.test/post",
            tags: ["sync", "aspire"],
            repoIds: [Repo]);

        var pane = await harness.RenderAsync();

        var kind = pane.Find("[data-testid='inbox-pane-item-kind']");
        var mark = kind.QuerySelector("svg");
        Assert.NotNull(mark);
        Assert.Contains("capture-kind-marker--article", mark.ClassList);
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Contains("Article", kind.TextContent);
        Assert.Equal("article", pane.Find("[data-testid='inbox-pane-item']").GetAttribute("data-inbox-kind"));

        var source = pane.Find("[data-testid='inbox-pane-item-source']");
        Assert.Contains("badge--source", source.ClassList);
        Assert.Equal("Web clipper", source.TextContent.Trim());

        var person = pane.Find("[data-testid='inbox-pane-item-person']");
        Assert.Contains("tag-chip--person", person.ClassList);
        Assert.Equal("@maria", person.TextContent.Trim());

        Assert.Equal(["#sync", "#aspire"], pane.FindAll("[data-testid='inbox-pane-item-tag']").Select(chip => chip.TextContent.Trim()));
        // The alias, which is what a reader recognises; the id rides on the title.
        var repository = pane.Find("[data-testid='inbox-pane-item-repository']");
        Assert.Equal("backlog", repository.TextContent.Trim());
        Assert.Equal("Repository: JSdotNet/Backlog", repository.GetAttribute("title"));
    }

    [Fact]
    public async Task A_kind_this_build_does_not_know_is_shown_as_its_plain_word()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("From a newer phone", ContentKind.Text, slug: "hologram");

        var pane = await harness.RenderAsync();

        var kind = pane.Find("[data-testid='inbox-pane-item-kind']");
        Assert.Null(kind.QuerySelector("svg"));
        Assert.Equal("hologram", kind.TextContent.Trim());
        Assert.Equal("hologram", pane.Find("[data-testid='inbox-kind-hologram'] .inbox-pane__chip-label").TextContent.Trim());
    }

    // --- The detail, per kind -----------------------------------------------

    [Fact]
    public async Task Choosing_a_row_opens_its_detail_and_marks_the_row_pressed()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Ask about the trial length", channel: "mobile");

        var pane = await harness.RenderAsync();

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-empty']"));

        var row = pane.Find($"[data-testid='inbox-item-{item.Id:D}']");
        Assert.Equal("false", row.GetAttribute("aria-pressed"));

        await row.ClickAsync(new());

        Assert.Equal("true", pane.Find($"[data-testid='inbox-item-{item.Id:D}']").GetAttribute("aria-pressed"));
        Assert.Contains("inbox-pane__row--selected", pane.Find($"[data-testid='inbox-item-{item.Id:D}']").ClassList);

        var detail = pane.Find("[data-testid='inbox-detail']");
        Assert.Equal("Ask about the trial length", detail.QuerySelector("#inbox-detail-title")!.TextContent.Trim());
        Assert.Equal("Text · Mobile", detail.QuerySelector(".inbox-pane__detail-eyebrow")!.TextContent.Trim());
    }

    [Fact]
    public async Task A_video_shows_its_thumbnail_and_its_link()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Aspire 13", ContentKind.YouTube, channel: "youtube", sourceUrl: "https://www.youtube.com/watch?v=abc123");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.Equal(
            "https://img.youtube.com/vi/abc123/hqdefault.jpg",
            pane.Find("[data-testid='inbox-detail-thumbnail']").GetAttribute("src"));
        Assert.Equal("https://www.youtube.com/watch?v=abc123", pane.Find("[data-testid='inbox-detail-link']").GetAttribute("href"));
        // Title-only capture: the empty body reads as a plain line, not an error.
        Assert.Equal("status", pane.Find("[data-testid='inbox-detail-no-body']").GetAttribute("role"));
    }

    [Theory]
    [InlineData("https://youtu.be/def456", "def456")]
    [InlineData("https://www.youtube.com/shorts/ghi789", "ghi789")]
    [InlineData("https://m.youtube.com/watch?v=jkl012&t=10s", "jkl012")]
    public void A_video_id_is_read_from_either_spelling(string url, string expected) =>
        Assert.Equal(expected, InboxItemDetail.YouTubeVideoId(url));

    [Fact]
    public void A_video_from_elsewhere_has_no_thumbnail_to_show() =>
        Assert.Null(InboxItemDetail.YouTubeVideoId("https://vimeo.com/123"));

    [Fact]
    public async Task An_article_shows_its_link_and_its_excerpt()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Local-first", ContentKind.Article, sourceUrl: "https://example.test/post", bodyMd: "An **excerpt** of the page.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-kind-article']"));
        Assert.Equal("https://example.test/post", pane.Find("[data-testid='inbox-detail-link']").GetAttribute("href"));
        Assert.Contains("excerpt", pane.Find("[data-testid='inbox-detail-body']").TextContent);
        Assert.NotNull(pane.Find("[data-testid='inbox-detail-body'] strong"));
    }

    [Fact]
    public async Task A_link_shows_its_url()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("https://example.test", ContentKind.Link, sourceUrl: "https://example.test");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-link']"));
        Assert.Equal("https://example.test", pane.Find("[data-testid='inbox-detail-link']").GetAttribute("href"));
    }

    [Fact]
    public async Task An_email_shows_its_sender_its_subject_and_its_body()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Invoice for September", ContentKind.Email, channel: "email", person: "@accounts", bodyMd: "Please find attached.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.Equal("@accounts", pane.Find("[data-testid='inbox-detail-sender']").TextContent.Trim());
        Assert.Equal("Invoice for September", pane.Find("[data-testid='inbox-detail-subject']").TextContent.Trim());
        Assert.Contains("Please find attached.", pane.Find("[data-testid='inbox-detail-body']").TextContent);
    }

    [Fact]
    public async Task A_claude_artifact_shows_its_link_and_its_summary()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Sync design", ContentKind.ClaudeArtifact, channel: "claude", sourceUrl: "https://claude.ai/artifacts/1", bodyMd: "A summary.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-kind-claude-artifact']"));
        Assert.Equal("https://claude.ai/artifacts/1", pane.Find("[data-testid='inbox-detail-link']").GetAttribute("href"));
        Assert.Contains("A summary.", pane.Find("[data-testid='inbox-detail-body']").TextContent);
    }

    [Fact]
    public async Task A_code_capture_shows_a_code_block()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("A snippet", ContentKind.Code, channel: "ide", bodyMd: "```csharp\nvar x = 1;\n```");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        var code = pane.Find("[data-testid='inbox-detail-code']");
        Assert.Contains("var x = 1;", code.TextContent);
        Assert.Empty(pane.FindAll("[data-testid='inbox-detail-body']"));
    }

    [Fact]
    public async Task An_image_shows_the_picture()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Whiteboard", ContentKind.Image, channel: "mobile", sourceUrl: "https://example.test/whiteboard.png");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.Equal("https://example.test/whiteboard.png", pane.Find("[data-testid='inbox-detail-image']").GetAttribute("src"));
        Assert.Equal("whiteboard.png", pane.Find("[data-testid='inbox-detail-attachment']").TextContent.Trim());
    }

    [Fact]
    public async Task A_document_shows_its_attachment()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Contract", ContentKind.Document, sourceUrl: "https://example.test/files/contract.pdf");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        var attachment = pane.Find("[data-testid='inbox-detail-attachment']");
        Assert.Equal("contract.pdf", attachment.TextContent.Trim());
        Assert.Equal("https://example.test/files/contract.pdf", attachment.GetAttribute("href"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-detail-image']"));
    }

    // --- A capture's own files ----------------------------------------------

    [Fact]
    public async Task A_captured_picture_is_a_thumbnail_and_a_captured_file_is_a_row_with_open()
    {
        using var harness = Harness.Create();
        var jpeg = new InboxAttachmentDto(Guid.NewGuid(), "whiteboard.jpg", "image/jpeg", 7, IsImage: true, Downloaded: true, LastError: null);
        var pdf = new InboxAttachmentDto(Guid.NewGuid(), "contract.pdf", "application/pdf", 2_400_000, IsImage: false, Downloaded: true, LastError: null);
        var item = harness.Inbox.Seed("Planning session", ContentKind.Document, channel: "mobile", bodyMd: "Notes from the room.");
        harness.Inbox.SeedAttachments(item.Id, jpeg, pdf);
        harness.Inbox.AttachmentBytes[jpeg.Id] = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        // The picture above the body, drawn from the file's own bytes.
        var image = pane.WaitForElement("[data-testid='inbox-detail-attachment-image']");
        Assert.Equal("data:image/jpeg;base64,/9j/4AECAw==", image.GetAttribute("src"));
        Assert.Equal("whiteboard.jpg", image.GetAttribute("alt"));
        var content = pane.Find("[data-testid='inbox-detail-kind-document']");
        Assert.True(
            content.InnerHtml.IndexOf("inbox-detail-thumbnails", StringComparison.Ordinal)
                < content.InnerHtml.IndexOf("inbox-detail-body", StringComparison.Ordinal),
            "The thumbnails are drawn above the body.");

        // The PDF is a row, with its size, and Open hands it to the module.
        var row = pane.Find($"[data-testid='inbox-attachment-row-{pdf.Id:N}']");
        Assert.Contains("contract.pdf", row.TextContent);
        Assert.Contains("2.3 MB", row.TextContent);
        Assert.Empty(pane.FindAll($"[data-testid='inbox-attachment-row-{jpeg.Id:N}']"));

        await pane.Find($"[data-testid='inbox-attachment-open-{pdf.Id:N}']").ClickAsync(new());
        await pane.Find($"[data-testid='inbox-attachment-thumbnail-{jpeg.Id:N}']").ClickAsync(new());

        Assert.Equal([pdf.Id, jpeg.Id], harness.Inbox.Opened);
    }

    [Fact]
    public async Task A_file_that_failed_to_download_shows_why_and_retry_brings_it_down()
    {
        using var harness = Harness.Create();
        var pdf = new InboxAttachmentDto(Guid.NewGuid(), "contract.pdf", "application/pdf", 900, IsImage: false, Downloaded: false,
            LastError: "The sync service could not be reached.");
        var item = harness.Inbox.Seed("Contract", ContentKind.Document, channel: "mobile");
        harness.Inbox.SeedAttachments(item.Id, pdf);

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.Equal(
            "The sync service could not be reached.",
            pane.Find($"[data-testid='inbox-attachment-error-{pdf.Id:N}']").TextContent.Trim());
        Assert.Empty(pane.FindAll($"[data-testid='inbox-attachment-open-{pdf.Id:N}']"));

        await pane.Find($"[data-testid='inbox-attachment-retry-{pdf.Id:N}']").ClickAsync(new());

        Assert.Equal([pdf.Id], harness.Inbox.Retried);
        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll($"[data-testid='inbox-attachment-open-{pdf.Id:N}']"));
            Assert.Empty(pane.FindAll($"[data-testid='inbox-attachment-error-{pdf.Id:N}']"));
        });
    }

    [Fact]
    public async Task A_file_not_fetched_yet_says_it_is_waiting_and_a_picture_not_on_disk_is_a_row()
    {
        using var harness = Harness.Create();
        var photo = new InboxAttachmentDto(Guid.NewGuid(), "IMG_2041.jpg", "image/jpeg", 12, IsImage: true, Downloaded: false, LastError: null);
        var item = harness.Inbox.Seed("IMG_2041", ContentKind.Image, channel: "mobile");
        harness.Inbox.SeedAttachments(item.Id, photo);

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.Empty(pane.FindAll("[data-testid='inbox-detail-thumbnails']"));
        Assert.Equal(
            "Waiting to download",
            pane.Find($"[data-testid='inbox-attachment-waiting-{photo.Id:N}']").TextContent.Trim());
    }

    [Fact]
    public async Task A_voice_memo_shows_its_transcript()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Memo", ContentKind.Voice, channel: "mobile", bodyMd: "Remember to call back.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-kind-voice']"));
        Assert.Contains("Remember to call back.", pane.Find("[data-testid='inbox-detail-body']").TextContent);
    }

    [Fact]
    public async Task A_text_capture_shows_its_body_as_markdown()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Note", ContentKind.Text, bodyMd: "- one\n- two");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-detail-kind-text']"));
        Assert.Equal(2, pane.FindAll("[data-testid='inbox-detail-body'] li").Count);
    }

    // --- The acts -----------------------------------------------------------

    [Fact]
    public async Task Create_plan_is_shown_disabled_with_its_reason_when_the_drafter_is_unavailable()
    {
        using var harness = Harness.Create();
        harness.Inbox.PlanDrafterAvailability = (false, "Configure Azure Foundry in Settings to create plans.");
        var item = harness.Inbox.Seed("Plan me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        var button = pane.Find("[data-testid='inbox-create-plan']");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("Configure Azure Foundry in Settings to create plans.", button.GetAttribute("title"));

        // And enabled, with a different title, when it is.
        harness.Inbox.PlanDrafterAvailability = (true, null);
        pane.Render();

        var enabled = pane.Find("[data-testid='inbox-create-plan']");
        Assert.False(enabled.HasAttribute("disabled"));
        Assert.NotEqual("Configure Azure Foundry in Settings to create plans.", enabled.GetAttribute("title"));
    }

    [Fact]
    public async Task Move_to_backlog_raises_routed_and_the_row_says_where_it_went()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Route me", repoIds: [Repo, "JSdotNet/Other"]);
        var routed = new List<InboxRoutedDto>();
        harness.State.Routed += routed.Add;

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        var button = pane.Find("[data-testid='inbox-move-to-backlog']");
        Assert.Equal("Move to backlog → 2 tasks", button.TextContent.Trim());

        await button.ClickAsync(new());

        var raised = Assert.Single(routed);
        Assert.Equal(item.Id, raised.InboxItemId);
        Assert.Equal(2, raised.TaskIds.Count);

        // The item stays on its row, marked, and the detail says so; the acts
        // that decide are gone and only filing remains.
        Assert.Equal("Routed", pane.Find("[data-testid='inbox-pane-item-routed']").TextContent.Trim());
        Assert.Contains("Routed to 2 tasks on", pane.Find("[data-testid='inbox-detail-state']").TextContent);
        Assert.Empty(pane.FindAll("[data-testid='inbox-move-to-backlog']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-archive']"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-move-to-list']"));
        // And it no longer counts as waiting.
        Assert.Equal("0", pane.Find("[data-testid='inbox-nav-inbox-count']").TextContent.Trim());
    }

    [Fact]
    public async Task Create_plan_raises_routed_when_the_drafter_answers()
    {
        using var harness = Harness.Create();
        harness.Inbox.PlanDrafterAvailability = (true, null);
        harness.Inbox.PlanEntries = 3;
        var item = harness.Inbox.Seed("Plan me");
        var routed = new List<InboxRoutedDto>();
        harness.State.Routed += routed.Add;

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-create-plan']").ClickAsync(new());

        Assert.Equal(3, Assert.Single(routed).TaskIds.Count);
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-plan-created");
    }

    [Fact]
    public async Task Archiving_takes_the_row_out_and_the_detail_says_archived()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Dismiss me");
        harness.Inbox.Seed("Keep me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-archive']").ClickAsync(new());

        Assert.Equal(["Keep me"], Titles(pane));
        Assert.Equal("Archived.", pane.Find("[data-testid='inbox-detail-state']").TextContent.Trim());
        Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(item.Id)!.Status);
    }

    // --- Delete -------------------------------------------------------------

    [Fact]
    public async Task Delete_asks_first_and_cancelling_keeps_the_item()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Maybe delete me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-delete']").ClickAsync(new());

        Assert.Contains("Archive keeps an item findable", pane.Find("[data-testid='inbox-delete-dialog']").TextContent);

        await pane.Find("[data-testid='inbox-delete-cancel']").ClickAsync(new());

        Assert.Empty(harness.Inbox.Deleted);
        Assert.Equal(["Maybe delete me"], Titles(pane));
    }

    [Fact]
    public async Task Confirming_delete_removes_the_item_and_clears_the_detail()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Delete me");
        harness.Inbox.Seed("Keep me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-delete']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-delete-confirm']").ClickAsync(new());

        Assert.Equal([item.Id], harness.Inbox.Deleted);
        Assert.Null(harness.Inbox.Find(item.Id));
        Assert.Equal(["Keep me"], Titles(pane));
        Assert.NotNull(pane.Find("[data-testid='inbox-detail-empty']"));
    }

    /// <summary>Archive goes once an item is decided; delete does not, because
    /// clearing out the archive is exactly what it is for.</summary>
    [Fact]
    public async Task An_archived_item_can_still_be_deleted()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Dismiss me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-archive']").ClickAsync(new());

        Assert.Empty(pane.FindAll("[data-testid='inbox-archive']"));
        Assert.NotNull(pane.Find("[data-testid='inbox-delete']"));
    }

    // --- Queue health ---------------------------------------------------------

    [Fact]
    public async Task The_queue_health_strip_counts_every_unprocessed_item_and_calls_out_the_stale_ones()
    {
        var now = new FakeInboxItems().Now;
        using var harness = Harness.Create(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now));
        var resources = harness.Inbox.SeedList("Resources");
        harness.Inbox.Seed("Fresh", capturedAt: now.AddHours(-2));
        harness.Inbox.Seed("Filed and old", listId: resources.Id, capturedAt: now.AddDays(-20));
        harness.Inbox.Seed("Just over", capturedAt: now.AddDays(-15));
        harness.Inbox.Seed("Put aside long ago", status: InboxStatus.Deferred, capturedAt: now.AddDays(-40));
        harness.Inbox.Seed("Dismissed long ago", status: InboxStatus.Archived, capturedAt: now.AddDays(-60));

        var pane = await harness.RenderAsync();

        Assert.Equal("3 unprocessed items", pane.Find("[data-testid='inbox-queue-health-count']").TextContent.Trim());
        Assert.Equal("oldest captured 20d ago", pane.Find("[data-testid='inbox-queue-health-oldest']").TextContent.Trim());
        Assert.Equal("2 over 14 days", pane.Find("[data-testid='inbox-queue-health-stale']").TextContent.Trim());
    }

    [Fact]
    public async Task The_queue_health_strip_has_no_chip_when_nothing_has_waited_too_long()
    {
        // Pinned to the fake module's clock: against the system clock the seeded
        // date goes stale by itself fourteen days after it.
        var now = new FakeInboxItems().Now;
        using var harness = Harness.Create(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now));
        harness.Inbox.Seed("Fresh", capturedAt: now);

        var pane = await harness.RenderAsync();

        Assert.Empty(pane.FindAll("[data-testid='inbox-queue-health-stale']"));
    }

    [Fact]
    public async Task An_empty_queue_says_nothing_is_waiting()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Dismissed", status: InboxStatus.Archived);

        var pane = await harness.RenderAsync();

        Assert.Equal("Nothing waiting", pane.Find("[data-testid='inbox-queue-health-count']").TextContent.Trim());
        Assert.Empty(pane.FindAll("[data-testid='inbox-queue-health-oldest']"));
    }

    // --- Deferral ------------------------------------------------------------

    [Fact]
    public async Task Deferring_with_a_date_moves_the_row_to_the_deferred_slice_and_counts_it_there()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Read it next week");
        harness.Inbox.Seed("Keep me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-defer-date'] input").InputAsync(new ChangeEventArgs { Value = "2026-09-21" });
        await pane.Find("[data-testid='inbox-defer']").ClickAsync(new());

        Assert.Equal(["Keep me"], Titles(pane));
        Assert.Equal("1", pane.Find("[data-testid='inbox-nav-inbox-count']").TextContent.Trim());
        Assert.Equal("1", pane.Find("[data-testid='inbox-nav-deferred-count']").TextContent.Trim());
        Assert.Equal("Deferred until 21 Sep 2026.", pane.Find("[data-testid='inbox-detail-state']").TextContent.Trim());
        Assert.Equal(new DateOnly(2026, 9, 21), harness.Inbox.Find(item.Id)!.DeferredUntil);

        await pane.Find("[data-testid='inbox-nav-deferred']").ClickAsync(new());

        Assert.Equal(["Read it next week"], Titles(pane));
    }

    [Fact]
    public async Task Deferring_with_no_date_keeps_it_aside_until_returned()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Someday");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-defer']").ClickAsync(new());

        Assert.Equal("Deferred with no review date.", pane.Find("[data-testid='inbox-detail-state']").TextContent.Trim());
        Assert.Equal("Change date", pane.Find("[data-testid='inbox-defer']").TextContent.Trim());

        await pane.Find("[data-testid='inbox-return']").ClickAsync(new());

        Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(item.Id)!.Status);
        Assert.Equal(["Someday"], Titles(pane));
        Assert.Empty(pane.FindAll("[data-testid='inbox-return']"));
    }

    /// <summary>The deferred slice spans lists and runs in the order items will
    /// come back: soonest date first, undated last.</summary>
    [Fact]
    public async Task The_deferred_slice_lists_every_deferred_item_soonest_first()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");
        harness.Inbox.Seed("Undated", status: InboxStatus.Deferred);
        harness.Inbox.Seed("Later", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 12, 1), listId: resources.Id);
        harness.Inbox.Seed("Sooner", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 1));
        harness.Inbox.Seed("Waiting");

        var pane = await harness.RenderAsync();

        Assert.Equal("3", pane.Find("[data-testid='inbox-nav-deferred-count']").TextContent.Trim());
        Assert.Equal("0", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}-count']").TextContent.Trim());

        await pane.Find("[data-testid='inbox-nav-deferred']").ClickAsync(new());

        Assert.Equal(["Sooner", "Later", "Undated"], Titles(pane));
    }

    /// <summary>Opening the pane is the sweep: an item whose date has passed is
    /// in the queue before anyone reads it, and the toast says why.</summary>
    [Fact]
    public async Task Opening_the_pane_brings_back_every_deferred_item_whose_date_has_passed()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Due yesterday", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 9, 13));
        harness.Inbox.Seed("Due tomorrow", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 9, 15));

        var pane = await harness.RenderAsync();

        Assert.Equal(1, harness.Inbox.ResurfaceDueCalls);
        Assert.Equal(["Due yesterday"], Titles(pane));
        Assert.Equal("1", pane.Find("[data-testid='inbox-nav-deferred-count']").TextContent.Trim());
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-resurfaced");

        // Open again later: the sweep runs again.
        harness.Inbox.Now = harness.Inbox.Now.AddDays(1);
        await pane.InvokeAsync(harness.State.OpenedAsync);

        Assert.Equal(2, harness.Inbox.ResurfaceDueCalls);
        Assert.Equal("0", pane.Find("[data-testid='inbox-nav-deferred-count']").TextContent.Trim());
    }

    [Fact]
    public async Task A_sweep_that_brings_nothing_back_says_nothing()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("Not yet", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 1));

        await harness.RenderAsync();

        Assert.DoesNotContain(harness.Toasts.Visible, toast => toast.TestId == "inbox-resurfaced");
    }

    [Fact]
    public async Task Move_to_list_files_the_item_and_the_row_moves_to_that_list()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");
        var item = harness.Inbox.Seed("File me");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-move-to-list']").ClickAsync(new());

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-move-list-dialog']"));
        await pane.Find($"[data-testid='inbox-move-list-{InboxDesktopState.ListNavId(resources.Id)}']").ClickAsync(new());

        Assert.Equal(resources.Id, harness.Inbox.Find(item.Id)!.ListId);
        Assert.Empty(pane.FindAll("[data-testid='inbox-move-list-dialog']"));
        Assert.Empty(Titles(pane));
        Assert.Equal("1", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}-count']").TextContent.Trim());
    }

    [Fact]
    public async Task A_refused_act_lands_on_a_toast()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Route me twice");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        await harness.State.RouteToBacklogAsync();
        await harness.State.RouteToBacklogAsync();

        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-error" && toast.Severity == ToastSeverity.Error);
    }

    // --- Add and Capture ----------------------------------------------------

    /// <summary>Both actions sit in the header whether or not there is anything
    /// in the queue: an empty Inbox is exactly where somebody reaches for Add.</summary>
    [Fact]
    public async Task The_header_offers_add_and_capture_when_the_inbox_is_empty()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        Assert.Equal("Add", pane.Find("[data-testid='inbox-pane-add']").TextContent.Trim());
        Assert.Equal("Capture", pane.Find("[data-testid='inbox-pane-capture']").TextContent.Trim());
    }

    [Fact]
    public async Task The_header_offers_add_and_capture_when_there_are_items()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("One");
        harness.Inbox.Seed("Two");

        var pane = await harness.RenderAsync();

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-pane-add']"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-pane-capture']"));
    }

    [Fact]
    public async Task Add_opens_a_dialog_with_a_title_and_notes()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-add-dialog']"));

        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());

        var dialog = pane.Find("[data-testid='inbox-pane-add-dialog']");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-pane-add-title']"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-pane-add-notes']"));
    }

    /// <summary>The notes land in <c>BodyMd</c> and the detail pane reads them
    /// back as markdown, so the box they are written in is the shared markdown
    /// editor — a formatting toolbar over the source — rather than a plain
    /// textarea. Labelled "Notes", and the label is the textarea's own name.</summary>
    [Fact]
    public async Task Notes_are_written_in_the_markdown_editor()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());

        var notes = pane.Find("[data-testid='inbox-pane-add-notes']");
        Assert.Contains("markdown-editor", notes.ClassList);
        Assert.NotEmpty(notes.QuerySelectorAll("[role='toolbar'] button"));
        Assert.NotNull(notes.QuerySelector("[data-testid='markdown-editor-bullet']"));

        var textarea = notes.QuerySelector("textarea");
        Assert.NotNull(textarea);
        var label = pane.Find($"label[for='{textarea.GetAttribute("id")}']");
        Assert.Equal("Notes", label.TextContent);
    }

    /// <summary>The module refuses an item without a title, so the dialog does
    /// not offer to try.</summary>
    [Fact]
    public async Task Submit_is_disabled_until_a_title_is_typed()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());

        Assert.True(pane.Find("[data-testid='inbox-pane-add-submit']").HasAttribute("disabled"));

        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "   " });
        Assert.True(pane.Find("[data-testid='inbox-pane-add-submit']").HasAttribute("disabled"));
        Assert.Empty(harness.Inbox.Items);

        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "Ask about the trial length" });
        Assert.False(pane.Find("[data-testid='inbox-pane-add-submit']").HasAttribute("disabled"));
    }

    /// <summary>Asserted on what the module received and on what the pane shows,
    /// never on a throw: bUnit swallows a handler's exception, so a submit that
    /// did nothing would look exactly like one that threw.</summary>
    [Fact]
    public async Task Submitting_files_a_manual_item_with_the_notes_as_its_body_and_opens_it()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "  Ask about the trial length " });
        await pane.Find("[data-testid='inbox-pane-add-notes'] textarea").InputAsync(new ChangeEventArgs { Value = "Before Friday.\n" });
        await pane.Find("[data-testid='inbox-pane-add-submit']").ClickAsync(new());

        var item = Assert.Single(harness.Inbox.Items);
        Assert.Equal("Ask about the trial length", item.Title);
        Assert.Equal("Before Friday.", item.BodyMd);
        Assert.Equal("manual", item.Channel);

        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-add-dialog']"));
        Assert.Equal(["Ask about the trial length"], Titles(pane));
        Assert.Equal("Manual", pane.Find("[data-testid='inbox-pane-item-source']").TextContent.Trim());
        // The new item is selected, so the detail opens on it with the notes as its body.
        Assert.Equal("Ask about the trial length", pane.Find("[data-testid='inbox-detail'] #inbox-detail-title").TextContent.Trim());
        Assert.Contains("Before Friday.", pane.Find("[data-testid='inbox-detail']").TextContent, StringComparison.Ordinal);

        // The next Add starts clean rather than with the last one still in it.
        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        Assert.Equal(string.Empty, pane.Find("[data-testid='inbox-pane-add-title'] input").GetAttribute("value"));
    }

    [Fact]
    public async Task Notes_are_optional_and_none_is_an_empty_body()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "Just a title" });
        await pane.Find("[data-testid='inbox-pane-add-submit']").ClickAsync(new());

        var item = Assert.Single(harness.Inbox.Items);
        Assert.Equal("Just a title", item.Title);
        Assert.Equal(string.Empty, item.BodyMd);
        Assert.Equal(["Just a title"], Titles(pane));
    }

    [Fact]
    public async Task Cancel_closes_the_dialog_and_files_nothing()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "Something" });
        await pane.Find("[data-testid='inbox-pane-add-cancel']").ClickAsync(new());

        Assert.Empty(harness.Inbox.Items);
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-add-dialog']"));
    }

    /// <summary>The dialog has closed by the time the module answers, so a
    /// refusal is a toast of its own rather than the item acts' one.</summary>
    [Fact]
    public async Task A_refused_add_is_a_toast_under_its_own_id()
    {
        using var harness = Harness.Create();
        harness.Inbox.NextCaptureError = Error.Unexpected("inbox.store_failed", "The inbox database is locked.");

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new ChangeEventArgs { Value = "Ask about the trial length" });
        await pane.Find("[data-testid='inbox-pane-add-submit']").ClickAsync(new());

        Assert.Empty(harness.Inbox.Items);
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-add-dialog']"));
        var toast = Assert.Single(harness.Toasts.Visible);
        Assert.Equal("inbox-add-error", toast.TestId);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains("locked", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_raises_OnCapture()
    {
        using var harness = Harness.Create();

        var raised = 0;
        var pane = await harness.RenderAsync(parameters => parameters
            .Add(p => p.OnCapture, () => raised++));

        await pane.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        Assert.Equal(1, raised);
    }

    /// <summary>While a run is in flight the button is busy rather than gone,
    /// so focus survives the round trip and a second press cannot start a
    /// second run.</summary>
    [Fact]
    public async Task The_capture_button_is_busy_while_a_run_is_in_flight()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync(parameters => parameters
            .Add(p => p.CaptureRunning, true));

        var button = pane.Find("[data-testid='inbox-pane-capture']");
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_capture_message_is_shown_as_a_status_alert()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync(parameters => parameters
            .Add(p => p.CaptureMessage, "YouTube: no adapter is available yet. 0 new items."));

        var result = pane.Find("[data-testid='inbox-pane-capture-result']");
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Contains("inbox-pane__capture-result", result.ClassList);
        Assert.Contains("no adapter", result.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_message_renders_no_alert()
    {
        using var harness = Harness.Create();
        harness.Inbox.Seed("One");

        var pane = await harness.RenderAsync();

        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-capture-result']"));
    }

    // --- The context menu ---------------------------------------------------

    [Fact]
    public async Task The_menu_offers_what_the_target_can_do()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");
        var resources = harness.Inbox.SeedList("Resources");

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-nav-inbox']").ContextMenuAsync(new MouseEventArgs { ClientX = 10, ClientY = 20 });
        Assert.Equal(["new-list", "new-group"], MenuIds(pane));
        await CloseMenuAsync(pane);

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']").ContextMenuAsync(new MouseEventArgs());
        Assert.Equal(["rename-group", "new-list-in-group", "ungroup"], MenuIds(pane));
        await CloseMenuAsync(pane);

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ContextMenuAsync(new MouseEventArgs());
        Assert.Equal(["rename-list", "move-to-group", "delete-list"], MenuIds(pane));
        Assert.Contains("menu-list__item--destructive", pane.Find("[data-testid='inbox-nav-menu-item-delete-list']").ClassList);
    }

    [Fact]
    public async Task New_list_makes_a_list_and_opens_its_name_for_typing()
    {
        using var harness = Harness.Create();

        var pane = await harness.RenderAsync();

        await pane.Find("[data-testid='inbox-nav']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-new-list']").ClickAsync(new());

        var created = Assert.Single(harness.Inbox.Lists, list => list.Name.StartsWith("New list", StringComparison.Ordinal));
        var field = pane.Find("[data-testid='inbox-nav-rename']");
        Assert.Equal(created.Name, field.GetAttribute("value"));

        await field.InputAsync(new ChangeEventArgs { Value = "Reading" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Contains(harness.Inbox.Lists, list => list.Name == "Reading");
        Assert.Empty(pane.FindAll("[data-testid='inbox-nav-rename']"));
        // The new list is the open slice.
        Assert.Equal(InboxDesktopState.ListNavId(created.Id), harness.State.SelectedSliceId);
    }

    [Fact]
    public async Task New_list_in_a_group_lands_in_that_group_and_new_group_makes_a_group()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-new-list-in-group']").ClickAsync(new());

        Assert.Equal(areas.Id, Assert.Single(harness.Inbox.Lists).GroupId);
        await pane.Find("[data-testid='inbox-nav-rename']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await pane.Find("[data-testid='inbox-nav']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-new-group']").ClickAsync(new());

        Assert.Equal(2, harness.Inbox.Groups.Count);
        Assert.Equal("New group", pane.Find("[data-testid='inbox-nav-rename']").GetAttribute("value"));
    }

    // --- Inline rename ------------------------------------------------------

    [Fact]
    public async Task F2_opens_the_name_enter_commits_it()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");

        var pane = await harness.RenderAsync();

        var row = pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']");
        await row.KeyDownAsync(new KeyboardEventArgs { Key = "F2" });

        var field = pane.Find("[data-testid='inbox-nav-rename']");
        Assert.Equal("Resources", field.GetAttribute("value"));

        await field.InputAsync(new ChangeEventArgs { Value = "  Reading list  " });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Reading list", harness.Inbox.Lists.Single().Name);
        Assert.Empty(pane.FindAll("[data-testid='inbox-nav-rename']"));
        Assert.Contains("Reading list", pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").TextContent);
    }

    [Fact]
    public async Task Escape_cancels_a_rename_and_keeps_the_name()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-rename-list']").ClickAsync(new());

        var field = pane.Find("[data-testid='inbox-nav-rename']");
        await field.InputAsync(new ChangeEventArgs { Value = "Something else" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Resources", harness.Inbox.Lists.Single().Name);
        Assert.Empty(pane.FindAll("[data-testid='inbox-nav-rename']"));
    }

    [Fact]
    public async Task A_group_renames_the_same_way()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-rename-group']").ClickAsync(new());

        var field = pane.Find("[data-testid='inbox-nav-rename']");
        await field.InputAsync(new ChangeEventArgs { Value = "Domains" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Domains", harness.Inbox.Groups.Single().Name);
    }

    [Fact]
    public async Task A_refused_rename_keeps_the_field_open_with_the_reason_on_a_toast()
    {
        using var harness = Harness.Create();
        harness.Inbox.SeedList("Resources");
        var updates = harness.Inbox.SeedList("Updates");

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(updates.Id)}']").KeyDownAsync(new KeyboardEventArgs { Key = "F2" });
        var field = pane.Find("[data-testid='inbox-nav-rename']");
        await field.InputAsync(new ChangeEventArgs { Value = "resources" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-nav-rename']"));
        Assert.Contains(harness.Toasts.Visible, toast => toast.Message.Contains("already a list", StringComparison.Ordinal));
        Assert.Equal("Updates", harness.Inbox.Lists.Single(list => list.Id == updates.Id).Name);
    }

    // --- Delete, move to group, ungroup -------------------------------------

    [Fact]
    public async Task Deleting_a_list_asks_first_and_its_rows_return_to_the_inbox()
    {
        using var harness = Harness.Create();
        var resources = harness.Inbox.SeedList("Resources");
        harness.Inbox.Seed("Filed", listId: resources.Id);

        var pane = await harness.RenderAsync();
        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ClickAsync(new());
        Assert.Equal(["Filed"], Titles(pane));

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-delete-list']").ClickAsync(new());

        var dialog = pane.Find("[data-testid='inbox-delete-list-dialog']");
        Assert.Contains("1 item in \"Resources\" return to Inbox", dialog.TextContent);
        Assert.Single(harness.Inbox.Lists);

        // Cancel first: nothing happens.
        await pane.Find("[data-testid='inbox-delete-list-cancel']").ClickAsync(new());
        Assert.Single(harness.Inbox.Lists);
        Assert.Empty(pane.FindAll("[data-testid='inbox-delete-list-dialog']"));

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-delete-list']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-delete-list-confirm']").ClickAsync(new());

        Assert.Empty(harness.Inbox.Lists);
        Assert.Empty(pane.FindAll($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']"));
        // The open slice fell back to the inbox, where the row now is.
        Assert.Equal(InboxDesktopState.InboxSliceId, harness.State.SelectedSliceId);
        Assert.Equal(["Filed"], Titles(pane));
        Assert.Equal("1", pane.Find("[data-testid='inbox-nav-inbox-count']").TextContent.Trim());
    }

    [Fact]
    public async Task Move_to_group_offers_the_other_groups_and_files_the_list_under_the_chosen_one()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");
        var projects = harness.Inbox.SeedGroup("Projects");
        var resources = harness.Inbox.SeedList("Resources", areas.Id);

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-move-to-group']").ClickAsync(new());

        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-move-group-dialog']"));
        // Not the group it is in; "No group" because it is in one.
        Assert.Empty(pane.FindAll($"[data-testid='inbox-move-group-{InboxDesktopState.GroupNavId(areas.Id)}']"));
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-move-group-none']"));

        await pane.Find($"[data-testid='inbox-move-group-{InboxDesktopState.GroupNavId(projects.Id)}']").ClickAsync(new());

        Assert.Equal(projects.Id, harness.Inbox.Lists.Single().GroupId);
        Assert.Empty(pane.FindAll("[data-testid='inbox-move-group-dialog']"));
        Assert.NotEmpty(pane.FindAll($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(projects.Id)}-group'] [data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']"));
    }

    [Fact]
    public async Task Ungroup_moves_the_lists_to_the_top_and_removes_the_group()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");
        var resources = harness.Inbox.SeedList("Resources", areas.Id);

        var pane = await harness.RenderAsync();

        await pane.Find($"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']").ContextMenuAsync(new MouseEventArgs());
        await pane.Find("[data-testid='inbox-nav-menu-item-ungroup']").ClickAsync(new());

        Assert.Empty(harness.Inbox.Groups);
        Assert.Null(harness.Inbox.Lists.Single().GroupId);
        Assert.NotEmpty(pane.FindAll($"[data-testid='inbox-nav-ungrouped'] [data-testid='inbox-nav-{InboxDesktopState.ListNavId(resources.Id)}']"));
    }

    [Fact]
    public async Task A_group_folds_and_unfolds_on_its_heading()
    {
        using var harness = Harness.Create();
        var areas = harness.Inbox.SeedGroup("Areas");
        harness.Inbox.SeedList("Resources", areas.Id);

        var pane = await harness.RenderAsync();

        var heading = $"[data-testid='inbox-nav-{InboxDesktopState.GroupNavId(areas.Id)}']";
        Assert.Equal("true", pane.Find(heading).GetAttribute("aria-expanded"));

        await pane.Find(heading).ClickAsync(new());
        Assert.Equal("false", pane.Find(heading).GetAttribute("aria-expanded"));
        Assert.False(harness.State.IsGroupExpanded(areas.Id));

        await pane.Find(heading).ClickAsync(new());
        Assert.True(harness.State.IsGroupExpanded(areas.Id));
    }

    // --- Picking several ----------------------------------------------------

    [Fact]
    public async Task Select_puts_a_box_on_every_row_and_a_shift_click_takes_the_range_with_a_running_count()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "One", "Two", "Three", "Four");

        var pane = await harness.RenderAsync();

        // No boxes and no bar until the reader asks for them.
        Assert.Empty(pane.FindAll("[data-testid^='inbox-pick-']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-bulk-bar']"));

        await pane.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());

        Assert.Equal(4, pane.FindAll("[data-testid^='inbox-pick-']").Count);
        Assert.Equal("0 items selected", pane.Find("[data-testid='inbox-bulk-bar-count']").TextContent.Trim());

        await PickAsync(pane, items[0].Id);
        await PickAsync(pane, items[2].Id, shift: true);

        Assert.Equal("3 items selected", pane.Find("[data-testid='inbox-bulk-bar-count']").TextContent.Trim());
        Assert.Equal([items[0].Id, items[1].Id, items[2].Id], harness.State.SelectedItems.Select(item => item.Id));
        Assert.Equal(3, pane.FindAll(".inbox-pane__item--picked").Count);

        // A shift press that unticks gives the run back, measured from the last press.
        await PickAsync(pane, items[1].Id, shift: true);

        Assert.Equal([items[0].Id], harness.State.SelectedItems.Select(item => item.Id));
        Assert.Equal("1 item selected", pane.Find("[data-testid='inbox-bulk-bar-count']").TextContent.Trim());
    }

    [Fact]
    public async Task Archive_across_the_selection_goes_through_one_batch_and_says_how_many()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "One", "Two", "Three");

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());
        await PickAsync(pane, items[0].Id);
        await PickAsync(pane, items[1].Id);
        await pane.Find("[data-testid='inbox-bulk-archive']").ClickAsync(new());

        var batch = Assert.Single(harness.Inbox.Batches);
        Assert.Equal("archive", batch.Act);
        Assert.Equal([items[0].Id, items[1].Id], batch.Ids);
        Assert.Equal(["Three"], Titles(pane));

        // The archived rows left the view, and the selection with them.
        Assert.Equal(0, harness.State.SelectionCount);

        var toast = Assert.Single(harness.Toasts.Visible, toast => toast.TestId == InboxDesktopState.BulkResultTestId);
        Assert.Equal(ToastSeverity.Info, toast.Severity);
        Assert.Equal("2 items archived.", toast.Message);
    }

    [Fact]
    public async Task A_partial_failure_names_the_item_it_could_not_change_rather_than_reporting_success()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "Fine", "Stubborn");
        harness.Inbox.Refuse[items[1].Id] = InboxErrors.InvalidTransition("An item that is triaged cannot be archived.");

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-bulk-bar-select-all'] input").ChangeAsync(new ChangeEventArgs { Value = true });
        await pane.Find("[data-testid='inbox-bulk-archive']").ClickAsync(new());

        var toast = Assert.Single(harness.Toasts.Visible, toast => toast.TestId == InboxDesktopState.BulkResultTestId);
        Assert.Equal(ToastSeverity.Warning, toast.Severity);
        Assert.Equal(
            "1 item archived. Not changed — \"Stubborn\": An item that is triaged cannot be archived.",
            toast.Message);

        // The refused item is still on screen and still picked, so the reader can see what is left.
        Assert.Equal(["Stubborn"], Titles(pane));
        Assert.Equal([items[1].Id], harness.State.SelectedIds);
    }

    [Fact]
    public async Task Move_to_list_across_the_selection_files_every_item_there()
    {
        using var harness = Harness.Create();
        var reading = harness.Inbox.SeedList("Reading");
        var items = SeedNewestFirst(harness, "One", "Two");

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());
        await PickAsync(pane, items[0].Id);
        await PickAsync(pane, items[1].Id);
        await pane.Find("[data-testid='inbox-bulk-move']").ClickAsync(new());
        await pane.Find($"[data-testid='inbox-bulk-move-list-{InboxDesktopState.ListNavId(reading.Id)}']").ClickAsync(new());

        Assert.All(items, item => Assert.Equal(reading.Id, harness.Inbox.Find(item.Id)!.ListId));
        Assert.Empty(Titles(pane));
        Assert.Equal("2 items moved to Reading.", harness.Toasts.Visible.Single(toast => toast.TestId == InboxDesktopState.BulkResultTestId).Message);
    }

    [Fact]
    public async Task Adding_tags_keeps_each_items_own_and_leaves_an_item_that_has_them_alone()
    {
        using var harness = Harness.Create();
        var items = SeedNewestFirst(harness, "Tagged already", "Has its own", "Bare");
        await harness.Inbox.SetTagsAsync(items[0].Id, ["q4"], TestContext.Current.CancellationToken);
        await harness.Inbox.SetTagsAsync(items[1].Id, ["reading"], TestContext.Current.CancellationToken);

        var pane = await harness.RenderAsync();
        await pane.InvokeAsync(() => harness.State.SetSelectAllVisible(true));
        var outcome = await pane.InvokeAsync(() => harness.State.BulkAddTagsAsync(["q4"]));

        Assert.Equal(2, outcome.Changed);
        Assert.Equal(1, outcome.Unchanged);
        // The item already carrying the tag was not written at all.
        Assert.Equal([items[1].Id, items[2].Id], Assert.Single(harness.Inbox.Batches).Ids);
        Assert.Equal(["reading", "q4"], harness.Inbox.Find(items[1].Id)!.Tags.Select(tag => tag.Name));
        Assert.Equal(["q4"], harness.Inbox.Find(items[2].Id)!.Tags.Select(tag => tag.Name));
        Assert.Equal("2 items tagged q4, 1 already up to date.", harness.Toasts.Visible.Single(toast => toast.TestId == InboxDesktopState.BulkResultTestId).Message);

        var removed = await pane.InvokeAsync(() => harness.State.BulkRemoveTagAsync("q4"));

        Assert.Equal(3, removed.Changed);
        Assert.Equal(["reading"], harness.Inbox.Find(items[1].Id)!.Tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task Assigning_repositories_replaces_them_and_names_a_routed_item_it_will_not_touch()
    {
        using var harness = Harness.Create();
        var open = harness.Inbox.Seed("Open", repoIds: ["JSdotNet/Old"], capturedAt: harness.Inbox.Now.AddMinutes(1));
        var routed = harness.Inbox.Seed("Routed", status: InboxStatus.Triaged);

        var pane = await harness.RenderAsync();
        await pane.InvokeAsync(() => harness.State.SetSelectAllVisible(true));
        var outcome = await pane.InvokeAsync(() => harness.State.BulkAssignRepositoriesAsync([Repo]));

        Assert.Equal(1, outcome.Changed);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal(routed.Id, failure.Id);
        Assert.Equal(InboxErrors.ItemAlreadyDecided, failure.Error);
        Assert.Equal([Repo], harness.Inbox.Find(open.Id)!.RepoIds);
        Assert.Empty(harness.Inbox.Find(routed.Id)!.RepoIds);
        Assert.Contains("\"Routed\"", harness.Toasts.Visible.Single(toast => toast.TestId == InboxDesktopState.BulkResultTestId).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selection_survives_a_refresh_is_pruned_by_a_filter_and_clears_when_the_slice_changes()
    {
        using var harness = Harness.Create();
        var reading = harness.Inbox.SeedList("Reading");
        var video = harness.Inbox.Seed("A video", ContentKind.YouTube, capturedAt: harness.Inbox.Now.AddMinutes(1));
        var note = harness.Inbox.Seed("A note");

        var pane = await harness.RenderAsync();
        await pane.InvokeAsync(() => harness.State.SetSelectAllVisible(true));

        // A refresh — a sync landing, say — keeps what is still there.
        harness.Inbox.Seed("Arrived meanwhile", capturedAt: harness.Inbox.Now.AddMinutes(2));
        await pane.InvokeAsync(harness.State.ReloadAsync);
        Assert.Equal([video.Id, note.Id], harness.State.SelectedItems.Select(item => item.Id));

        // A filter that hides a picked row takes it out of the selection.
        await pane.InvokeAsync(() => harness.State.ToggleKind(video.KindSlug));
        Assert.Equal([video.Id], harness.State.SelectedIds);

        // Another slice is another set of rows: nothing picked there yet.
        await pane.InvokeAsync(() => harness.State.SelectSlice(InboxDesktopState.ListNavId(reading.Id)));
        Assert.Equal(0, harness.State.SelectionCount);
        Assert.True(harness.State.SelectionMode);
    }

    [Fact]
    public async Task Escape_on_the_bar_puts_the_selection_down()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("One");

        var pane = await harness.RenderAsync();
        await pane.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());
        await PickAsync(pane, item.Id);
        await pane.Find("[data-testid='inbox-bulk-bar']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(harness.State.SelectionMode);
        Assert.Empty(pane.FindAll("[data-testid='inbox-bulk-bar']"));
        Assert.Empty(pane.FindAll("[data-testid^='inbox-pick-']"));
    }

    // --- The state's own arithmetic -----------------------------------------

    [Fact]
    public void A_bulk_sentence_leads_with_what_landed_and_names_what_did_not()
    {
        var outcome = new InboxBulkOutcome(
            3,
            1,
            [
                new InboxBulkFailure(Guid.NewGuid(), "Gone", InboxErrors.ItemNotFound),
                new InboxBulkFailure(Guid.NewGuid(), "Routed", InboxErrors.ItemAlreadyDecided)
            ]);

        Assert.Equal(
            "3 items archived, 1 already up to date. Not changed — \"Gone\": That inbox item no longer exists. \"Routed\": Already routed or archived, so there is nothing left to change.",
            InboxDesktopState.BulkMessage(outcome, "archived"));

        Assert.Equal("No items changed, 2 already up to date.", InboxDesktopState.BulkMessage(new InboxBulkOutcome(0, 2, []), "archived"));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1m ago")]
    [InlineData(3599, "59m ago")]
    [InlineData(3600, "1h ago")]
    [InlineData(86400, "1d ago")]
    public void An_age_is_written_in_the_short_form(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, InboxDesktopState.Age(now.AddSeconds(-secondsAgo), now));
    }

    // --- Driving it ---------------------------------------------------------

    private static IReadOnlyList<string> Titles(IRenderedComponent<InboxPane> pane) =>
        [.. pane.FindAll("[data-testid='inbox-pane-item-title']").Select(title => title.TextContent.Trim())];

    /// <summary>Seeds items a minute apart so the list draws them in the order
    /// given — newest capture first, so the first title is the newest.</summary>
    private static IReadOnlyList<InboxItemDto> SeedNewestFirst(Harness harness, params string[] titles) =>
        [.. titles.Select((title, index) => harness.Inbox.Seed(title, capturedAt: harness.Inbox.Now.AddMinutes(titles.Length - index)))];

    private static Task PickAsync(IRenderedComponent<InboxPane> pane, Guid id, bool shift = false) =>
        pane.Find($"[data-testid='inbox-pick-{id:D}'] input").ClickAsync(new MouseEventArgs { ShiftKey = shift });

    // --- Suggestions ----------------------------------------------------------

    [Fact]
    public async Task An_open_items_suggestions_are_numbered_chips_and_nothing_is_applied_until_one_is_taken()
    {
        using var harness = Harness.Create();
        harness.GitHub.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]);
        var item = harness.Inbox.Seed("The sync service drops release notes");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync", "The backlog files entries under #sync.");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Repository, Repo, "Your rule #sync => JSdotNet/Backlog.");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Destination, "tasks", "A note of your own.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);

        pane.WaitForAssertion(() =>
        {
            var chips = pane.FindAll("[data-testid='inbox-detail-suggestions'] .tag-chip");
            Assert.Equal(
                ["1#sync", "2Repository backlog", "3Move to backlog"],
                chips.Select(chip => chip.TextContent.Replace("×", string.Empty, StringComparison.Ordinal).Trim()));
        });
        var tag = pane.Find("[data-testid='inbox-suggestion-tag-sync'] .tag-chip__label");
        Assert.Equal("Add #sync: The backlog files entries under #sync.", tag.GetAttribute("aria-label"));
        Assert.Equal("1", tag.GetAttribute("aria-keyshortcuts"));

        var shown = harness.Inbox.Find(item.Id)!;
        Assert.Empty(shown.Tags);
        Assert.Empty(shown.RepoIds);
        Assert.Equal(InboxStatus.Unprocessed, shown.Status);
    }

    [Fact]
    public async Task Pressing_a_chip_adds_its_tag_and_the_chip_leaves_the_row()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("About sync", tags: ["infra"]);
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-tag-sync']");

        await pane.Find("[data-testid='inbox-suggestion-tag-sync'] .tag-chip__label").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["infra", "sync"], harness.Inbox.Find(item.Id)!.Tags.Select(tag => tag.Name));
            Assert.Empty(pane.FindAll("[data-testid='inbox-suggestion-tag-sync']"));
        });
    }

    [Fact]
    public async Task A_digit_on_the_detail_or_on_the_rows_takes_the_chip_with_that_number()
    {
        using var harness = Harness.Create();
        harness.GitHub.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]);
        var item = harness.Inbox.Seed("The sync service");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Repository, Repo);

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-repository-jsdotnet-backlog']");

        // The detail takes focus from a click on its text, out of the tab order,
        // which is how the digits reach it after reading the item.
        Assert.Equal("-1", pane.Find("[data-testid='inbox-detail']").GetAttribute("tabindex"));

        // "2" on the detail takes the second chip, the repository.
        await pane.Find("[data-testid='inbox-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "2" });
        pane.WaitForAssertion(() => Assert.Equal([Repo], harness.Inbox.Find(item.Id)!.RepoIds));

        // "1" on the rows, where focus is right after choosing one, takes the tag.
        pane.WaitForElement("[data-testid='inbox-suggestion-tag-sync']");
        await pane.Find("[data-testid='inbox-pane-list']").KeyDownAsync(new KeyboardEventArgs { Key = "1" });
        pane.WaitForAssertion(() => Assert.Equal(["sync"], harness.Inbox.Find(item.Id)!.Tags.Select(tag => tag.Name)));
    }

    [Fact]
    public async Task A_digit_typed_into_a_picker_or_with_a_modifier_held_takes_nothing()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("The sync service");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-tag-sync']");

        await pane.Find("[data-testid='inbox-detail-tags'] .tag-select__input").KeyDownAsync(new KeyboardEventArgs { Key = "1" });
        await pane.Find("[data-testid='inbox-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "1", CtrlKey = true });
        await pane.Find("[data-testid='inbox-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "7" });

        Assert.Empty(harness.Inbox.Find(item.Id)!.Tags);
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-suggestion-tag-sync']"));
    }

    [Fact]
    public async Task A_dismissed_suggestion_is_recorded_and_does_not_come_back_after_a_reload()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("The sync service");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Tag, "sync");
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Destination, "tasks");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-tag-sync']");

        await pane.Find("[data-testid='inbox-suggestion-tag-sync'] [aria-label='Dismiss #sync']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-suggestion-tag-sync']")));
        Assert.Contains((item.Id, "tag:sync"), harness.Inbox.Dismissed);
        Assert.Empty(harness.Inbox.Find(item.Id)!.Tags);

        await pane.InvokeAsync(harness.State.ReloadAsync);

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='inbox-suggestion-tag-sync']"));
            // The one left is now number 1.
            Assert.Equal("1", pane.Find("[data-testid='inbox-suggestion-destination-tasks'] .tag-chip__key").TextContent);
        });
    }

    [Fact]
    public async Task Taking_the_backlog_suggestion_routes_the_item_and_taking_archive_archives_it()
    {
        using var harness = Harness.Create();
        var work = harness.Inbox.Seed("Fix the flaky test");
        var digest = harness.Inbox.Seed("Weekly digest");
        harness.Inbox.SeedSuggestion(work.Id, InboxSuggestionKind.Destination, "tasks");
        harness.Inbox.SeedSuggestion(digest.Id, InboxSuggestionKind.Destination, "archive");
        var routed = new List<InboxRoutedDto>();
        harness.State.Routed += routed.Add;

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, work.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-destination-tasks']");
        await pane.Find("[data-testid='inbox-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "1" });

        pane.WaitForAssertion(() => Assert.Equal(work.Id, Assert.Single(routed).InboxItemId));
        // A decided item is offered nothing more.
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='inbox-detail-suggestions']")));

        await harness.SelectAsync(pane, digest.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-destination-archive']");
        await pane.Find("[data-testid='inbox-suggestion-destination-archive'] .tag-chip__label").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Archived, harness.Inbox.Find(digest.Id)!.Status));
    }

    [Fact]
    public async Task A_knowledge_suggestion_says_why_it_cannot_be_taken_has_no_number_and_can_still_be_dismissed()
    {
        using var harness = Harness.Create();
        var item = harness.Inbox.Seed("Local-first patterns", ContentKind.Article);
        harness.Inbox.SeedSuggestion(item.Id, InboxSuggestionKind.Destination, "devbook", "Collected material.", "Keeping an item as knowledge is not built yet.");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, item.Id);
        var chip = pane.WaitForElement("[data-testid='inbox-suggestion-destination-devbook']");

        Assert.Contains("Keep as knowledge", chip.TextContent, StringComparison.Ordinal);
        Assert.Contains("not built yet", chip.GetAttribute("title"), StringComparison.Ordinal);
        Assert.Empty(chip.QuerySelectorAll("button.tag-chip__label"));
        Assert.Empty(chip.QuerySelectorAll(".tag-chip__key"));

        await pane.Find("[data-testid='inbox-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "1" });
        Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(item.Id)!.Status);

        await pane.Find("[data-testid='inbox-suggestion-destination-devbook'] [aria-label='Dismiss Keep as knowledge']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.Contains((item.Id, "destination:devbook"), harness.Inbox.Dismissed));
    }

    [Fact]
    public async Task Choosing_another_item_shows_that_items_suggestions()
    {
        using var harness = Harness.Create();
        var first = harness.Inbox.Seed("First");
        var second = harness.Inbox.Seed("Second");
        harness.Inbox.SeedSuggestion(first.Id, InboxSuggestionKind.Tag, "alpha");
        harness.Inbox.SeedSuggestion(second.Id, InboxSuggestionKind.Tag, "beta");

        var pane = await harness.RenderAsync();
        await harness.SelectAsync(pane, first.Id);
        pane.WaitForElement("[data-testid='inbox-suggestion-tag-alpha']");

        await harness.SelectAsync(pane, second.Id);

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll("[data-testid='inbox-suggestion-tag-beta']"));
            Assert.Empty(pane.FindAll("[data-testid='inbox-suggestion-tag-alpha']"));
        });
    }

    private static IReadOnlyList<string> MenuIds(IRenderedComponent<InboxPane> pane) =>
        [.. pane.FindAll("[data-testid='inbox-nav-menu'] [role='menuitem']")
            .Select(item => item.GetAttribute("data-testid")!.Replace("inbox-nav-menu-item-", string.Empty, StringComparison.Ordinal))];

    private static Task CloseMenuAsync(IRenderedComponent<InboxPane> pane) =>
        pane.Find(".context-menu__backdrop").ClickAsync(new());

    private sealed class Harness : IDisposable
    {
        private readonly string _root;

        private Harness(string root, BunitContext context, FakeInboxItems inbox, GitHubSettingsStore gitHub, ToastChannel toasts)
        {
            _root = root;
            Context = context;
            Inbox = inbox;
            GitHub = gitHub;
            Toasts = toasts;
        }

        public BunitContext Context { get; }

        public FakeInboxItems Inbox { get; }

        public GitHubSettingsStore GitHub { get; }

        public ToastChannel Toasts { get; }

        public InboxDesktopState State => Context.Services.GetRequiredService<InboxDesktopState>();

        /// <summary>A harness over the fake module. <paramref name="clock"/>
        /// is what the state reads "now" from; the system clock when null.</summary>
        public static Harness Create(TimeProvider? clock = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-pane-tests", Guid.NewGuid().ToString("n"));
            var gitHub = new GitHubSettingsStore(Path.Combine(root, "github.json"));

            var context = new BunitContext();
            // The split pane, the side menu's focus moves and the menus all
            // reach for JS; none of it is what these tests are about.
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton(gitHub);
            if (clock is not null) context.Services.AddSingleton(clock);
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(root, context, inbox, gitHub, context.Services.GetRequiredService<ToastChannel>());
        }

        /// <summary>Renders the pane the way the shell shows it: initialised, so
        /// the starter organiser is seeded and the snapshot is on screen.</summary>
        public async Task<IRenderedComponent<InboxPane>> RenderAsync(
            Action<ComponentParameterCollectionBuilder<InboxPane>>? parameters = null)
        {
            var pane = parameters is null ? Context.Render<InboxPane>() : Context.Render(parameters);
            await pane.InvokeAsync(State.InitializeAsync);
            return pane;
        }

        public Task SelectAsync(IRenderedComponent<InboxPane> pane, Guid id) =>
            pane.Find($"[data-testid='inbox-item-{id:D}']").ClickAsync(new());

        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

file static class InboxStateTestExtensions
{
    /// <summary>Selects and archives through the state, for a test whose subject
    /// is what the list shows afterwards rather than the archive button.</summary>
    public static async Task ArchiveAfterSelectingAsync(this InboxDesktopState state, IRenderedComponent<InboxPane> pane, Guid id)
    {
        await pane.InvokeAsync(() => state.SelectItem(id));
        await pane.InvokeAsync(state.ArchiveAsync);
    }
}
