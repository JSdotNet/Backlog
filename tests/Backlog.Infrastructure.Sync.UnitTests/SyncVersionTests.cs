namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// When the sync service counts as newer than this app. The versions on either
/// side come from different places — an MSIX package's four parts, an APK's
/// three, a development build's <c>1.0</c> — so a part that is not there counts as
/// zero, and anything after a <c>-</c> or <c>+</c> is a label rather than a number.
/// </summary>
public sealed class SyncVersionTests
{
    [Theory]
    [InlineData("0.2.0", "0.1.122")]
    [InlineData("0.1.123", "0.1.122.0")]
    [InlineData("1.0.1", "1.0")]
    [InlineData("0.1.0.1", "0.1.0")]
    [InlineData("2.0.0-preview.1", "1.9.9")]
    public void A_higher_service_version_is_newer(string service, string app) =>
        Assert.True(SyncVersion.IsServiceNewer(service, app));

    [Theory]
    [InlineData("0.1.0", "0.1.122.0")]
    [InlineData("0.1.0", "0.1.63")]
    [InlineData("0.1.0", "1.0")]
    [InlineData("1.0.0", "1.0")]
    [InlineData("1.0", "1.0.0.0")]
    [InlineData("1.0.0+8315afc", "1.0.0")]
    [InlineData("1.0.0-beta", "1.0.0")]
    public void An_equal_or_lower_service_version_is_not_newer(string service, string app) =>
        Assert.False(SyncVersion.IsServiceNewer(service, app));

    /// <summary>A version either side cannot read says nothing about which is
    /// newer, and stopping sync on a guess would strand a person over a typo.</summary>
    [Theory]
    [InlineData(null, "0.1.0")]
    [InlineData("", "0.1.0")]
    [InlineData("unknown", "0.1.0")]
    [InlineData("9.0.0", null)]
    [InlineData("9.0.0", "unknown")]
    [InlineData("9.x.0", "0.1.0")]
    [InlineData("9.0.0", "1.2.3.4.5")]
    public void An_unreadable_version_on_either_side_is_not_newer(string? service, string? app) =>
        Assert.False(SyncVersion.IsServiceNewer(service, app));
}
