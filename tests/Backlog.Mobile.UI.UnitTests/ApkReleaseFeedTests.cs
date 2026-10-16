using Backlog.Mobile.UI.AppUpdate;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Picking the APK to offer out of GitHub's "list releases" answer. The newest
/// release is not <c>/releases/latest</c>: that points at the desktop's MSIX-only
/// release, and the nightly mobile builds are published with <c>--latest=false</c>.
/// So every release is read and the highest-versioned mobile APK wins, whichever
/// tag namespace it was published under.
/// </summary>
public sealed class ApkReleaseFeedTests
{
    private static string Release(string tag, bool draft = false, bool prerelease = false, string[]? assets = null)
    {
        var assetJson = string.Join(",", (assets ?? []).Select(name =>
            $$"""{"name":"{{name}}","state":"uploaded","size":31457280,"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/{{tag}}/{{name}}"}"""));

        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{assetJson}}]}""";
    }

    private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private static ApkRelease? Newest(string json) => ApkReleaseFeed.TryRead(json, out var newest) ? newest : null;

    [Fact]
    public void The_highest_versioned_apk_wins_across_v_and_mobile_v_tags()
    {
        var json = Feed(
            Release("v0.1.116", assets: ["Backlog.Desktop_0.1.116.msix"]),
            Release("mobile-v0.1.57", assets: ["Backlog.Mobile_0.1.57.apk"]),
            Release("v1.0.0", assets: ["Backlog.Desktop_1.0.0.msix", "Backlog.Mobile_1.0.0.apk"]),
            Release("mobile-v0.1.58", assets: ["Backlog.Mobile_0.1.58.apk"]));

        var newest = Newest(json);

        Assert.NotNull(newest);
        Assert.Equal(new ApkVersion("1.0.0", 10000000L), newest.Version);
        Assert.Equal("v1.0.0", newest.Tag);
        Assert.Equal(
            new Uri("https://github.com/JSdotNet/Backlog/releases/download/v1.0.0/Backlog.Mobile_1.0.0.apk"),
            newest.DownloadUrl);
        Assert.Equal(31457280L, newest.Size);
    }

    [Fact]
    public void A_nightly_counts_even_though_it_is_not_marked_latest()
    {
        var json = Feed(
            Release("mobile-v0.1.60", prerelease: true, assets: ["Backlog.Mobile_0.1.60.apk"]),
            Release("mobile-v0.1.59", assets: ["Backlog.Mobile_0.1.59.apk"]));

        Assert.Equal("0.1.60", Newest(json)?.Version.Display);
    }

    [Fact]
    public void A_draft_is_never_offered()
    {
        var json = Feed(
            Release("mobile-v0.1.61", draft: true, assets: ["Backlog.Mobile_0.1.61.apk"]),
            Release("mobile-v0.1.59", assets: ["Backlog.Mobile_0.1.59.apk"]));

        Assert.Equal("0.1.59", Newest(json)?.Version.Display);
    }

    [Fact]
    public void Releases_without_a_mobile_apk_offer_nothing()
    {
        var json = Feed(
            Release("v0.1.116", assets: ["Backlog.Desktop_0.1.116.msix"]),
            Release("v0.1.115", assets: ["Backlog.Mobile_latest.apk", "Backlog.Mobile_0.1.115.aab"]));

        Assert.Null(Newest(json));
    }

    [Fact]
    public void An_asset_still_uploading_is_passed_over()
    {
        const string json = """
            [
              {"tag_name":"mobile-v0.1.62","draft":false,"assets":[
                {"name":"Backlog.Mobile_0.1.62.apk","state":"starter","size":31457280,"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.62/Backlog.Mobile_0.1.62.apk"}]},
              {"tag_name":"mobile-v0.1.59","draft":false,"assets":[
                {"name":"Backlog.Mobile_0.1.59.apk","state":"uploaded","size":31457280,"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.59/Backlog.Mobile_0.1.59.apk"}]}
            ]
            """;

        Assert.Equal("0.1.59", Newest(json)?.Version.Display);
    }

    [Theory]
    [InlineData("http://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.63/Backlog.Mobile_0.1.63.apk")]
    [InlineData("https://example.com/Backlog.Mobile_0.1.63.apk")]
    [InlineData("not a url")]
    public void A_download_that_is_not_https_from_github_is_never_offered(string url)
    {
        var json = $$"""
            [{"tag_name":"mobile-v0.1.63","draft":false,"assets":[
              {"name":"Backlog.Mobile_0.1.63.apk","state":"uploaded","size":31457280,"browser_download_url":"{{url}}"}]}]
            """;

        Assert.Null(Newest(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]
    [InlineData("null")]
    public void A_malformed_answer_says_it_was_unreadable(string json)
    {
        Assert.False(ApkReleaseFeed.TryRead(json, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(""","size":0""")]
    [InlineData(""","size":"big" """)]
    public void An_asset_that_does_not_say_how_big_it_is_is_never_offered(string size)
    {
        var json = $$"""
            [{"tag_name":"mobile-v0.1.63","draft":false,"assets":[
              {"name":"Backlog.Mobile_0.1.63.apk","state":"uploaded"{{size}},"browser_download_url":"https://github.com/JSdotNet/Backlog/releases/download/mobile-v0.1.63/Backlog.Mobile_0.1.63.apk"}]}]
            """;

        Assert.Null(Newest(json));
    }

    [Fact]
    public void An_empty_list_is_readable_and_offers_nothing()
    {
        Assert.True(ApkReleaseFeed.TryRead("[]", out var newest));
        Assert.Null(newest);
    }
}
