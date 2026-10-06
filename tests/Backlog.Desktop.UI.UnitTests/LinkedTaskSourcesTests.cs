using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.UI.Components.Tasks;
using static Backlog.Desktop.UI.UnitTests.LinkedTaskPaneTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one place a <see cref="SourceRef"/> becomes what a screen draws, and where the
/// settings page learns what a connector can do.
/// </summary>
public sealed class LinkedTaskSourcesTests
{
    private static readonly SourceRef Reference =
        new(FakeConnector.Id, "owner/repo", "I_1", "https://example.com/1", "#1", null, "open", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_refusal_to_complete_at_the_source_is_drawn_with_its_reason()
    {
        var sources = new LinkedTaskSources([new FakeConnector()]);

        var source = sources.ToTaskSource(Reference.WithFlag(LinkedTaskFlags.DoneLocally, set: true) with { WriteBackRefusal = "GitHub refused (403)" })!;

        Assert.Equal("GitHub refused (403)", source.WriteBackRefusal);
        Assert.Contains(source.Flags, flag => flag.Kind == TaskDetailKind.SourceWriteBackRefused && flag.Text.EndsWith("GitHub refused (403)", StringComparison.Ordinal));
        Assert.Null(sources.ToTaskSource(Reference)!.WriteBackRefusal);
    }

    [Fact]
    public void Only_an_installed_connector_that_says_so_can_complete()
    {
        var sources = new LinkedTaskSources([new FakeConnector(), new CompletingConnector()]);

        Assert.True(sources.CanComplete(CompletingConnector.Id));
        Assert.False(sources.CanComplete(FakeConnector.Id));
        Assert.False(sources.CanComplete("jira"));
        Assert.False(sources.CanComplete(null));
    }

    /// <summary>A connector that can finish an item at its source.</summary>
    internal sealed class CompletingConnector : ITaskConnector
    {
        public const string Id = "completing";

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Completing", "completing", "color-primary-light");

        public TaskConnectorCapabilities Capabilities { get; } = new(CanComplete: true);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
