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
            var thrown = Record.Exception(() => File.Delete(database));
            Assert.True(thrown is null, $"{database} is still held after the harness was disposed: {thrown?.Message}");
        }
    }
}
