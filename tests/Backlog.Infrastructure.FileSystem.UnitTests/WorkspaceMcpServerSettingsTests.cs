using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The MCP server's half of the workspace settings file: which loopback port the
/// listener takes, the grants the app made for what it installs, and the escape hatch's
/// debug setting.
/// <para>
/// Since the shared token was retired (local ADR 0052, Phase 1) the file holds no MCP
/// secret: a token an earlier build wrote is read only to be retired, and dropped once the
/// migration has rewritten what names it.
/// </para>
/// <para>
/// Every store is built on the test-seam constructor, which names the settings
/// file separately from the folder. Nothing here touches
/// <c>%LOCALAPPDATA%</c>.
/// </para>
/// </summary>
public class WorkspaceMcpServerSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "workspace-mcp-settings-tests-" + Guid.NewGuid().ToString("N"));

    public WorkspaceMcpServerSettingsTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "settings.json");

    /// <summary>A store over this test's own folder, with the sync-folder probe
    /// stubbed out: whether the machine running the suite has OneDrive signed in
    /// is not something these assertions may depend on.</summary>
    private WorkspaceSettingsStore Store() => new(_root, SettingsFile, _ => null);

    [Fact]
    public void An_untouched_workspace_listens_on_the_default_port_and_has_no_token_and_no_grants()
    {
        var store = Store();

        Assert.Equal(WorkspaceSettingsStore.DefaultMcpServerPort, store.McpServerPort);
        Assert.Equal(5757, store.McpServerPort);
        Assert.Empty(store.RetiredMcpTokens);
        Assert.Null(store.McpServerGrants);
        Assert.False(store.McpServerAcceptsEnvironmentToken);
    }

    [Fact]
    public void A_chosen_port_survives_a_restart()
    {
        Assert.Null(Store().SetMcpServerPort(6060));

        Assert.Equal(6060, Store().McpServerPort);
    }

    [Fact]
    public void Choosing_a_port_announces_it_for_the_listener_to_rebind()
    {
        var store = Store();
        var raised = 0;
        store.McpChanged += () => raised++;

        Assert.Null(store.SetMcpServerPort(6060));
        Assert.Equal(1, raised);

        // Choosing the port it is already on is not a change, so nothing rebinds.
        Assert.Null(store.SetMcpServerPort(6060));
        Assert.Equal(1, raised);
    }

    /// <summary>A port outside the usable range is answered with a sentence
    /// rather than an exception, the way every setter on this store answers a bad
    /// value — and the value on the store does not move.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void An_unusable_port_is_refused_with_a_sentence(int port)
    {
        var store = Store();

        var error = store.SetMcpServerPort(port);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(WorkspaceSettingsStore.DefaultMcpServerPort, store.McpServerPort);
    }

    /// <summary>Zero is refused by the rule above, and it is worth saying why on
    /// its own: to a socket it means "any free port", and a server every
    /// registration names by number is the one thing that may not take one.</summary>
    [Fact]
    public void Port_zero_is_refused_because_it_would_mean_any_port()
    {
        Assert.NotNull(Store().SetMcpServerPort(0));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(5757)]
    [InlineData(65535)]
    public void The_edges_of_the_usable_range_are_accepted(int port)
    {
        var store = Store();

        Assert.Null(store.SetMcpServerPort(port));
        Assert.Equal(port, store.McpServerPort);
    }

    /// <summary>A settings.json an earlier build wrote: its three tokens are read as retired,
    /// kept through an unrelated save so the migration can still recognise what names them, and
    /// gone from the file once <see cref="WorkspaceSettingsStore.ForgetRetiredMcpTokens"/> ran.</summary>
    [Fact]
    public void The_shared_tokens_of_an_earlier_build_are_retired_then_dropped()
    {
        File.WriteAllText(SettingsFile, """
            { "mcpServer": { "token": "shared", "hookToken": "hook", "telemetryToken": "telemetry", "port": 6060 } }
            """);

        var store = Store();
        Assert.Equal(["shared", "hook", "telemetry"], store.RetiredMcpTokens);
        Assert.Equal(6060, store.McpServerPort);

        // An unrelated save before the migration ran keeps them, field by field.
        _ = store.SetBackupSchedule(BackupSchedule.Off with { Cadence = BackupCadence.Daily });
        Assert.Equal(["shared", "hook", "telemetry"], Store().RetiredMcpTokens);

        Assert.Null(store.ForgetRetiredMcpTokens());
        Assert.Empty(store.RetiredMcpTokens);

        var text = File.ReadAllText(SettingsFile);
        Assert.DoesNotContain("shared", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Store().RetiredMcpTokens);
        Assert.Equal(6060, Store().McpServerPort);
    }

    /// <summary>The grants the migration made are recorded by id, survive a restart, and are
    /// announced; a row missing any of the three reads as no migration yet.</summary>
    [Fact]
    public void The_installed_grants_are_recorded_by_id_and_survive_a_restart()
    {
        var store = Store();
        var raised = 0;
        store.McpChanged += () => raised++;

        Assert.Null(store.SetMcpServerGrants(new McpInstalledGrants("g_d", "g_q", "g_t")));
        Assert.Equal(1, raised);

        Assert.Equal(new McpInstalledGrants("g_d", "g_q", "g_t"), Store().McpServerGrants);

        using (var document = JsonDocument.Parse(File.ReadAllText(SettingsFile)))
        {
            var grants = document.RootElement.GetProperty("mcpServer").GetProperty("grants");
            Assert.Equal("g_q", grants.GetProperty("questionHook").GetString());
            Assert.Equal("g_d", grants.GetProperty("delivery").GetString());
            Assert.Equal("g_t", grants.GetProperty("telemetry").GetString());
        }

        File.WriteAllText(SettingsFile, """{ "mcpServer": { "grants": { "delivery": "g_d", "questionHook": "g_q" } } }""");
        Assert.Null(Store().McpServerGrants);
    }

    /// <summary>The BACKLOG_MCP_TOKEN escape hatch: off unless the hand-edited debug setting is
    /// on, and then the environment variable's value — never anything from the file.</summary>
    [Fact]
    public void The_environment_token_is_accepted_only_where_the_debug_setting_is_on()
    {
        var environment = new Dictionary<string, string?> { ["BACKLOG_MCP_TOKEN"] = "from-env" };

        var off = new WorkspaceSettingsStore(_root, SettingsFile, _ => null) { ReadEnvironment = name => environment.GetValueOrDefault(name) };
        Assert.False(off.McpServerAcceptsEnvironmentToken);
        Assert.Null(off.EnvironmentMcpToken);

        File.WriteAllText(SettingsFile, """{ "mcpServer": { "token": "retired", "acceptEnvironmentToken": true } }""");

        var on = new WorkspaceSettingsStore(_root, SettingsFile, _ => null) { ReadEnvironment = name => environment.GetValueOrDefault(name) };
        Assert.True(on.McpServerAcceptsEnvironmentToken);
        Assert.Equal("from-env", on.EnvironmentMcpToken);

        environment["BACKLOG_MCP_TOKEN"] = " ";
        Assert.Null(on.EnvironmentMcpToken);

        // And a save keeps the setting, and drops nothing it should keep.
        Assert.Null(on.ForgetRetiredMcpTokens());
        Assert.True(Store().McpServerAcceptsEnvironmentToken);
    }

    /// <summary>A workspace nobody has switched the server on in keeps producing
    /// the settings file it always did — no row, so no reader of that file has to
    /// learn about a feature nobody chose.</summary>
    [Fact]
    public void The_default_port_and_no_grants_write_no_row()
    {
        var store = Store();

        // Any save at all, through a setting that is not this one.
        _ = store.SetBackupSchedule(BackupSchedule.Off with { Cadence = BackupCadence.Daily });

        using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));

        Assert.False(document.RootElement.TryGetProperty("mcpServer", out _));
    }

    /// <summary>A hand-edited port outside the range reads as the default rather
    /// than as a failure to open, the way a hand-edited backup schedule reads as
    /// off. A file like any other here.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("null")]
    public void A_hand_edited_port_that_is_not_usable_reads_as_the_default(string port)
    {
        File.WriteAllText(
            SettingsFile,
            $$"""{ "rootDirectory": {{JsonSerializer.Serialize(_root)}}, "mcpServer": { "grants": { "delivery": "g_d", "questionHook": "g_q", "telemetry": "g_t" }, "port": {{port}} } }""");

        var store = Store();

        Assert.Equal(WorkspaceSettingsStore.DefaultMcpServerPort, store.McpServerPort);

        // And the grants beside it are still read: one unusable field does not
        // discard the row.
        Assert.Equal("g_d", store.McpServerGrants?.Delivery);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder the runner could not remove is not a failed test.
        }
    }
}
