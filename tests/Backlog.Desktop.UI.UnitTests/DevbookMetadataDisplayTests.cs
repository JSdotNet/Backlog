namespace Backlog.Desktop.UI.UnitTests;

public class DevbookMetadataDisplayTests
{
    [Theory]
    [InlineData("link")]
    [InlineData("related")]
    [InlineData(" Related ")]
    public void Hides_redundant_labels_for_visible_links(string label)
    {
        Assert.False(DevbookMetadataDisplay.ShouldShowLabel(label));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("depends-on")]
    [InlineData("owner")]
    public void Keeps_labels_that_add_meaning(string label)
    {
        Assert.True(DevbookMetadataDisplay.ShouldShowLabel(label));
    }
}
