using System.Reflection;
using Backlog.Modules.Work.Abstractions.Tasks.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The per-PC folder keyed by Dev PCs' Machine rather than the machine's name (local
/// ADR 0073 §7), and the compatibility read that adopts a folder an older build named
/// after the machine.
/// </summary>
public sealed class DevToolMachineFolderTests : IDisposable
{
    private static readonly Machine Tower = Machine.FromDevice(
        new DeviceIdentity(new Guid("6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71"), "DEV-TOWER"));

    private readonly TemporaryDirectory _temp = new("backlog-machine-folder");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ToolFolder => Path.Combine(_temp.Path, DevToolConfigurationPaths.ToolFolderName);

    private string LegacyFolder => Path.Combine(ToolFolder, "dev-tower");

    private string MachineFolder => Path.Combine(ToolFolder, Tower.Id.ToString());

    [Fact]
    public void The_per_pc_folder_is_the_machines_id_and_the_name_is_the_legacy_folder()
    {
        var paths = DevToolConfigurationPaths.FromStorageRoot(_temp.Path, Tower);

        Assert.Equal(Path.Combine(ToolFolder, DevToolConfigurationPaths.CatalogFileName), paths.CatalogPath);
        Assert.Equal(Path.Combine(MachineFolder, DevToolConfigurationPaths.CatalogFileName), paths.PcConfigPath);
        Assert.Equal(Path.Combine(MachineFolder, DevToolConfigurationPaths.LastRunFileName), paths.LastRunPath);
        Assert.Equal(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName), paths.LegacyPcConfigPath);
    }

    [Fact]
    public void A_renamed_machine_keeps_its_folder()
    {
        var renamed = Tower with { Name = "DEV-TOWER-2" };

        Assert.Equal(
            DevToolConfigurationPaths.FromStorageRoot(_temp.Path, Tower).PcConfigPath,
            DevToolConfigurationPaths.FromStorageRoot(_temp.Path, renamed).PcConfigPath);
    }

    [Fact]
    public void The_catalog_path_form_keys_the_folder_by_the_machine_too()
    {
        var catalog = Path.Combine(ToolFolder, DevToolConfigurationPaths.CatalogFileName);

        var paths = DevToolConfigurationPaths.FromCatalogPath(catalog, Tower);

        Assert.Equal(Path.Combine(MachineFolder, DevToolConfigurationPaths.CatalogFileName), paths.PcConfigPath);
    }

    [Fact]
    public async Task A_folder_named_after_the_machine_is_adopted_under_its_id_and_kept()
    {
        Directory.CreateDirectory(LegacyFolder);
        await File.WriteAllTextAsync(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName), """{"plugins":[]}""", Ct);
        await File.WriteAllTextAsync(Path.Combine(LegacyFolder, DevToolConfigurationPaths.LastRunFileName), "{}", Ct);

        var paths = DevToolConfigurationPaths.FromStorageRoot(_temp.Path, Tower);

        Assert.Same(paths, DevToolConfiguration.AdoptLegacyPcFolder(paths));

        Assert.Equal("""{"plugins":[]}""", await File.ReadAllTextAsync(paths.PcConfigPath, Ct));
        Assert.True(File.Exists(paths.LastRunPath));

        // Copied, not moved: an older build, or another machine of this name, still
        // reads the name's folder.
        Assert.True(File.Exists(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName)));
        Assert.Empty(Directory.EnumerateDirectories(ToolFolder, "*.adopting-*"));
    }

    [Fact]
    public async Task A_folder_already_under_the_id_is_never_overwritten()
    {
        Directory.CreateDirectory(LegacyFolder);
        await File.WriteAllTextAsync(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName), "legacy", Ct);
        Directory.CreateDirectory(MachineFolder);
        await File.WriteAllTextAsync(Path.Combine(MachineFolder, DevToolConfigurationPaths.CatalogFileName), "current", Ct);

        var paths = DevToolConfigurationPaths.FromStorageRoot(_temp.Path, Tower);

        Assert.Same(paths, DevToolConfiguration.AdoptLegacyPcFolder(paths));
        Assert.Equal("current", await File.ReadAllTextAsync(paths.PcConfigPath, Ct));
    }

    [Fact]
    public void A_machine_with_no_legacy_folder_adopts_nothing()
    {
        var paths = DevToolConfigurationPaths.FromStorageRoot(_temp.Path, Tower);

        Assert.Same(paths, DevToolConfiguration.AdoptLegacyPcFolder(paths));
        Assert.False(Directory.Exists(MachineFolder));
    }

    [Fact]
    public async Task The_catalog_only_adapter_reads_the_adopted_choices()
    {
        Directory.CreateDirectory(LegacyFolder);
        await File.WriteAllTextAsync(Path.Combine(ToolFolder, DevToolConfigurationPaths.CatalogFileName), """{"plugins":[],"mcpServers":[]}""", Ct);
        await File.WriteAllTextAsync(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName), """{"plugins":[]}""", Ct);

        var store = DispatchProxy.Create<ITaskStore, DevToolPcConfigPathTests.RootOnlyStore>();
        ((DevToolPcConfigPathTests.RootOnlyStore)(object)store).Root = _temp.Path;

        var catalog = await DevToolService.CatalogOnly(store, Tower).ListAsync(Ct);

        Assert.Equal(Path.Combine(MachineFolder, DevToolConfigurationPaths.CatalogFileName), catalog.PcConfigPath);
        Assert.True(File.Exists(catalog.PcConfigPath));
    }

    [Fact]
    public async Task The_desktop_adapter_names_the_machines_folder_and_adopts_the_legacy_one()
    {
        Directory.CreateDirectory(LegacyFolder);
        await File.WriteAllTextAsync(Path.Combine(LegacyFolder, DevToolConfigurationPaths.CatalogFileName), """{"plugins":[]}""", Ct);

        var service = new DevToolService(configPath: Path.Combine(ToolFolder, DevToolConfigurationPaths.CatalogFileName)) { Machine = Tower };

        var catalog = await service.ListAsync(Ct);

        Assert.Equal(Path.Combine(MachineFolder, DevToolConfigurationPaths.CatalogFileName), catalog.PcConfigPath);
        Assert.True(File.Exists(catalog.PcConfigPath));
    }
}
