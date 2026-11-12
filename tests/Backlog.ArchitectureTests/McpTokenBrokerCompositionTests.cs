namespace Backlog.ArchitectureTests;

/// <summary>
/// The MCP grant store and its token broker (local ADR 0052 §3) are the installed app's: the
/// desktop head composes and starts them, and the web harness composes neither (§9), so its
/// <c>/mcp</c> keeps no auth. A MAUI head cannot be composed in a test process, so this is a
/// text scan over the composition sources, the way <c>TaskSyncClientRegistrationTests</c>
/// guards the same heads — deliberately narrow: it reads names, not whether a line runs.
/// </summary>
public class McpTokenBrokerCompositionTests
{
    private static readonly string[] GrantTypes = ["McpTokenBroker", "McpGrantTokens", "FileMcpGrantStore", "IMcpGrantStore"];

    [Fact]
    public void The_web_harnesses_and_the_shared_desktop_composition_compose_no_grant_store_or_broker()
    {
        var folders = new[]
        {
            new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Harness")),
            new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop.Composition"))
        };

        var files = folders.SelectMany(folder => Repository.SourceFilesUnder(folder, ".cs", ".razor")).ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => GrantTypes.Any(type => File.ReadAllText(file.FullName).Contains(type, StringComparison.Ordinal)))
            .Select(Repository.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The MCP grant store and token broker belong to the desktop head alone (local ADR 0052 §9); "
            + "these files name them:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_desktop_head_composes_and_starts_the_broker()
    {
        var head = File.ReadAllText(Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop", "MauiProgram.cs"));

        Assert.Contains("<IMcpGrantStore>", head, StringComparison.Ordinal);
        Assert.Contains("new McpGrantTokens(", head, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<McpTokenBroker>().Start()", head, StringComparison.Ordinal);
    }
}
