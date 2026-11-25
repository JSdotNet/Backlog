using Backlog.Desktop.Composition;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Tests;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The settings files an agent checkpoint copies are asked of the stores the head composed,
/// so a head that keeps them somewhere else - the web harness under its content root - is
/// copied where they are, and one it does not compose names nothing (local ADR 0061 §8). The
/// MCP grants are the desktop head's to name (local ADR 0058 §9), handed in beside them.
/// </summary>
public sealed class CheckpointedSettingsFilesTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-checkpointed-settings-tests");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Each_composed_store_names_the_file_it_keeps()
    {
        var github = new GitHubSettingsStore(_temp.Combine("github.json"), () => _temp.Path);
        var pace = new PlanningVelocitySettingsStore(_temp.Combine("planning-velocity.json"));
        var myDay = new MyDayPreferencesStore(_temp.Combine(MyDayPreferencesStore.FileName));
        var iteration = new IterationSettingsStore(_temp.Combine(IterationSettingsStore.FileName));
        var grants = _temp.Combine(FileMcpGrantStore.FileName);

        using var services = new ServiceCollection()
            .AddSingleton(github)
            .AddSingleton(pace)
            .AddSingleton(myDay)
            .AddSingleton(iteration)
            .BuildServiceProvider();

        Assert.Equal(
            [github.SettingsPath, pace.SettingsPath, myDay.SettingsPath, iteration.SettingsPath, grants],
            DesktopCompositionRegistration.CheckpointedSettingsFiles(services, [grants]));
    }

    [Fact]
    public void A_head_that_composes_none_of_the_stores_names_no_file()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var named = DesktopCompositionRegistration.CheckpointedSettingsFiles(services, []);

        Assert.Equal(4, named.Count);
        Assert.All(named, Assert.Null);
    }
}
