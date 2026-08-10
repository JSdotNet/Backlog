using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public class CopilotToolTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("unknown", "1.2.3", false)]
    [InlineData("not installed", "1.2.3", false)]
    public void Update_available_only_compares_known_versions(string installed, string available, bool expected)
    {
        var tool = new CopilotToolInfo(
            "plugin:test",
            CopilotToolKind.Plugin,
            "test",
            "https://github.com/example/test",
            ConfiguredEnabled: true,
            Installed: true,
            installed,
            available,
            "Enabled plugin");

        Assert.Equal(expected, tool.UpdateAvailable);
    }
}

public class UnsupportedCopilotToolServiceTests
{
    [Fact]
    public async Task Listing_returns_a_clear_unsupported_message()
    {
        var service = new UnsupportedCopilotToolService();

        var catalog = await service.ListAsync();

        Assert.Empty(catalog.Tools);
        Assert.Contains("desktop app", catalog.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CopilotToolAction.Update)]
    [InlineData(CopilotToolAction.Enable)]
    [InlineData(CopilotToolAction.Disable)]
    public async Task Actions_fail_without_throwing(CopilotToolAction action)
    {
        var service = new UnsupportedCopilotToolService();

        var result = action switch
        {
            CopilotToolAction.Update => await service.UpdateAsync("plugin:test"),
            CopilotToolAction.Enable => await service.EnableAsync("plugin:test"),
            CopilotToolAction.Disable => await service.DisableAsync("plugin:test"),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}

