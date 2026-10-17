extern alias DesktopHarness;

using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The blocked-task rule end to end in the desktop harness's own composition
/// (<c>.devbook/domain/progress/requirements.md#requirement-a-blocked-task-raises-one-new-impediment</c>):
/// a <c>set_blocked</c> call and a <c>blocked:</c> token are both task writes through Tasks,
/// the watcher the head resolves at startup hears them, and Progress raises a New
/// impediment over real <c>progress_impediments</c> rows — Tasks never calling Progress.
/// </summary>
public sealed class BlockedTaskRaisesImpedimentTests
{
    private sealed class Harness : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.AddSingleton<IAppFeatureSettings>(new AllFeaturesOn()));
    }

    private sealed class AllFeaturesOn : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Set_blocked_raises_one_New_impediment_and_unblocking_leaves_it()
    {
        using var harness = new Harness();
        var watcher = harness.Services.GetRequiredService<BlockedTaskWatcher>();
        using var scope = harness.Services.CreateScope();
        var tracker = ActivatorUtilities.CreateInstance<TrackerTools>(scope.ServiceProvider);
        var impediments = scope.ServiceProvider.GetRequiredService<IImpediments>();

        var entry = await tracker.CreateItemAsync("# Bank import mapping\n`task`\n", cancellationToken: Ct);
        await tracker.SetBlockedAsync(entry.Id, blocked: true, Ct);
        await watcher.Idle;

        var raised = Assert.Single((await impediments.ListAsync(cancellationToken: Ct)).Value);
        Assert.Equal("Bank import mapping", raised.Title);
        Assert.Equal(ImpedimentState.New, raised.State);
        Assert.Equal([entry.Id], raised.HoldsBack);

        // Marking it again, or any other write, raises nothing more; unblocking resolves nothing.
        await tracker.CommentAsync(entry.Id, "Waiting on the bank.", Ct);
        await tracker.SetBlockedAsync(entry.Id, blocked: false, Ct);
        await watcher.Idle;

        var still = Assert.Single((await impediments.ListAsync(cancellationToken: Ct)).Value);
        Assert.Equal(raised.Id, still.Id);
        Assert.Equal(ImpedimentState.New, still.State);
    }

    [Fact]
    public async Task A_blocked_token_in_the_entry_text_raises_one_too()
    {
        using var harness = new Harness();
        var watcher = harness.Services.GetRequiredService<BlockedTaskWatcher>();
        using var scope = harness.Services.CreateScope();
        var tracker = ActivatorUtilities.CreateInstance<TrackerTools>(scope.ServiceProvider);
        var impediments = scope.ServiceProvider.GetRequiredService<IImpediments>();

        var entry = await tracker.CreateItemAsync("# Credit note numbering\n`task` `blocked:2026-10-16`\n", cancellationToken: Ct);
        await watcher.Idle;

        var raised = Assert.Single((await impediments.ListAsync(cancellationToken: Ct)).Value);
        Assert.Equal("Credit note numbering", raised.Title);
        Assert.Equal([entry.Id], raised.HoldsBack);
    }
}
