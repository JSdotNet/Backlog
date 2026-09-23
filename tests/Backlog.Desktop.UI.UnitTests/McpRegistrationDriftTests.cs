using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Whether what a host has registered still points where the catalog says it
/// should — and what the row is allowed to offer when it does not.
///
/// <para>The port an HTTP server listens on is a setting, and a setting that
/// changes leaves every host registered against the old one. Nothing detects
/// that today: the row compares two version columns, and an HTTP row has no
/// versions. So the comparison is a target comparison, made here where it can be
/// checked without a machine, and the row carries the answer rather than deriving
/// it from prose it cannot parse.</para>
/// </summary>
public class McpRegistrationDriftTests
{
    private const string Expected = "http://127.0.0.1:5757/mcp";

    [Fact]
    public void A_registration_pointing_at_the_same_url_has_not_drifted()
    {
        var details = new DevToolOutput.ClaudeMcpServerDetails("User config", string.Empty)
        {
            Type = "http",
            Url = Expected
        };

        Assert.False(DevToolConfiguration.RegistrationDrifted("http", Expected, details));
    }

    /// <summary>The case the whole comparison exists for: the port moved, every
    /// host is still registered against the old one, and nothing anywhere said
    /// so.</summary>
    [Fact]
    public void A_registration_pointing_at_another_port_has_drifted()
    {
        var details = new DevToolOutput.ClaudeMcpServerDetails("User config", string.Empty)
        {
            Type = "http",
            Url = "http://127.0.0.1:6000/mcp"
        };

        Assert.True(DevToolConfiguration.RegistrationDrifted("http", Expected, details));
    }

    /// <summary>A registration made over the wrong transport is drift even when
    /// the target reads the same: a stdio registration of a server the catalog
    /// says is reached over HTTP is a registration that cannot work, and it is
    /// exactly what an older build of this app would have written.</summary>
    [Fact]
    public void A_registration_made_over_another_transport_has_drifted()
    {
        var details = new DevToolOutput.ClaudeMcpServerDetails("User config", "backlog-mcp")
        {
            Type = "stdio"
        };

        Assert.True(DevToolConfiguration.RegistrationDrifted("http", Expected, details));
    }

    /// <summary>
    /// And the difference that is not a difference. A URL is not a string: the
    /// CLI prints back what it was given, a person editing the catalog writes a
    /// trailing slash or an upper-case scheme, and a comparison that called either
    /// of those drift would put a Re-register button on a row forever and clear
    /// nothing by pressing it.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:5757/mcp/")]
    [InlineData("HTTP://127.0.0.1:5757/mcp")]
    public void A_url_that_only_reads_differently_has_not_drifted(string registered)
    {
        var details = new DevToolOutput.ClaudeMcpServerDetails("User config", string.Empty)
        {
            Type = "http",
            Url = registered
        };

        Assert.False(DevToolConfiguration.RegistrationDrifted("http", Expected, details));
    }

    /// <summary>Nothing registered is not drift. It is absence, which is the
    /// other half of the row and has its own answer — a registration that was
    /// never made cannot have moved.</summary>
    [Fact]
    public void A_registration_that_is_not_there_has_not_drifted()
    {
        Assert.False(DevToolConfiguration.RegistrationDrifted("http", Expected, details: null));
    }

    /// <summary>A command-registered server is compared the same way, against the
    /// command it declares. It is the same question asked of the other
    /// mechanism.</summary>
    [Fact]
    public void A_command_registration_is_compared_against_its_command()
    {
        var matching = new DevToolOutput.ClaudeMcpServerDetails("User config", "aspire agent mcp") { Type = "stdio" };
        var moved = new DevToolOutput.ClaudeMcpServerDetails("User config", "aspire-preview agent mcp") { Type = "stdio" };

        Assert.False(DevToolConfiguration.RegistrationDrifted(declaredType: string.Empty, "aspire agent mcp", matching));
        Assert.True(DevToolConfiguration.RegistrationDrifted(declaredType: string.Empty, "aspire agent mcp", moved));
    }

    /// <summary>
    /// What the row does about it. A drifted registration is repaired by
    /// re-registering, and until now nothing offered that: the drifted row got a
    /// note in its status and no button at all.
    /// </summary>
    [Fact]
    public void A_drifted_http_row_can_be_re_registered()
    {
        Assert.True(RowWith(drifted: true, installed: true, enabled: true).CanReRegister);
    }

    /// <summary>
    /// And the row this repairs by arriving: the command-registered server has
    /// been able to drift since the day it was added, and its only answer was a
    /// sentence. Re-register is not an HTTP feature — it is what a registration
    /// that points somewhere else needs, whichever mechanism made it.
    /// </summary>
    [Fact]
    public void A_drifted_command_registered_row_can_be_re_registered_too()
    {
        var row = RowWith(drifted: true, installed: true, enabled: true) with
        {
            // The shape of the aspire row: nothing to install, so nothing an
            // Installable-gated offer could ever reach.
            Installable = false
        };

        Assert.True(row.CanReRegister);

        // And it is emphatically not the Update offer under another name: that one
        // is gated on Installable, which this row is not.
        Assert.False(row.CanUpdate);
        Assert.False(row.CanInstall);
    }

    /// <summary>A row this machine has switched off is not a row to act on. The
    /// same gate every other offer carries.</summary>
    [Fact]
    public void A_row_this_machine_has_switched_off_offers_nothing()
    {
        Assert.False(RowWith(drifted: true, installed: true, enabled: false).CanReRegister);
    }

    /// <summary>Nothing drifted, nothing to repair — including the host that has
    /// no registration at all, which needs registering rather than
    /// re-registering.</summary>
    [Fact]
    public void A_row_that_has_not_drifted_offers_nothing()
    {
        Assert.False(RowWith(drifted: false, installed: true, enabled: true).CanReRegister);
        Assert.False(RowWith(drifted: true, installed: false, enabled: true).CanReRegister);
    }

    private static DevToolInfo RowWith(bool drifted, bool installed, bool enabled) =>
        new(
            "mcp:backlog",
            DevToolKind.McpServer,
            "backlog",
            Expected,
            ConfiguredEnabled: enabled,
            Installed: installed,
            DevToolOutput.NoVersion,
            DevToolOutput.NoVersion,
            "Registered with Claude")
        {
            HostStates =
            [
                new(DevToolHosts.Claude, installed, DevToolOutput.NoVersion, DevToolOutput.NoVersion, "Registered")
                {
                    RegistrationDrifted = drifted
                }
            ]
        };
}
