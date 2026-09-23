using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The MCP server's half of the workspace settings file: which loopback port the
/// listener takes, and the bearer token it demands.
/// <para>
/// The token is the part worth being careful about. Local ADR 0012 §1 has the
/// app generate it once and keep it, and every registration that talks to this
/// server names it — so "generated once" and "then stable" are two separate
/// claims and both are asserted here, across a restart rather than only within
/// one store.
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
    public void An_untouched_workspace_listens_on_the_default_port_and_has_no_token()
    {
        var store = Store();

        Assert.Equal(WorkspaceSettingsStore.DefaultMcpServerPort, store.McpServerPort);
        Assert.Equal(5757, store.McpServerPort);

        // Not generated at construction: a secret on disk for a feature nobody
        // switched on is a secret with no reason to exist.
        Assert.Null(store.McpServerToken);
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

    [Fact]
    public void A_token_is_generated_on_first_need_and_then_stable()
    {
        var store = Store();

        var first = store.EnsureMcpServerToken();

        Assert.False(string.IsNullOrWhiteSpace(first));

        // Asking again is not asking for another one.
        Assert.Equal(first, store.EnsureMcpServerToken());
        Assert.Equal(first, store.McpServerToken);
    }

    [Fact]
    public void A_generated_token_survives_a_restart()
    {
        var first = Store().EnsureMcpServerToken();

        var reopened = Store();

        Assert.Equal(first, reopened.McpServerToken);
        Assert.Equal(first, reopened.EnsureMcpServerToken());
    }

    /// <summary>Two workspaces are two tokens. It reads like a tautology and is
    /// not: a token derived from the machine, the user or the path would pass
    /// every other test in this file and hand every installation the same
    /// secret.</summary>
    [Fact]
    public void Two_workspaces_do_not_share_a_token()
    {
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);

        var mine = Store().EnsureMcpServerToken();
        var theirs = new WorkspaceSettingsStore(other, Path.Combine(other, "settings.json"), _ => null)
            .EnsureMcpServerToken();

        Assert.NotEqual(mine, theirs);
    }

    /// <summary>256 bits of Base64Url, which is 43 characters and no padding —
    /// the same construction <c>IRegistrationCredentialGenerator</c> uses. The
    /// length is asserted because it is what makes the fixed-time comparison's
    /// length leak worth nothing.</summary>
    [Fact]
    public void A_token_is_256_bits_of_url_safe_text()
    {
        var token = Store().EnsureMcpServerToken();

        Assert.Equal(43, token.Length);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Generating_a_token_announces_it()
    {
        var store = Store();
        var raised = 0;
        store.McpChanged += () => raised++;

        _ = store.EnsureMcpServerToken();
        Assert.Equal(1, raised);

        // The second ask generates nothing, so it announces nothing.
        _ = store.EnsureMcpServerToken();
        Assert.Equal(1, raised);
    }

    /// <summary>A workspace nobody has switched the server on in keeps producing
    /// the settings file it always did — no row, so no reader of that file has to
    /// learn about a feature nobody chose.</summary>
    [Fact]
    public void The_default_port_and_no_token_write_no_row()
    {
        var store = Store();

        // Any save at all, through a setting that is not this one.
        _ = store.SetBackupSchedule(BackupSchedule.Off with { Cadence = BackupCadence.Daily });

        using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));

        Assert.False(document.RootElement.TryGetProperty("mcpServer", out _));
    }

    [Fact]
    public void A_token_is_written_even_at_the_default_port()
    {
        var store = Store();
        var token = store.EnsureMcpServerToken();

        using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));

        var row = document.RootElement.GetProperty("mcpServer");

        Assert.Equal(token, row.GetProperty("token").GetString());

        // The port is null while it is the default, so moving the default later
        // moves this workspace with it.
        Assert.Equal(JsonValueKind.Null, row.GetProperty("port").ValueKind);
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
            $$"""{ "rootDirectory": {{JsonSerializer.Serialize(_root)}}, "mcpServer": { "token": "kept", "port": {{port}} } }""");

        var store = Store();

        Assert.Equal(WorkspaceSettingsStore.DefaultMcpServerPort, store.McpServerPort);

        // And the token beside it is still read: one unusable field does not
        // discard the row.
        Assert.Equal("kept", store.McpServerToken);
    }

    /// <summary>
    /// The documented sharp edge, asserted rather than left to be discovered.
    /// <para>
    /// <c>ReadSettings</c> answers null for a file it cannot parse as well as for
    /// one that is not there — by design, so a broken file never stops the app
    /// opening — and the store cannot tell those apart. So a corrupt file reads
    /// as "no token yet" and the next need writes a fresh one, which every
    /// existing registration then fails to authenticate with. This test exists to
    /// make that a decision somebody took rather than a surprise; see
    /// <c>EnsureMcpServerToken</c>'s remarks.
    /// </para>
    /// </summary>
    [Fact]
    public void A_corrupt_settings_file_rotates_the_token()
    {
        var original = Store().EnsureMcpServerToken();

        File.WriteAllText(SettingsFile, "{ this is not json");

        var reopened = Store();

        Assert.Null(reopened.McpServerToken);
        Assert.NotEqual(original, reopened.EnsureMcpServerToken());
    }

    /// <summary>An absent file is the ordinary first run, and is not a
    /// rotation of anything: there was nothing to rotate.</summary>
    [Fact]
    public void An_absent_settings_file_is_a_first_run_rather_than_a_rotation()
    {
        Assert.False(File.Exists(SettingsFile));

        var store = Store();

        Assert.Null(store.McpServerToken);
        Assert.False(string.IsNullOrWhiteSpace(store.EnsureMcpServerToken()));
        Assert.True(File.Exists(SettingsFile));
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
