using Backlog.Mobile.UI.AppUpdate;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The versionCode a release APK carries, packed from its MAJOR.MINOR.PATCH the
/// way release-mobile.yml's "Derive version numbers" step packs it — the phone
/// compares against the installed versionCode, so the two packings must agree to
/// the digit, limits included.
/// </summary>
public sealed class ApkVersionTests
{
    [Theory]
    [InlineData("1.2.3", 10200003L)]
    [InlineData("0.1.42", 100042L)]
    [InlineData("0.0.1", 1L)]
    [InlineData("214.99.99999", 2149999999L)]
    public void A_semantic_version_packs_into_the_workflows_version_code(string display, long expected)
    {
        Assert.Equal(expected, ApkVersion.TryParse(display)?.VersionCode);
    }

    [Theory]
    [InlineData("215.0.0")]
    [InlineData("1.100.0")]
    [InlineData("1.0.100000")]
    [InlineData("0.0.0")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-beta")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("99999999999.0.0")]
    public void A_version_the_workflow_would_refuse_packs_to_nothing(string? display)
    {
        Assert.Null(ApkVersion.TryParse(display));
    }

    [Fact]
    public void The_release_asset_name_carries_the_display_version_and_its_code()
    {
        var version = ApkVersion.TryParseAssetName("Backlog.Mobile_0.1.57.apk");

        Assert.Equal(new ApkVersion("0.1.57", 100057L), version);
    }

    [Theory]
    [InlineData("Backlog.Desktop_0.1.57.msix")]
    [InlineData("Backlog.Mobile_0.1.57.aab")]
    [InlineData("Backlog.Mobile_latest.apk")]
    [InlineData("Backlog.Mobile_0.1.apk")]
    [InlineData("backlog.mobile_0.1.57.apk")]
    [InlineData("Backlog.Mobile_1.100.0.apk")]
    [InlineData("")]
    public void An_asset_that_is_not_a_mobile_release_apk_names_no_version(string name)
    {
        Assert.Null(ApkVersion.TryParseAssetName(name));
    }
}
