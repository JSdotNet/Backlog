using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Backlog.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.Services;

public sealed partial class CopilotToolService : ICopilotToolService
{
    private readonly IBacklogStore? _store;
    private readonly string? _configPath;
    private readonly ILogger<CopilotToolService>? _logger;

    public CopilotToolService(ILogger<CopilotToolService>? logger = null, string? configPath = null)
        : this(null, logger, configPath)
    {
    }

    public CopilotToolService(IBacklogStore store, ILogger<CopilotToolService>? logger = null)
        : this(store, logger, null)
    {
    }

    private CopilotToolService(IBacklogStore? store, ILogger<CopilotToolService>? logger, string? configPath)
    {
        _store = store;
        _logger = logger;
        _configPath = configPath;
    }

    public async Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default)
    {
        var configPaths = ConfigurationPaths;
        if (!File.Exists(configPaths.CatalogPath))
        {
            return new CopilotToolCatalog([], $"Tool catalog was not found at {configPaths.CatalogPath}.");
        }

        var config = await CopilotToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var messages = new List<string>();
        var installedPlugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var log = new CommandLog();

        try
        {
            installedPlugins = await GetInstalledPluginsAsync(log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to list Copilot plugins.");
            messages.Add("Copilot plugins could not be checked.");
        }

        try
        {
            installedTools = await GetInstalledDotNetToolsAsync(log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to list .NET tools.");
            messages.Add(".NET tools could not be checked.");
        }

        var tools = new List<CopilotToolInfo>();
        foreach (var plugin in GetArray(root, "plugins"))
        {
            var name = GetString(plugin, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var kind = GetString(plugin, "kind");
            var enabled = GetBool(plugin, "enabled");
            var installedVersion = GetPluginInstalledVersion(plugin, installedPlugins, log);
            var installed = !installedVersion.Equals("not installed", StringComparison.OrdinalIgnoreCase);
            var availableVersion = await GetPluginAvailableVersionAsync(plugin, log, ct).ConfigureAwait(false);

            tools.Add(new CopilotToolInfo(
                PluginKey(name),
                CopilotToolKind.Plugin,
                name,
                GetString(plugin, "source"),
                enabled,
                installed,
                installedVersion,
                availableVersion,
                DescribeStatus(enabled, installed, CopilotToolInfo.VersionDiffers(installedVersion, availableVersion), kind)));
        }

        foreach (var server in GetArray(root, "mcpServers"))
        {
            var packageId = GetString(server, "packageId");
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            var name = GetString(server, "name");
            var displayName = string.IsNullOrWhiteSpace(name) ? packageId : $"{name} ({packageId})";
            var enabled = GetBool(server, "enabled");
            var installedVersion = installedTools.TryGetValue(packageId, out var version) ? version : "not installed";
            var availableVersion = await GetDotNetToolAvailableVersionAsync(packageId, log, ct).ConfigureAwait(false);

            tools.Add(new CopilotToolInfo(
                McpServerKey(packageId),
                CopilotToolKind.McpServer,
                displayName,
                packageId,
                enabled,
                installedTools.ContainsKey(packageId),
                installedVersion,
                availableVersion,
                DescribeStatus(enabled, installedTools.ContainsKey(packageId), CopilotToolInfo.VersionDiffers(installedVersion, availableVersion), "mcp-server")));
        }

        var sourceMessage = config.PcConfigExists
            ? $"Showing tools from {config.CatalogPath} with PC config {config.PcConfigPath}."
            : $"Showing tools from {config.CatalogPath}. PC config will be created at {config.PcConfigPath}.";
        var message = messages.Count == 0 ? sourceMessage : $"{sourceMessage} {string.Join(" ", messages)}";
        return new CopilotToolCatalog(tools, message) { Commands = log.Commands };
    }

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => ApplyAsync(key, null, ct);

    public async Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default)
    {
        var catalog = await ListAsync(ct).ConfigureAwait(false);

        // "Update all" is several runs stitched together, so its log is too: the
        // scan that decided what was out of date, then each tool's own commands
        // in the order they ran. Keeping only the last tool's would hide the
        // failure that mattered behind whichever one happened to go last.
        var commands = new List<CopilotToolCommand>(catalog.Commands);
        var candidates = catalog.Tools.Where(tool => tool.CanUpdate).ToArray();
        if (candidates.Length == 0)
        {
            return CopilotToolActionResult.Ok("No enabled tools have updates available.", commands);
        }

        var updated = 0;
        var failures = new List<string>();
        foreach (var tool in candidates)
        {
            var result = await ApplyAsync(tool.Key, null, ct).ConfigureAwait(false);
            commands.AddRange(result.Commands);
            if (result.Succeeded)
            {
                updated++;
            }
            else
            {
                failures.Add($"{tool.Name}: {result.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return CopilotToolActionResult.Failed($"Updated {updated} tool(s); {failures.Count} failed. {string.Join(" ", failures)}", commands);
        }

        return CopilotToolActionResult.Ok($"Updated {updated} tool(s).", commands);
    }

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, true, ct);

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, false, ct);

    /// <summary>One action, and every command it ran. The log is attached here
    /// rather than at each return inside <see cref="ApplyCoreAsync" />, because
    /// the exits that most need it are the ones that leave early: a refusal
    /// carries the output explaining it, and the catch below would otherwise
    /// report an exception message with nothing behind it.</summary>
    private async Task<CopilotToolActionResult> ApplyAsync(string key, bool? enabled, CancellationToken ct)
    {
        var log = new CommandLog();
        var result = await ApplyCoreAsync(key, enabled, log, ct).ConfigureAwait(false);

        return result with { Commands = log.Commands };
    }

    private async Task<CopilotToolActionResult> ApplyCoreAsync(string key, bool? enabled, CommandLog log, CancellationToken ct)
    {
        var configPaths = ConfigurationPaths;
        if (!File.Exists(configPaths.CatalogPath))
        {
            return CopilotToolActionResult.Failed($"Tool catalog was not found at {configPaths.CatalogPath}.");
        }

        var config = await CopilotToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var node = FindToolNode(root, key);
        if (node is null)
        {
            return CopilotToolActionResult.Failed("That tool is no longer in the config.");
        }

        if (enabled is not null)
        {
            await CopilotToolConfiguration.WriteEnabledOverrideAsync(configPaths, key, enabled.Value, ct).ConfigureAwait(false);
            root = (await CopilotToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false)).Root;
            node = FindToolNode(root, key) ?? throw new InvalidOperationException("That tool is no longer in the config.");
        }

        try
        {
            return key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)
                ? await ApplyPluginAsync(node, log, ct).ConfigureAwait(false)
                : await ApplyMcpServerAsync(node, log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Copilot tool action failed for {Key}.", key);
            return CopilotToolActionResult.Failed($"{ToolDisplayName(node)} could not be changed: {ex.Message}");
        }
    }

    private CopilotToolConfigurationPaths ConfigurationPaths => _configPath is null
        ? CopilotToolConfigurationPaths.CreateDefault(storageRootDirectory: _store?.RootDirectory)
        : CopilotToolConfigurationPaths.FromCatalogPath(_configPath);

    private async Task<CopilotToolActionResult> ApplyPluginAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        var name = GetRequiredString(plugin, "name");
        var kind = GetString(plugin, "kind");
        var enabled = GetBool(plugin, "enabled");

        if (kind.Equals("repository-skills", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("repository-canvases", StringComparison.OrdinalIgnoreCase))
        {
            var status = enabled
                ? await RefreshRepositorySourceAsync(plugin, log, ct).ConfigureAwait(false)
                : "disabled in config";
            return CopilotToolActionResult.Ok($"{name}: {status}.");
        }

        var cli = ResolveCopilotCli(log);
        var installed = await GetInstalledPluginsAsync(log, ct).ConfigureAwait(false);
        var isInstalled = installed.ContainsKey(name);

        if (!enabled)
        {
            if (!isInstalled)
            {
                return CopilotToolActionResult.Ok($"{name} is already disabled.");
            }

            var uninstall = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "uninstall", name], log, ct).ConfigureAwait(false);
            return uninstall.ExitCode == 0
                ? CopilotToolActionResult.Ok($"{name} was disabled.")
                : CopilotToolActionResult.Failed(CommandFailure(name, uninstall));
        }

        var args = isInstalled ? new[] { "plugin", "update", name } : ["plugin", "install", GetRequiredString(plugin, "source")];
        var result = await RunAsync(cli.Command, [.. cli.Prefix, .. args], log, ct).ConfigureAwait(false);
        return result.ExitCode == 0
            ? CopilotToolActionResult.Ok($"{name} was {(isInstalled ? "updated" : "enabled")}.")
            : CopilotToolActionResult.Failed(CommandFailure(name, result));
    }

    private async Task<CopilotToolActionResult> ApplyMcpServerAsync(JsonNode server, CommandLog log, CancellationToken ct)
    {
        var packageId = GetRequiredString(server, "packageId");
        var enabled = GetBool(server, "enabled");
        var installed = await GetInstalledDotNetToolsAsync(log, ct).ConfigureAwait(false);
        var isInstalled = installed.ContainsKey(packageId);

        string[] args;
        string success;
        if (!enabled)
        {
            if (!isInstalled)
            {
                return CopilotToolActionResult.Ok($"{packageId} is already disabled.");
            }

            args = ["tool", "uninstall", "--global", packageId];
            success = $"{packageId} was disabled.";
        }
        else if (isInstalled)
        {
            args = ["tool", "update", "--global", packageId];
            success = $"{packageId} was updated.";
        }
        else
        {
            args = ["tool", "install", "--global", packageId];
            success = $"{packageId} was enabled.";
        }

        var result = await RunAsync("dotnet", args, log, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? CopilotToolActionResult.Ok(success) : CopilotToolActionResult.Failed(CommandFailure(packageId, result));
    }

    private async Task<Dictionary<string, string>> GetInstalledPluginsAsync(CommandLog log, CancellationToken ct)
    {
        var cli = ResolveCopilotCli(log);
        var result = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "list"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CommandFailure("Copilot plugin list", result));
        }

        var plugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.Output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("Name", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("-"))
            {
                continue;
            }

            var match = PluginListLineRegex().Match(trimmed);
            if (match.Success)
            {
                plugins[match.Groups["name"].Value] = string.IsNullOrWhiteSpace(match.Groups["version"].Value)
                    ? "installed"
                    : match.Groups["version"].Value;
            }
        }

        return plugins;
    }

    private static async Task<Dictionary<string, string>> GetInstalledDotNetToolsAsync(CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "list", "--global"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CommandFailure("dotnet tool list", result));
        }

        var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.Output))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("Package Id", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("-"))
            {
                continue;
            }

            var parts = WhitespaceRegex().Split(trimmed);
            if (parts.Length >= 2)
            {
                tools[parts[0]] = parts[1];
            }
        }

        return tools;
    }

    private static async Task<string> GetDotNetToolAvailableVersionAsync(string packageId, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "search", packageId, "--exact-match"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return "unknown";
        }

        foreach (var line in SplitLines(result.Output))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = WhitespaceRegex().Split(trimmed);
            return parts.Length >= 2 ? parts[1] : "unknown";
        }

        return "unknown";
    }

    private static async Task<string> GetPluginAvailableVersionAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        var source = GetString(plugin, "source");
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unknown";
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var result = await RunAsync("gh", ["release", "view", "--repo", $"{parts[0]}/{parts[1]}", "--json", "tagName", "--jq", ".tagName"], log, ct).ConfigureAwait(false);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()))
                {
                    return result.Output.Trim();
                }
            }
        }

        var repoPath = GetString(plugin, "repoPath");
        if (!string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath))
        {
            var result = await RunAsync("git", ["-C", repoPath, "rev-parse", "--short", "HEAD"], log, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()))
            {
                return result.Output.Trim();
            }
        }

        return "unknown";
    }

    private static string GetPluginInstalledVersion(JsonNode plugin, IReadOnlyDictionary<string, string> installedPlugins, CommandLog log)
    {
        var name = GetRequiredString(plugin, "name");
        if (installedPlugins.TryGetValue(name, out var version))
        {
            return version;
        }

        var kind = GetString(plugin, "kind");
        if (!kind.Equals("repository-skills", StringComparison.OrdinalIgnoreCase)
            && !kind.Equals("repository-canvases", StringComparison.OrdinalIgnoreCase))
        {
            return "not installed";
        }

        var repoPath = GetString(plugin, "repoPath");
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return "not installed";
        }

        var result = RunAsync("git", ["-C", repoPath, "rev-parse", "--short", "HEAD"], log, CancellationToken.None).GetAwaiter().GetResult();
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()) ? result.Output.Trim() : "source";
    }

    private static async Task<string> RefreshRepositorySourceAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        var source = GetRequiredString(plugin, "source");
        var repoPath = ResolveConfiguredPath(GetRequiredString(plugin, "repoPath"));
        if (Directory.Exists(repoPath))
        {
            var pull = await RunAsync("git", ["-C", repoPath, "pull", "--ff-only"], log, ct).ConfigureAwait(false);
            return pull.ExitCode == 0 ? "source refreshed" : CommandFailure(ToolDisplayName(plugin), pull);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repoPath) ?? Environment.CurrentDirectory);
        var clone = await RunAsync("git", ["clone", source, repoPath], log, ct).ConfigureAwait(false);
        return clone.ExitCode == 0 ? "source cloned" : CommandFailure(ToolDisplayName(plugin), clone);
    }

    private static IEnumerable<JsonNode> GetArray(JsonNode root, string name) =>
        root[name]?.AsArray().Where(node => node is not null).Cast<JsonNode>() ?? [];

    private static JsonNode? FindToolNode(JsonNode root, string key)
    {
        if (key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase))
        {
            var name = key["plugin:".Length..];
            return GetArray(root, "plugins").FirstOrDefault(node => GetString(node, "name").Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (key.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            var packageId = key["mcp:".Length..];
            return GetArray(root, "mcpServers").FirstOrDefault(node => GetString(node, "packageId").Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string GetString(JsonNode node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static string GetRequiredString(JsonNode node, string name) =>
        string.IsNullOrWhiteSpace(GetString(node, name))
            ? throw new InvalidOperationException($"The tool config entry is missing '{name}'.")
            : GetString(node, name);

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    private static string PluginKey(string name) => $"plugin:{name}";

    private static string McpServerKey(string packageId) => $"mcp:{packageId}";

    private static string ToolDisplayName(JsonNode node) => GetString(node, "name") is { Length: > 0 } name ? name : GetString(node, "packageId");

    private static string DescribeStatus(bool enabled, bool installed, bool updateAvailable, string kind)
    {
        if (!enabled)
        {
            return "Disabled in config";
        }

        if (!installed)
        {
            return "Not installed";
        }

        return updateAvailable ? "Update available" : $"Enabled {kind}";
    }

    private static (string Command, string[] Prefix) ResolveCopilotCli(CommandLog log)
    {
        var copilot = RunAsync("copilot", ["--version"], log, CancellationToken.None).GetAwaiter().GetResult();
        return copilot.ExitCode == 0 ? ("copilot", []) : ("gh", ["copilot"]);
    }

    private static async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CommandLog log, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Redirecting the streams is not what keeps the console away — only
            // this flag is. The desktop head is a GUI-subsystem process with no
            // console of its own, so Windows hands every child it starts a brand
            // new one, and opening the tools tab meant a dozen of them blinking
            // across the screen while the pane quietly read their output.
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var result = new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));

        log.Record(fileName, arguments, result);

        return result;
    }

    private static string CommandFailure(string name, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(details) ? $"{name} failed with exit code {result.ExitCode}." : details.Trim();
    }

    private static string ResolveConfiguredPath(string path) => Environment.ExpandEnvironmentVariables(path);

    private static IEnumerable<string> SplitLines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// What one call to <see cref="ListAsync" /> or one action ran, collected so
    /// the pane can show it.
    ///
    /// <para>Passed from method to method by hand rather than parked in a field
    /// or an <c>AsyncLocal</c>. Nearly everything in this class is static and
    /// several of the callers run concurrently with each other, so an ambient
    /// collector would be a shared mutable that no call site mentions — the
    /// parameter is the point: a method that runs a process says so in its
    /// signature.</para>
    /// </summary>
    private sealed class CommandLog
    {
        /// <summary>How much of one command's output survives. A <c>git clone</c>
        /// prints progress by the screenful and this text goes straight into a
        /// pane, so the tail is dropped rather than handed to the renderer.</summary>
        private const int OutputLimit = 4000;

        private readonly List<CopilotToolCommand> _commands = [];

        public IReadOnlyList<CopilotToolCommand> Commands => _commands;

        public void Record(string fileName, IReadOnlyList<string> arguments, CommandResult result) =>
            _commands.Add(new CopilotToolCommand(Describe(fileName, arguments), result.ExitCode, Captured(result)));

        /// <summary>The command as something a reader could paste into a shell,
        /// which is the form they will want it in when they go to reproduce
        /// whatever just failed.</summary>
        private static string Describe(string fileName, IReadOnlyList<string> arguments) =>
            string.Join(' ', new[] { Quoted(fileName) }.Concat(arguments.Select(Quoted)));

        private static string Quoted(string value) =>
            value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

        private static string Captured(CommandResult result)
        {
            // Standard error after standard output rather than interleaved: the
            // two streams were read separately and their real ordering was lost
            // at that point, so pretending to restore it would be a fiction.
            var combined = string.Join(
                Environment.NewLine,
                new[] { result.Output, result.Error }
                    .Select(stream => stream.TrimEnd())
                    .Where(stream => stream.Length > 0));

            return combined.Length <= OutputLimit
                ? combined
                : combined[..OutputLimit] + $"{Environment.NewLine}... output truncated at {OutputLimit} characters.";
        }
    }

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^(?<name>\\S+)(?:\\s+(?<version>\\S+))?")]
    private static partial Regex PluginListLineRegex();
}
