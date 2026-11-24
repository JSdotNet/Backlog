extern alias DesktopHarness;

using Backlog.Infrastructure.FileSystem;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// That a disposed harness has let go of the databases it wrote, which proves
/// <see cref="HarnessDatabases"/> asks for each pool by the key the host opened it under.
/// A path built any other way clears nothing, and the pooled handle that is left keeps
/// the file from being deleted until the process exits.
/// </summary>
public class HarnessDatabasesTests
{
    private sealed class Harness : IsolatedHarnessFactory<DesktopHarness::Program>;

    [Fact]
    public async Task A_disposed_harness_leaves_every_database_it_wrote_deletable()
    {
        string root;
        var harness = new Harness();
        try
        {
            root = harness.Services.GetRequiredService<WorkspaceSettingsStore>().RootDirectory;
        }
        finally
        {
            await harness.DisposeAsync();
        }

        var databases = Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(databases);

        foreach (var database in databases)
        {
            var thrown = await DeleteOnceReturnedAsync(database);
            Assert.True(thrown is null, $"{database} is still held {LetGo.TotalSeconds:0} s after the harness was disposed: {thrown?.Message}");
        }
    }

    /// <summary>How long a connection still in use when the harness went may take to come back.</summary>
    private static readonly TimeSpan LetGo = TimeSpan.FromSeconds(10);

    /// <summary>Clearing a pool closes its idle connections at once, but one that work the
    /// host started was still using closes only when that work hands it back, a moment after
    /// the host is gone on a busy runner. A pool cleared under any other key never closes it:
    /// that is what still fails here, after <see cref="LetGo"/>.</summary>
    private static async Task<Exception?> DeleteOnceReturnedAsync(string database)
    {
        var giveUp = DateTime.UtcNow + LetGo;
        while (true)
        {
            var thrown = Record.Exception(() => File.Delete(database));
            if (thrown is not IOException || DateTime.UtcNow >= giveUp) return thrown;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
