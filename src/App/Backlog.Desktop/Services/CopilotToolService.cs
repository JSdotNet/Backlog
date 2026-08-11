using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Backlog.Desktop.UI.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.Services;

public sealed partial class CopilotToolService : ICopilotToolService
{
    private readonly CopilotToolConfigurationPaths _configPaths;
    private readonly ILogger<CopilotToolService>? _logger;

    public CopilotToolService(ILogger<CopilotToolService>? logger = null, string? configPath = null)
    {
        _logger = logger;
        _configPaths = configPath is null
            ? CopilotToolConfigurationPaths.CreateDefault()
            : CopilotToolConfigurationPaths.FromCatalogPath(configPath);
    }

    public async Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_configPaths.CatalogPath))
        {
            return new CopilotToolCatalog([], $"Tool catalog was not found at {_configPaths.CatalogPath}.");
        }

        var config = await CopilotToolConfiguration.ReadAsync(_configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var messages = new List<string>();
        var installedPlugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            installedPlugins = await GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to list Copilot plugins.");
            messages.Add("Copilot plugins could not be checked.");
        }

        try
        {
            installedTools = await GetInstalledDotNetToolsAsync(ct).ConfigureAwait(false);
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
            var installedVersion = GetPluginInstalledVersion(plugin, installedPlugins);
            var installed = !installedVersion.Equals("not installed", StringComparison.OrdinalIgnoreCase);
            var availableVersion = await GetPluginAvailableVersionAsync(plugin, ct).ConfigureAwait(false);

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
            var availableVersion = await GetDotNetToolAvailableVersionAsync(packageId, ct).ConfigureAwait(false);

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
        return new CopilotToolCatalog(tools, message);
    }

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => ApplyAsync(key, null, ct);

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, true, ct);

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, false, ct);

    private async Task<CopilotToolActionResult> ApplyAsync(string key, bool? enabled, CancellationToken ct)
    {
        if (!File.Exists(_configPaths.CatalogPath))
        {
            return CopilotToolActionResult.Failed($"Tool catalog was not found at {_configPaths.CatalogPath}.");
        }

        var config = await CopilotToolConfiguration.ReadAsync(_configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var node = FindToolNode(root, key);
        if (node is null)
        {
            return CopilotToolActionResult.Failed("That tool is no longer in the config.");
        }

        if (enabled is not null)
        {
            await CopilotToolConfiguration.WriteEnabledOverrideAsync(_configPaths, key, enabled.Value, ct).ConfigureAwait(false);
            root = (await CopilotToolConfiguration.ReadAsync(_configPaths, ct).ConfigureAwait(false)).Root;
            node = FindToolNode(root, key) ?? throw new InvalidOperationException("That tool is no longer in the config.");
        }

        try
        {
            return key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)
                ? await ApplyPluginAsync(node, ct).ConfigureAwait(false)
                : await ApplyMcpServerAsync(node, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Copilot tool action failed for {Key}.", key);
            return CopilotToolActionResult.Failed($"{ToolDisplayName(node)} could not be changed: {ex.Message}");
        }
    }

    private async Task<CopilotToolActionResult> ApplyPluginAsync(JsonNode plugin, CancellationToken ct)
    {
        var name = GetRequiredString(plugin, "name");
        var kind = GetString(plugin, "kind");
        var enabled = GetBool(plugin, "enabled");

        if (kind.Equals("repository-skills", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("repository-canvases", StringComparison.OrdinalIgnoreCase))
        {
            var status = enabled
                ? await RefreshRepositorySourceAsync(plugin, ct).ConfigureAwait(false)
                : "disabled in config";
            return CopilotToolActionResult.Ok($"{name}: {status}.");
        }

        var cli = ResolveCopilotCli();
        var installed = await GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        var isInstalled = installed.ContainsKey(name);

        if (!enabled)
        {
            if (!isInstalled)
            {
                return CopilotToolActionResult.Ok($"{name} is already disabled.");
            }

            var uninstall = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "uninstall", name], ct).ConfigureAwait(false);
            return uninstall.ExitCode == 0
                ? CopilotToolActionResult.Ok($"{name} was disabled.")
                : CopilotToolActionResult.Failed(CommandFailure(name, uninstall));
        }

        var args = isInstalled ? new[] { "plugin", "update", name } : ["plugin", "install", GetRequiredString(plugin, "source")];
        var result = await RunAsync(cli.Command, [.. cli.Prefix, .. args], ct).ConfigureAwait(false);
        return result.ExitCode == 0
            ? CopilotToolActionResult.Ok($"{name} was {(isInstalled ? "updated" : "enabled")}.")
            : CopilotToolActionResult.Failed(CommandFailure(name, result));
    }

    private async Task<CopilotToolActionResult> ApplyMcpServerAsync(JsonNode server, CancellationToken ct)
    {
        var packageId = GetRequiredString(server, "packageId");
        var enabled = GetBool(server, "enabled");
        var installed = await GetInstalledDotNetToolsAsync(ct).ConfigureAwait(false);
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

        var result = await RunAsync("dotnet", args, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? CopilotToolActionResult.Ok(success) : CopilotToolActionResult.Failed(CommandFailure(packageId, result));
    }

    private async Task<Dictionary<string, string>> GetInstalledPluginsAsync(CancellationToken ct)
    {
        var cli = ResolveCopilotCli();
        var result = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "list"], ct).ConfigureAwait(false);
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

    private static async Task<Dictionary<string, string>> GetInstalledDotNetToolsAsync(CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "list", "--global"], ct).ConfigureAwait(false);
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

    private static async Task<string> GetDotNetToolAvailableVersionAsync(string packageId, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "search", packageId, "--exact-match"], ct).ConfigureAwait(false);
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

    private static async Task<string> GetPluginAvailableVersionAsync(JsonNode plugin, CancellationToken ct)
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
                var result = await RunAsync("gh", ["release", "view", "--repo", $"{parts[0]}/{parts[1]}", "--json", "tagName", "--jq", ".tagName"], ct).ConfigureAwait(false);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()))
                {
                    return result.Output.Trim();
                }
            }
        }

        var repoPath = GetString(plugin, "repoPath");
        if (!string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath))
        {
            var result = await RunAsync("git", ["-C", repoPath, "rev-parse", "--short", "HEAD"], ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()))
            {
                return result.Output.Trim();
            }
        }

        return "unknown";
    }

    private static string GetPluginInstalledVersion(JsonNode plugin, IReadOnlyDictionary<string, string> installedPlugins)
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

        var result = RunAsync("git", ["-C", repoPath, "rev-parse", "--short", "HEAD"], CancellationToken.None).GetAwaiter().GetResult();
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output.Trim()) ? result.Output.Trim() : "source";
    }

    private static async Task<string> RefreshRepositorySourceAsync(JsonNode plugin, CancellationToken ct)
    {
        var source = GetRequiredString(plugin, "source");
        var repoPath = ResolveConfiguredPath(GetRequiredString(plugin, "repoPath"));
        if (Directory.Exists(repoPath))
        {
            var pull = await RunAsync("git", ["-C", repoPath, "pull", "--ff-only"], ct).ConfigureAwait(false);
            return pull.ExitCode == 0 ? "source refreshed" : CommandFailure(ToolDisplayName(plugin), pull);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repoPath) ?? Environment.CurrentDirectory);
        var clone = await RunAsync("git", ["clone", source, repoPath], ct).ConfigureAwait(false);
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

    private static (string Command, string[] Prefix) ResolveCopilotCli()
    {
        var copilot = RunAsync("copilot", ["--version"], CancellationToken.None).GetAwaiter().GetResult();
        return copilot.ExitCode == 0 ? ("copilot", []) : ("gh", ["copilot"]);
    }

    private static async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
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
        return new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static string CommandFailure(string name, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(details) ? $"{name} failed with exit code {result.ExitCode}." : details.Trim();
    }

    private static string ResolveConfiguredPath(string path) => Environment.ExpandEnvironmentVariables(path);

    private static IEnumerable<string> SplitLines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("^(?<name>\\S+)(?:\\s+(?<version>\\S+))?")]
    private static partial Regex PluginListLineRegex();
}
