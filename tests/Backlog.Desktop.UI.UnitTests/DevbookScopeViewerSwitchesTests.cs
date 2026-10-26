using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Whether the repository chapter viewer offers Edit and shows status is asked of
/// Devbook's scope, which answers from the two feature switches — and a host that
/// composed no scope gets the viewer as it always was.
/// </summary>
public sealed class DevbookScopeViewerSwitchesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devbook-scope-viewer-switches-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edit_is_offered_only_while_devbook_editing_is_on(bool enabled)
    {
        var (scope, features) = Scope();
        features.SetEnabled(DevbookFeatures.Editing, enabled);

        Assert.Equal(enabled, scope.EditOffered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_is_shown_only_while_devbook_status_is_on(bool enabled)
    {
        var (scope, features) = Scope();
        features.SetEnabled(DevbookFeatures.Status, enabled);

        Assert.Equal(enabled, scope.StatusShown);
    }

    [Fact]
    public void A_host_without_a_scope_offers_edit_and_shows_status()
    {
        Assert.True(DevbookScope.EditOfferedBy(null));
        Assert.True(DevbookScope.StatusShownBy(null));
    }

    [Fact]
    public void A_host_with_a_scope_answers_from_its_switches()
    {
        var (scope, _) = Scope();

        Assert.False(DevbookScope.EditOfferedBy(scope));
        Assert.False(DevbookScope.StatusShownBy(scope));
    }

    [Fact]
    public void Switching_either_flag_is_announced_through_the_scope()
    {
        var (scope, features) = Scope();
        var raised = 0;
        scope.SwitchesChanged += () => raised++;

        features.SetEnabled(DevbookFeatures.Editing, true);
        features.SetEnabled(DevbookFeatures.Status, true);

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task A_watch_hands_each_change_to_the_dispatcher_until_it_is_disposed()
    {
        var (scope, features) = Scope();
        var calls = 0;
        var watch = DevbookScope.Watch(scope, work => work(), () =>
        {
            calls++;
            return Task.CompletedTask;
        });

        features.SetEnabled(DevbookFeatures.Status, true);
        Assert.True(watch.StatusShown);
        Assert.False(watch.EditOffered);

        watch.Dispose();
        features.SetEnabled(DevbookFeatures.Editing, true);
        await Task.Yield();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_watch_with_no_scope_offers_edit_and_shows_status()
    {
        using var watch = DevbookScope.Watch(null, work => work(), () => Task.CompletedTask);

        Assert.True(watch.EditOffered);
        Assert.True(watch.StatusShown);
    }

    private (DevbookScope Scope, AppFeatureSettingsStore Features) Scope()
    {
        var gitHub = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(_root, "features", "features.json"));
        var folders = new DevbookFolderSource(gitHub, new WorkspaceSettingsStore(Path.Combine(_root, "store")));

        return (new DevbookScope(gitHub, folders, features), features);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
