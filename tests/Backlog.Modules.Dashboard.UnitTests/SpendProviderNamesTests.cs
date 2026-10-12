using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The one place a spend provider's name on screen is decided (AC27). Display words only:
/// the enum members, which settings keys, slugs and test ids are built from, stay as
/// they are.
/// </summary>
public sealed class SpendProviderNamesTests
{
    [Theory]
    [InlineData(SpendProvider.Claude, "Anthropic")]
    [InlineData(SpendProvider.Copilot, "Copilot")]
    [InlineData(SpendProvider.AzureFoundry, "Azure AI Foundry")]
    public void Each_provider_is_named_as_the_board_names_it(SpendProvider provider, string name)
    {
        Assert.Equal(name, provider.DisplayName());
    }

    [Fact]
    public void The_persisted_member_names_are_unchanged()
    {
        Assert.Equal(["Claude", "Copilot", "AzureFoundry"], Enum.GetNames<SpendProvider>());
    }
}
