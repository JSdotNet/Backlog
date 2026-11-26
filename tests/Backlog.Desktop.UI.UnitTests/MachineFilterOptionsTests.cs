using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Insights.UI;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Insights machine filter's options over Dev PCs' machines (local ADR 0073 §7):
/// valued by id, labelled by name, and told apart where a name is shared.
/// </summary>
public sealed class MachineFilterOptionsTests
{
    private static readonly Machine Tower = Of("6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71", "DEV-TOWER");

    [Fact]
    public void An_option_is_valued_by_the_machines_id_and_labelled_by_its_name()
    {
        var option = Assert.Single(MachineFilterOptions.From([Tower]));

        Assert.Equal("6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71", option.Value);
        Assert.Equal("DEV-TOWER", option.Label);
    }

    [Fact]
    public void Machines_sharing_this_devices_name_are_told_apart_from_it()
    {
        var options = MachineFilterOptions.From(
        [
            Tower,
            Of("0f4c2a9e-0000-4000-8000-000000000001", "DEV-TOWER"),
            Of("a71d33b0-0000-4000-8000-000000000002", "dev-tower")
        ]);

        Assert.Equal(
            ["DEV-TOWER (this device)", "DEV-TOWER (0f4c2a9e)", "dev-tower (a71d33b0)"],
            options.Select(option => option.Label));
    }

    [Fact]
    public void A_name_nobody_shares_is_left_alone()
    {
        var options = MachineFilterOptions.From([Tower, Of("0f4c2a9e-0000-4000-8000-000000000001", "DEV-LAPTOP")]);

        Assert.Equal(["DEV-TOWER", "DEV-LAPTOP"], options.Select(option => option.Label));
    }

    private static Machine Of(string id, string name) => new(new MachineId(Guid.Parse(id)), name);
}
