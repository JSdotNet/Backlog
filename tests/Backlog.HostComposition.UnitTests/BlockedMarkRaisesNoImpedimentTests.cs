extern alias DesktopHarness;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// A task's blocked mark is a Tasks-only mark, in the desktop harness's own composition
/// (<c>.devbook/domain/progress/requirements.md#requirement-a-tasks-blocked-mark-changes-no-impediment</c>):
/// a <c>set_blocked</c> call and a <c>blocked:</c> token added or removed are task writes
/// that raise, move and change no impediment over real <c>progress_impediments</c> rows.
/// </summary>
public sealed class BlockedMarkRaisesNoImpedimentTests
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
    public async Task Set_blocked_raises_no_impediment()
    {
        using var harness = new Harness();
        using var scope = harness.Services.CreateScope();
        var tracker = ActivatorUtilities.CreateInstance<TrackerTools>(scope.ServiceProvider);
        var impediments = scope.ServiceProvider.GetRequiredService<IImpediments>();

        var entry = await tracker.CreateItemAsync("# Bank import mapping\n`task`\n", cancellationToken: Ct);
        await tracker.SetBlockedAsync(entry.Id, blocked: true, Ct);
        await tracker.CommentAsync(entry.Id, "Waiting on the bank.", Ct);

        Assert.Empty((await impediments.ListAsync(cancellationToken: Ct)).Value);
    }

    [Fact]
    public async Task Removing_the_blocked_token_leaves_the_impediment_that_holds_the_task_back_alone()
    {
        using var harness = new Harness();
        using var scope = harness.Services.CreateScope();
        var tracker = ActivatorUtilities.CreateInstance<TrackerTools>(scope.ServiceProvider);
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskItems>();
        var impediments = scope.ServiceProvider.GetRequiredService<IImpediments>();

        var entry = await tracker.CreateItemAsync("# Bank import mapping\n`task` `blocked:2026-10-16`\n", cancellationToken: Ct);
        var raised = (await impediments.RaiseAsync(
            new RaiseImpedimentRequest("Bank sample files missing", [HeldBackItemDto.ForTask(entry.Id)]), Ct)).Value;
        Assert.True((await impediments.MoveAsync(raised.Id, ImpedimentState.Reported, "bank support", Ct)).IsSuccess);
        var handled = (await impediments.MoveAsync(raised.Id, ImpedimentState.BeingHandled, "bank support", Ct)).Value;

        var stored = Assert.Single(await tasks.ListAsync(Ct), task => task.Id == entry.Id);
        var unmarked = await tasks.SaveFromTextAsync(entry.Id, "# Bank import mapping\n`task`\n", stored.Order, cancellationToken: Ct);
        Assert.True(unmarked.IsSuccess);
        Assert.Null(unmarked.Value.Entry.BlockedSince);

        var still = Assert.Single((await impediments.ListAsync(cancellationToken: Ct)).Value);
        Assert.Equal(handled.Id, still.Id);
        Assert.Equal(ImpedimentState.BeingHandled, still.State);
        Assert.Equal(handled.History, still.History);
        Assert.Equal(handled.HoldsBack, still.HoldsBack);
    }
}
