using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// One whole listing, driven through a scripted process runner instead of the
/// CLIs it asks.
///
/// <para>What these pin is that running the independent probes side by side
/// changes nothing but the time it takes: the same rows, the same message and
/// the same transcript in the same order as at a bound of one, at most the bound
/// in flight, one fetch per mirror, and a Claude row that still only trusts a
/// marketplace this listing pulled.</para>
/// </summary>
public sealed class DevToolListingTests : IDisposable
{
    private const string LocalCommit = "1111111111111111111111111111111111111111";
    private const string FetchedCommit = "2222222222222222222222222222222222222222";

    private readonly string _root;
    private readonly string _catalogPath;
    private readonly string _mirror;

    /// <summary>Where the listing looks for the Claude desktop app's config: a
    /// file that is not there, so the desktop unit asks the package manager and
    /// records a command and a status-line sentence of its own.</summary>
    private readonly string[] _desktopConfig;

    public DevToolListingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "backlog-tool-listing-tests", Guid.NewGuid().ToString("N"));
        _mirror = Path.Combine(_root, "mirror");
        Directory.CreateDirectory(Path.Combine(_root, ".tools"));
        Directory.CreateDirectory(_mirror);

        _catalogPath = DevToolConfigurationPaths.FromRepositoryRoot(_root, "dev-pc").CatalogPath;
        _desktopConfig = [Path.Combine(_root, "claude-desktop", "claude_desktop_config.json")];
        File.WriteAllText(_catalogPath, Catalog(_mirror));
    }

    private DevToolService Service(ScriptedRunner runner, int concurrency, TimeSpan? probeTimeout = null) =>
        new(_catalogPath, runner, concurrency, probeTimeout, _desktopConfig);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_concurrent_listing_reads_exactly_as_a_listing_run_one_command_at_a_time()
    {
        var ct = TestContext.Current.CancellationToken;

        var serial = await Service(new ScriptedRunner(), concurrency: 1).ListAsync(ct);
        var concurrent = await Service(new ScriptedRunner(), concurrency: 5).ListAsync(ct);

        Assert.Equal(serial.Message, concurrent.Message);
        Assert.Equal(
            serial.Commands.Select(command => command.CommandLine),
            concurrent.Commands.Select(command => command.CommandLine));
        Assert.Equal(Serialized(serial), Serialized(concurrent));
    }

    /// <summary>
    /// The order a listing reports in, written out rather than compared with
    /// another run of the same code: both runs merge through the same sequence,
    /// so a comparison alone would agree with a sequence that was wrong.
    ///
    /// <para>This is the order the serial listing ran in — Copilot, .NET tools,
    /// Claude, every plugin row, the desktop app, every MCP server row, then the
    /// application inventory, winget before its probes — and the status line
    /// keeps the same order for what each of them had to say.</para>
    /// </summary>
    [Fact]
    public async Task A_concurrent_listing_reports_its_commands_rows_and_sentences_in_the_serial_order()
    {
        var catalog = await Service(new ScriptedRunner(), concurrency: 5).ListAsync(TestContext.Current.CancellationToken);

        string[] expectedCommands =
        [
            "copilot --version",
            "copilot plugin list",
            "dotnet tool list --global",
            "claude.cmd --version",
            "claude.cmd plugin marketplace list --json",
            "claude.cmd plugin marketplace update market-one",
            "claude.cmd plugin marketplace update market-two",
            "claude.cmd plugin list --json",
            "gh api repos/Owner/One/",
            "gh api repos/Owner/Two/",
            $"git -C {_mirror} fetch origin",
            $"git -C {_mirror} log -1 --format=%H HEAD ",
            $"git -C {_mirror} log -1 --format=%H FETCH_HEAD ",
            $"git -C {_mirror} log -1 --format=%H HEAD ",
            $"git -C {_mirror} log -1 --format=%H FETCH_HEAD ",
            "powershell -NoProfile -NonInteractive -Command \"(Get-AppxPackage",
            "dotnet tool search Owner.Guide",
            "claude.cmd mcp get guide",
            "claude.cmd mcp get aspire",
            "winget --version",
            "winget source update",
            "winget list",
            "winget upgrade",
            "winget show --id Missing.Probed ",
            "winget show --id Missing.App1 ",
            "winget show --id Missing.App2 ",
            "winget show --id Missing.App3 ",
            "winget show --id Missing.App4 ",
            "winget show --id Missing.App5 ",
            "tool-one --version",
            "powershell -Command volume",
            "service --version",
            "service --latest",
            "pwsh --version"
        ];

        var commands = catalog.Commands.Select(command => command.CommandLine).ToArray();
        Assert.True(
            expectedCommands.Length == commands.Length
                && expectedCommands.Zip(commands).All(pair => pair.Second.StartsWith(pair.First, StringComparison.Ordinal)),
            "Expected, in order:\n" + string.Join('\n', expectedCommands) + "\n\nActual:\n" + string.Join('\n', commands));

        string[] expectedRows =
        [
            DevToolConfiguration.KeyFor(DevToolKind.Marketplace, "market-one"),
            DevToolConfiguration.KeyFor(DevToolKind.Marketplace, "market-two"),
            DevToolConfiguration.KeyFor(DevToolKind.Plugin, "alpha"),
            DevToolConfiguration.KeyFor(DevToolKind.Plugin, "beta"),
            DevToolConfiguration.KeyFor(DevToolKind.Plugin, "skills-a"),
            DevToolConfiguration.KeyFor(DevToolKind.Plugin, "skills-b"),
            DevToolConfiguration.KeyFor(DevToolKind.McpServer, "Owner.Guide"),
            DevToolConfiguration.KeyFor(DevToolKind.McpServer, "aspire"),
            DevToolConfiguration.KeyFor(DevToolKind.McpServer, "desk"),
            DevToolConfiguration.KeyFor(DevToolKind.Application, "Git.Git"),
            DevToolConfiguration.KeyFor(DevToolKind.Application, "Missing.Probed"),
            .. Enumerable.Range(1, 7).Select(index => DevToolConfiguration.KeyFor(DevToolKind.Application, $"Missing.App{index}")),
            DevToolConfiguration.KeyFor(DevToolKind.Application, "dev-drive"),
            DevToolConfiguration.KeyFor(DevToolKind.Application, "service"),
            DevToolConfiguration.KeyFor(DevToolKind.Application, "pwsh")
        ];

        Assert.Equal(expectedRows, catalog.Tools.Select(tool => tool.Key));

        Assert.EndsWith(
            " The market-two marketplace could not be refreshed, so its plugins read as version unknown."
            + " The Claude desktop app was not found on this machine."
            + " Only the first 6 missing winget packages were looked up this refresh; Missing.App6, Missing.App7 still read as version unknown.",
            catalog.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A catalog that cannot be read fails before anything is started.
    ///
    /// <para>The catalog is read in full before the first unit starts. Read
    /// halfway through instead, the throw would leave winget and the Claude
    /// CLI running behind a caller that already has its error, with nothing
    /// left to stop them.</para>
    /// </summary>
    [Fact]
    public async Task A_catalog_that_cannot_be_read_fails_before_any_command_starts()
    {
        await File.WriteAllTextAsync(
            _catalogPath,
            """
            {
              "plugins": {},
              "mcpServers": [],
              "applications": [ { "id": "Git.Git", "name": "Git", "provider": "winget", "enabled": true } ]
            }
            """,
            TestContext.Current.CancellationToken);
        var runner = new ScriptedRunner();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(runner, concurrency: 5).ListAsync(TestContext.Current.CancellationToken));

        Assert.Empty(runner.Launches);
    }

    /// <summary>The Claude CLI writes its own state file as it starts, and
    /// nothing says two of it are safe at once. So every <c>claude</c> launch
    /// waits for the one before it, while the other CLIs run beside them.</summary>
    [Fact]
    public async Task No_two_claude_processes_run_at_once_while_the_other_clis_run_beside_them()
    {
        var runner = new ScriptedRunner();

        await Service(runner, concurrency: 5).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, runner.PeakClaudeInFlight);
        Assert.True(runner.PeakInFlight > 1, $"Peak in flight was {runner.PeakInFlight}.");
    }

    [Fact]
    public async Task Independent_probes_run_side_by_side_and_never_above_the_bound()
    {
        var runner = new ScriptedRunner();

        await Service(runner, concurrency: 3).ListAsync(TestContext.Current.CancellationToken);

        Assert.InRange(runner.PeakInFlight, 2, 3);
    }

    [Fact]
    public async Task A_listing_at_a_bound_of_one_never_overlaps_two_launches()
    {
        var runner = new ScriptedRunner();

        await Service(runner, concurrency: 1).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, runner.PeakInFlight);
    }

    [Fact]
    public async Task Two_rows_sharing_a_mirror_fetch_it_once()
    {
        var runner = new ScriptedRunner();

        var catalog = await Service(runner, concurrency: 5).ListAsync(TestContext.Current.CancellationToken);

        Assert.Single(runner.Launches, launch => launch.Contains(" fetch ", StringComparison.Ordinal));

        // And both rows still read the fetched tip, not only the one that fetched.
        foreach (var key in new[] { "plugin:skills-a", "plugin:skills-b" })
        {
            var row = Assert.Single(catalog.Tools, tool => tool.Key == key);
            Assert.Equal(DevToolOutput.ShortCommit(FetchedCommit), row.AvailableVersion);
        }
    }

    [Fact]
    public async Task A_claude_row_reads_its_available_version_only_from_a_marketplace_this_listing_pulled()
    {
        // The pull that succeeds is the slow one, so a row that read the pulled
        // set before every pull had finished would read it as not pulled.
        var runner = new ScriptedRunner { SlowCommand = "plugin marketplace update market-one" };

        var catalog = await Service(runner, concurrency: 5).ListAsync(TestContext.Current.CancellationToken);

        var pulled = ClaudeState(catalog, "plugin:alpha");
        var notPulled = ClaudeState(catalog, "plugin:beta");

        Assert.Equal("1.1.0", pulled.AvailableVersion);
        Assert.Equal(DevToolOutput.Unknown, notPulled.AvailableVersion);
        Assert.Contains("The market-two marketplace could not be refreshed", catalog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_caller_that_cancels_gets_its_cancellation_back()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runner = new ScriptedRunner { HangOn = "winget list", OnHang = cancel.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(runner, concurrency: 5).ListAsync(cancel.Token));
    }

    [Fact]
    public async Task A_command_that_outlives_its_deadline_is_a_timed_out_row_and_not_a_failed_listing()
    {
        var runner = new ScriptedRunner { HangOn = "winget upgrade" };

        var catalog = await Service(runner, concurrency: 5, probeTimeout: TimeSpan.FromMilliseconds(300))
            .ListAsync(TestContext.Current.CancellationToken);

        var upgrade = Assert.Single(catalog.Commands, command => command.CommandLine.StartsWith("winget upgrade", StringComparison.Ordinal));
        Assert.Equal(-2, upgrade.ExitCode);
    }

    private static DevToolHostState ClaudeState(DevToolCatalog catalog, string key) =>
        Assert.Single(Assert.Single(catalog.Tools, tool => tool.Key == key).HostStates, state => state.Host == DevToolHosts.Claude);

    private static string Serialized(DevToolCatalog catalog) => JsonSerializer.Serialize(catalog);

    private static string Catalog(string mirror)
    {
        var missingWinget = string.Join(
            ",",
            Enumerable.Range(1, 7).Select(index => $$"""{ "id": "Missing.App{{index}}", "name": "App {{index}}", "provider": "winget", "enabled": true }"""));

        return $$"""
            {
              "claude": {
                "marketplaces": [
                  { "name": "market-one", "source": "Owner/One" },
                  { "name": "market-two", "source": "Owner/Two" }
                ]
              },
              "plugins": [
                { "name": "alpha", "source": "Owner/One:plugins/alpha", "enabled": true },
                { "name": "beta", "source": "Owner/Two:plugins/beta", "claudeMarketplace": "market-two", "enabled": true },
                { "name": "skills-a", "source": "Owner/Mirror:plugins/a", "kind": "repository-skills", "repoPath": {{JsonSerializer.Serialize(mirror)}}, "skillsPath": "plugins\\a\\skills", "hosts": [ "copilot" ], "enabled": true },
                { "name": "skills-b", "source": "Owner/Mirror:plugins/b", "kind": "repository-skills", "repoPath": {{JsonSerializer.Serialize(mirror)}}, "skillsPath": "plugins\\b\\skills", "hosts": [ "copilot" ], "enabled": true }
              ],
              "mcpServers": [
                { "name": "guide", "packageId": "Owner.Guide", "claude": { "name": "guide", "command": "guide-server" }, "enabled": true },
                { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true },
                { "name": "desk", "command": "desk-server", "hosts": [ "claude-desktop" ], "enabled": true }
              ],
              "applications": [
                { "id": "Git.Git", "name": "Git", "provider": "winget", "probe": { "command": "git", "args": [ "--version" ] }, "enabled": true },
                { "id": "Missing.Probed", "name": "Probed", "provider": "winget", "probe": { "command": "tool-one", "args": [ "--version" ] }, "enabled": true },
                {{missingWinget}},
                { "id": "dev-drive", "name": "Dev Drive", "provider": "command", "detect": { "command": "powershell", "args": [ "-Command", "volume" ], "expect": "ReFS" }, "enabled": true },
                { "id": "service", "name": "Service", "provider": "command", "detect": { "command": "service", "args": [ "--version" ] }, "available": { "command": "service", "args": [ "--latest" ] }, "enabled": true },
                { "id": "pwsh", "name": "PowerShell", "provider": "command", "detect": { "command": "pwsh", "args": [ "--version" ] }, "enabled": true }
              ]
            }
            """;
    }

    /// <summary>
    /// Answers every command a listing asks with what the real CLI would print,
    /// after a short delay so that launches that can overlap do.
    /// </summary>
    private sealed class ScriptedRunner : IDevToolProcessRunner
    {
        private int _inFlight;
        private int _peak;
        private int _claudeInFlight;
        private int _claudePeak;

        public ConcurrentQueue<string> Launches { get; } = new();

        public int PeakInFlight => Volatile.Read(ref _peak);

        /// <summary>The most <c>claude</c> processes that were ever running at
        /// once.</summary>
        public int PeakClaudeInFlight => Volatile.Read(ref _claudePeak);

        /// <summary>A command line prefix that answers only once the listing's own
        /// deadline or the caller's cancellation stops it.</summary>
        public string? HangOn { get; init; }

        /// <summary>Called as the hanging command starts.</summary>
        public Action? OnHang { get; init; }

        /// <summary>A command line suffix answered ten times slower than the rest.</summary>
        public string? SlowCommand { get; init; }

        public async Task<DevToolProcessResult> RunAsync(DevToolProcessLaunch launch, CancellationToken ct)
        {
            var line = string.Join(' ', new[] { launch.FileName }.Concat(launch.Arguments));
            Launches.Enqueue(line);

            if (Answer(launch.FileName, launch.Arguments) is not { } answer)
            {
                throw new Win32Exception(2, $"An error occurred trying to start process '{launch.FileName}'.");
            }

            var claude = launch.FileName.StartsWith("claude", StringComparison.OrdinalIgnoreCase);
            UpdatePeak(ref _peak, Interlocked.Increment(ref _inFlight));
            if (claude)
            {
                UpdatePeak(ref _claudePeak, Interlocked.Increment(ref _claudeInFlight));
            }

            try
            {
                if (HangOn is { } hang && line.StartsWith(hang, StringComparison.Ordinal))
                {
                    OnHang?.Invoke();
                    await Task.Delay(Timeout.Infinite, ct);
                }

                var slow = SlowCommand is { } suffix && line.EndsWith(suffix, StringComparison.Ordinal);
                await Task.Delay(slow ? 100 : 10, ct);

                return answer;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
                if (claude)
                {
                    Interlocked.Decrement(ref _claudeInFlight);
                }
            }
        }

        private static void UpdatePeak(ref int peak, int inFlight)
        {
            int seen;
            do
            {
                seen = Volatile.Read(ref peak);
                if (inFlight <= seen)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref peak, inFlight, seen) != seen);
        }

        /// <summary>What each CLI says, or nothing for a CLI that is not on this
        /// machine.</summary>
        private static DevToolProcessResult? Answer(string fileName, IReadOnlyList<string> args)
        {
            var command = string.Join(' ', args);

            return fileName switch
            {
                "copilot" when command == "--version" => Ok("1.0.0"),
                "copilot" when command == "plugin list" => Ok("- alpha (v1.0.0)\n- beta (v2.0.0)"),
                "dotnet" when command == "tool list --global" => Ok("""
                    Package Id      Version      Commands
                    ---------------------------------------
                    owner.guide     1.0.0        guide-server
                    """),
                "dotnet" when command.StartsWith("tool search", StringComparison.Ordinal) => Ok("""
                    Package ID      Latest Version      Authors
                    --------------------------------------------
                    owner.guide     1.2.0               Owner
                    """),
                "claude.cmd" => Claude(command),
                "gh" when command.StartsWith("api repos/Owner/One/", StringComparison.Ordinal) => Ok("""{ "version": "1.1.0" }"""),
                "gh" when command.StartsWith("api repos/Owner/Two/", StringComparison.Ordinal) => Ok("""{ "version": "2.1.0" }"""),
                "git" when command.Contains(" fetch ", StringComparison.Ordinal) => Ok(string.Empty),
                "git" when command.Contains("FETCH_HEAD", StringComparison.Ordinal) => Ok(FetchedCommit),
                "git" when command.Contains(" log ", StringComparison.Ordinal) => Ok(LocalCommit),
                "winget" => Winget(command),
                // The desktop app's package probe answers nothing: not installed.
                "powershell" when command.Contains("Get-AppxPackage", StringComparison.Ordinal) => Ok(string.Empty),
                "powershell" => Ok("ReFS"),
                "service" when command == "--version" => Ok("1.0.0"),
                "service" when command == "--latest" => Ok("1.1.0"),
                "pwsh" => Ok("PowerShell 7.6.5"),
                "tool-one" => null,
                // Every CLI the fixture names is answered above; anything else
                // is a CLI this machine does not have.
                _ => null
            };
        }

        private static DevToolProcessResult? Claude(string command) => command switch
        {
            "--version" => Ok("2.1.0 (Claude Code)"),
            "plugin marketplace list --json" => Ok("""[ { "name": "market-one" }, { "name": "market-two" } ]"""),
            "plugin marketplace update market-one" => Ok("Updated market-one"),
            "plugin marketplace update market-two" => new DevToolProcessResult(1, string.Empty, "Failed to update market-two"),
            "plugin list --json" => Ok("""
                [
                  { "id": "alpha@market-one", "version": "1.0.0", "enabled": true, "installPath": "C:\\nowhere\\alpha\\1.0.0" },
                  { "id": "beta@market-two", "version": "2.0.0", "enabled": true, "installPath": "C:\\nowhere\\beta\\2.0.0" }
                ]
                """),
            "mcp get guide" => Ok("guide:\n  Scope: User config\n  Type: stdio\n  Command: guide-server\n"),
            _ when command.StartsWith("mcp get ", StringComparison.Ordinal) => new DevToolProcessResult(1, string.Empty, $"No MCP server named {command[8..]} found."),
            _ => new DevToolProcessResult(1, string.Empty, "unknown command")
        };

        private static DevToolProcessResult Winget(string command)
        {
            if (command == "--version")
            {
                return Ok("v1.9.25200");
            }

            if (command.StartsWith("source update", StringComparison.Ordinal))
            {
                return Ok("Updating source: winget\nDone");
            }

            if (command.StartsWith("list", StringComparison.Ordinal) || command.StartsWith("upgrade", StringComparison.Ordinal))
            {
                return Ok("""
                    Name Id      Version Available Source
                    -------------------------------------
                    Git  Git.Git 2.54.0  2.55.0.3  winget
                    """);
            }

            if (command.StartsWith("show --id ", StringComparison.Ordinal))
            {
                var id = command["show --id ".Length..].Split(' ')[0];

                return Ok($"Found {id} [{id}]\nVersion: 3.{id.Length}.0\nPublisher: Somebody\n");
            }

            return new DevToolProcessResult(1, string.Empty, "unknown command");
        }

        private static DevToolProcessResult Ok(string output) => new(0, output, string.Empty);
    }
}
