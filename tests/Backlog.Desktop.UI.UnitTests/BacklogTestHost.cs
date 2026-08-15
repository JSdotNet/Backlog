using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Composes the Backlog module the way a host does, so these tests drive the
/// desktop through the real use cases rather than a stand-in for them.
/// <para>
/// The desktop project itself can no longer see the module implementation — it
/// only knows <see cref="IBacklogEntries"/> — but a test is a host, and wiring
/// the handlers to a real file store is exactly what makes these tests worth
/// having.
/// </para>
/// </summary>
internal static class BacklogTestHost
{
    public static IBacklogRepository RepositoryFor(BacklogStore store) =>
        new RootedFileBacklogRepository(() => store.RootDirectory);

    public static IBacklogEntries EntriesFor(BacklogStore store) =>
        EntriesFor(RepositoryFor(store));

    public static IBacklogEntries EntriesFor(IBacklogRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddBacklogModule()
            .BuildServiceProvider()
            .GetRequiredService<IBacklogEntries>();

    public static BacklogDesktopState StateFor(
        BacklogStore store,
        GitHubIntegration gitHub,
        BacklogCopilotCli? copilot = null,
        RepositoryBacklogSource? repositoryBacklog = null) =>
        new(store, EntriesFor(store), gitHub, copilot, repositoryBacklog);
}
