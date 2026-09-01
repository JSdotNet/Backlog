namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Version formatting is what the "About" section shows; the informational
/// version carries a source-revision suffix the SDK appends that a person should
/// never see.
/// </summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.0.0-preview.2+deadbeef", "1.0.0-preview.2")]
    [InlineData("  1.4.0+meta  ", "1.4.0")]
    public void Normalize_drops_the_source_revision_suffix(string input, string expected)
    {
        Assert.Equal(expected, AppVersion.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("+onlymetadata")]
    public void Normalize_treats_blank_or_metadata_only_as_no_version(string? input)
    {
        Assert.Null(AppVersion.Normalize(input));
    }

    [Fact]
    public void Of_an_assembly_never_returns_blank()
    {
        var version = AppVersion.Of(typeof(AppVersion).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}
