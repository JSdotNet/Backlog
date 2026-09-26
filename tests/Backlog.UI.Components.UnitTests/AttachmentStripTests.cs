namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about the attachment strip: a tile per file with its
/// name, type and size; a refusal drawn in words on its own tile; and a remove
/// control only when the host can take a file back.
/// </summary>
public sealed class AttachmentStripTests
{
    private static readonly AttachmentTile Photo = new("photo", "IMG_0042.jpg", "JPEG image", "412 KB", IsPicture: true);

    private static readonly AttachmentTile Refused = new("exe", "setup.exe", "application/x-msdownload", "2 MB",
        Refusal: "Not a type cloud sync takes (.exe).");

    [Fact]
    public void Each_tile_shows_its_name_type_and_size()
    {
        using var context = new BunitContext();

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [Photo]));

        var tile = strip.Find("[data-testid='attachment-tile']");
        Assert.Equal("IMG_0042.jpg", tile.QuerySelector("[data-testid='attachment-tile-name']")!.TextContent);
        Assert.Equal("JPEG image · 412 KB", tile.QuerySelector("[data-testid='attachment-tile-facts']")!.TextContent);
        Assert.Equal("false", tile.GetAttribute("data-refused"));
    }

    [Fact]
    public void A_refused_tile_says_why_in_words()
    {
        using var context = new BunitContext();

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [Photo, Refused]));

        var tiles = strip.FindAll("[data-testid='attachment-tile']");
        Assert.Equal("true", tiles[1].GetAttribute("data-refused"));
        Assert.Contains("attachment-tile--refused", tiles[1].ClassList);
        Assert.Equal("Not a type cloud sync takes (.exe).", tiles[1].QuerySelector("[data-testid='attachment-tile-refusal']")!.TextContent);
        Assert.Null(tiles[0].QuerySelector("[data-testid='attachment-tile-refusal']"));
    }

    [Fact]
    public void The_remove_control_is_named_for_its_file_and_hands_the_tile_back()
    {
        using var context = new BunitContext();
        AttachmentTile? removed = null;

        var strip = context.Render<AttachmentStrip>(parameters => parameters
            .Add(s => s.Tiles, [Photo])
            .Add(s => s.OnRemove, tile => removed = tile));

        var remove = strip.Find("[data-testid='attachment-tile-remove']");
        Assert.Equal("Remove IMG_0042.jpg", remove.GetAttribute("aria-label"));

        remove.Click();

        Assert.Equal(Photo, removed);
    }

    [Fact]
    public void Without_a_remove_handler_the_strip_only_shows_and_empty_it_draws_nothing()
    {
        using var context = new BunitContext();

        var readOnly = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [Photo]));
        Assert.Empty(readOnly.FindAll("[data-testid='attachment-tile-remove']"));

        var empty = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, []));
        Assert.Empty(empty.FindAll("ul"));
    }
}
