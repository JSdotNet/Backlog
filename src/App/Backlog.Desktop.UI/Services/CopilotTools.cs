using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Backlog.Desktop.UI.Services;

public enum CopilotToolKind
{
    Plugin,
    McpServer
}

public enum CopilotToolAction
{
    Update,
    Enable,
    Disable
}

public sealed record CopilotToolInfo(
    string Key,
    CopilotToolKind Kind,
    string Name,
    string? Source,
    bool ConfiguredEnabled,
    bool Installed,
    string InstalledVersion,
    string AvailableVersion,
    string Status)
{
    public bool UpdateAvailable => VersionDiffers(InstalledVersion, AvailableVersion);

    public static bool VersionDiffers(string installedVersion, string availableVersion)
    {
        var installed = NormalizeVersion(installedVersion);
        var available = NormalizeVersion(availableVersion);

        return installed is not null
            && available is not null
            && !string.Equals(installed, available, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("not installed", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.StartsWith('v') ? trimmed[1..] : trimmed;
    }
}

public sealed record CopilotToolCatalog(IReadOnlyList<CopilotToolInfo> Tools, string Message);

public sealed record CopilotToolActionResult(bool Succeeded, string Message)
{
    public static CopilotToolActionResult Ok(string message) => new(true, message);

    public static CopilotToolActionResult Failed(string message) => new(false, message);
}

public interface ICopilotToolService
{
    Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default);

    Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default);

    Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default);
}

public sealed class UnsupportedCopilotToolService : ICopilotToolService
{
    private const string Message = "Copilot tool management is only available in the desktop app.";

    public Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(new CopilotToolCatalog([], Message));

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed(Message));
}

