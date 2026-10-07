using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// What a connector offers on the settings page to pick from. A connector that says
/// nothing more than the targets it lists offers each under its own spelling, so
/// every connector shipped before choices existed is picked from unchanged.
/// </summary>
public sealed class ConnectorTargetChoicesTests
{
    [Fact]
    public async Task A_connector_that_only_lists_targets_offers_each_named_as_itself()
    {
        ITaskConnector connector = new ListingConnector("owner/one", "owner/two");

        var choices = await connector.ListTargetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new ConnectorTargetChoice("owner/one", "owner/one"), new ConnectorTargetChoice("owner/two", "owner/two")],
            choices.Choices);
        Assert.Null(choices.CannotList);
    }

    [Fact]
    public async Task A_connector_that_lists_nothing_offers_nothing_and_says_nothing()
    {
        ITaskConnector connector = new StubTaskConnector();

        var choices = await connector.ListTargetChoicesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(choices.Choices);
        Assert.Null(choices.CannotList);
    }

    private sealed class ListingConnector(params string[] targets) : ITaskConnector
    {
        public TaskConnectorDescriptor Descriptor { get; } = new("listing", "Listing", "listing", "--color-stub");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public Task<IReadOnlyList<string>> ListTargetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(targets);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);
    }
}
