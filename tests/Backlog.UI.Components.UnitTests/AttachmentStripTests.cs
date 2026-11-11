using Microsoft.JSInterop;

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

    [Fact]
    public void A_recording_on_the_device_plays_on_its_tile_from_a_blob_it_releases_when_it_goes()
    {
        using var context = new BunitContext();
        var opened = context.JSInterop.Setup<string?>("backlogAudio.open", _ => true).SetResult("blob:recording");
        context.JSInterop.SetupVoid("backlogAudio.release", "blob:recording");
        var recording = Recording(new AudioSource("audio/aac", _ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]))));

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [recording]));

        var tile = strip.Find("[data-testid='attachment-tile']");
        Assert.NotNull(tile.QuerySelector("[data-glyph='audio']"));
        Assert.Equal("AAC audio · 18.4 MB", tile.QuerySelector("[data-testid='attachment-tile-facts']")!.TextContent);
        strip.WaitForAssertion(() =>
            Assert.Equal("blob:recording", strip.Find("[data-testid='attachment-tile-player']").GetAttribute("src")));
        var player = strip.Find("[data-testid='attachment-tile-player']");
        Assert.Equal("audio", player.TagName, ignoreCase: true);
        Assert.Equal("Play Recording · 47:12", player.GetAttribute("aria-label"));
        Assert.True(player.HasAttribute("controls"));
        Assert.Equal("audio/aac", opened.Invocations.Single().Arguments[1]);

        strip.Render(parameters => parameters.Add(s => s.Tiles, []));

        context.JSInterop.VerifyInvoke("backlogAudio.release");
    }

    [Fact]
    public void A_recording_whose_bytes_are_not_on_the_device_is_described_with_no_player()
    {
        using var context = new BunitContext();

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [Recording(null)]));

        var tile = strip.Find("[data-testid='attachment-tile']");
        Assert.NotNull(tile.QuerySelector("[data-glyph='audio']"));
        Assert.Equal("Recording · 47:12", tile.QuerySelector("[data-testid='attachment-tile-name']")!.TextContent);
        Assert.Empty(strip.FindAll("[data-testid='attachment-tile-player']"));
    }

    [Fact]
    public void A_recording_gone_since_the_tile_was_drawn_says_so_in_words()
    {
        using var context = new BunitContext();
        var gone = Recording(new AudioSource("audio/aac", _ => Task.FromResult<Stream?>(null)));

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [gone]));

        strip.WaitForAssertion(() =>
            Assert.Equal("This recording is not on this device any more.", strip.Find("[data-testid='attachment-tile-player-missing']").TextContent));
        Assert.Empty(strip.FindAll("[data-testid='attachment-tile-player']"));
    }

    [Fact]
    public void A_recording_the_browser_could_not_load_says_so_in_words_in_the_players_place()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<string?>("backlogAudio.open", _ => true).SetException(new JSException("decode failed"));
        var recording = Recording(new AudioSource("audio/aac", _ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]))));

        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [recording]));

        var notice = strip.WaitForElement("[data-testid='attachment-tile-player-missing']");
        Assert.Equal("This recording could not be loaded.", notice.TextContent);
        Assert.Contains("attachment-tile__player", notice.ClassList);
        Assert.Empty(strip.FindAll("[data-testid='attachment-tile-player']"));
    }

    [Fact]
    public void A_blob_that_arrives_after_its_tile_went_is_released()
    {
        using var context = new BunitContext();
        var opened = context.JSInterop.Setup<string?>("backlogAudio.open", _ => true);
        context.JSInterop.SetupVoid("backlogAudio.release", "blob:late");
        var recording = Recording(new AudioSource("audio/aac", _ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]))));
        var strip = context.Render<AttachmentStrip>(parameters => parameters.Add(s => s.Tiles, [recording]));
        strip.WaitForAssertion(() => Assert.Single(opened.Invocations));

        strip.Render(parameters => parameters.Add(s => s.Tiles, []));
        opened.SetResult("blob:late");

        strip.WaitForAssertion(() => context.JSInterop.VerifyInvoke("backlogAudio.release"));
    }

    private static AttachmentTile Recording(AudioSource? audio) =>
        new("recording", "Recording · 47:12", "AAC audio", "18.4 MB", IsAudio: true, Audio: audio);
}
