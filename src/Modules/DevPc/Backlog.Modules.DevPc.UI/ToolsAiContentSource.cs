using System.Text;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.DevPc.UI;

/// <summary>
/// Dev PC Management's answer to <see cref="IAiContentSource"/>: the tool
/// catalog this machine is set up from, one line per tool.
/// </summary>
/// <remarks>
/// <para>
/// The catalog as <see cref="IDevToolService.ListAsync"/> answers it — every
/// plugin, MCP server, marketplace and application, with what is installed and
/// what is available — and one line naming the catalog document, so a question
/// about "where is this configured" has a path to answer with. Nothing about
/// which rail is open or which row is expanded: the pane's arrangement is not
/// what the machine is set up with.
/// </para>
/// <para>
/// No secrets, and no command output. A tool's row says its name, kind,
/// versions and state; the environment an MCP server is launched with, and the
/// output of the last install, are the two places a token could be sitting, and
/// neither is read here.
/// </para>
/// </remarks>
internal sealed class ToolsAiContentSource(IDevToolService tools) : IAiContentSource
{
    public string AreaKey => "tools";

    public string AreaTitle => "Tools";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var catalog = await tools.ListAsync(cancellationToken).ConfigureAwait(false);

        var records = new List<string>();

        // The document first: it is the one record that is about the catalog
        // rather than in it, and a reader asking where to edit something should
        // not have it ranked below the tools by word overlap.
        if (catalog.CatalogExists && !string.IsNullOrWhiteSpace(catalog.CatalogPath))
        {
            records.Add($"Catalog document: {catalog.CatalogPath}");
        }
        else if (!string.IsNullOrWhiteSpace(catalog.Message))
        {
            records.Add($"Catalog: {catalog.Message.Trim()}");
        }

        records.AddRange(catalog.Tools.Select(Text));

        return AiContentBudget.Compose(
            AreaKey,
            AreaTitle,
            records,
            record => record,
            request.Question,
            request.BudgetCharacters,
            pinned: record => record.StartsWith("Catalog", StringComparison.Ordinal));
    }

    /// <summary>"Tool: name — kind, group, installed version, available version, state."</summary>
    internal static string Text(DevToolInfo tool)
    {
        var text = new StringBuilder("Tool: ").Append(tool.Name.Trim());
        text.Append(" — ").Append(Kind(tool.Kind));

        if (!string.IsNullOrWhiteSpace(tool.Group)) text.Append(", ").Append(tool.Group.Trim());

        text.Append(tool.Installed
            ? $", installed {Version(tool.InstalledVersion)}"
            : ", not installed");

        if (!string.IsNullOrWhiteSpace(tool.AvailableVersion)) text.Append(", available ").Append(tool.AvailableVersion.Trim());
        text.Append(tool.ConfiguredEnabled ? ", enabled" : ", disabled");
        if (!string.IsNullOrWhiteSpace(tool.Status)) text.Append(", ").Append(tool.Status.Trim());

        return text.ToString();
    }

    private static string Version(string version) => string.IsNullOrWhiteSpace(version) ? "(version unknown)" : version.Trim();

    /// <summary>The kind as a person says it. Exhaustive without a catch-all,
    /// for the reason the project's own build settings give: a kind nobody has
    /// named here fails the build rather than reading as something else.</summary>
    private static string Kind(DevToolKind kind) => kind switch
    {
        DevToolKind.Plugin => "plugin",
        DevToolKind.McpServer => "MCP server",
        DevToolKind.Marketplace => "marketplace",
        DevToolKind.Application => "application"
    };
}
