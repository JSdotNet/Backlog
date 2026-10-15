using System.Reflection;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// Both hosts name the per-PC choices file on the catalog they hand the pane,
/// whether or not the catalog is there yet.
///
/// <para>The pane's Overview footer names that file. Without it on the catalog
/// the only place it appeared was the status sentence, which says it in two
/// different phrasings depending on whether the file exists.</para>
/// </summary>
public sealed class DevToolPcConfigPathTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-pc-config-path-tests", Guid.NewGuid().ToString("N"));

    private string ToolFolder => Path.Combine(_root, ".tools");

    private string ExpectedPcConfigPath =>
        Path.Combine(ToolFolder, Environment.MachineName.ToLowerInvariant(), "ai-tools.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_desktop_host_names_the_choices_file_when_there_is_no_catalog_yet()
    {
        var service = new DevToolService(configPath: Path.Combine(ToolFolder, "ai-tools.json"));

        var catalog = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.False(catalog.CatalogExists);
        Assert.Equal(ExpectedPcConfigPath, catalog.PcConfigPath, ignoreCase: true);
    }

    [Fact]
    public async Task The_catalog_only_host_names_the_choices_file_when_there_is_no_catalog_yet()
    {
        var catalog = await new CatalogOnlyDevTools(StoreAt(_root)).ListAsync(TestContext.Current.CancellationToken);

        Assert.False(catalog.CatalogExists);
        Assert.Equal(ExpectedPcConfigPath, catalog.PcConfigPath, ignoreCase: true);
    }

    [Fact]
    public async Task The_catalog_only_host_names_the_choices_file_beside_the_catalog_it_read()
    {
        Directory.CreateDirectory(ToolFolder);
        await File.WriteAllTextAsync(
            Path.Combine(ToolFolder, "ai-tools.json"),
            """{ "plugins": [], "mcpServers": [] }""",
            TestContext.Current.CancellationToken);

        var catalog = await new CatalogOnlyDevTools(StoreAt(_root)).ListAsync(TestContext.Current.CancellationToken);

        Assert.True(catalog.CatalogExists);
        Assert.Equal(ExpectedPcConfigPath, catalog.PcConfigPath, ignoreCase: true);
    }

    private static ITaskStore StoreAt(string root)
    {
        var store = DispatchProxy.Create<ITaskStore, RootOnlyStore>();
        ((RootOnlyStore)(object)store).Root = root;
        return store;
    }

    /// <summary>A store that answers only where it lives: that is all the
    /// catalog-only host reads from it.</summary>
    public class RootOnlyStore : DispatchProxy
    {
        public string Root { get; set; } = "";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_" + nameof(ITaskStore.RootDirectory)
                ? Root
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
