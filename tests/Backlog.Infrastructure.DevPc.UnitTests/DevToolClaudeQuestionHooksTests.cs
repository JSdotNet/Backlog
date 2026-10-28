using System.Text.Json.Nodes;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The question relay's hooks in Claude's <c>settings.json</c> (local ADR 0041): merged beside
/// what the person has, removed leaving everything else, and a rerun changing nothing. Every
/// test writes a temp file; the real <c>~/.claude/settings.json</c> is never touched.
/// </summary>
public sealed class DevToolClaudeQuestionHooksTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "backlog-question-hooks-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private const string PersonalSettings = """
        {
          "permissions": { "allow": ["Bash(git status)"] },
          "hooks": {
            "PreToolUse": [
              { "matcher": "Bash", "hooks": [ { "type": "command", "command": "node my-guard.mjs", "timeout": 5 } ] }
            ],
            "Stop": [
              { "hooks": [ { "type": "command", "command": "say done" } ] }
            ],
            "Notification": [
              { "hooks": [ { "type": "command", "command": "notify-send claude" } ] }
            ]
          }
        }
        """;

    [Fact]
    public async Task Install_merges_the_five_hooks_beside_the_ones_already_there()
    {
        await WriteAsync(PersonalSettings);

        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        var root = await ReadAsync();
        var hooks = root["hooks"]!.AsObject();

        Assert.Equal(["Bash(git status)"], root["permissions"]!["allow"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains(Commands(hooks, "PreToolUse"), c => c == "node my-guard.mjs");
        Assert.Contains(Commands(hooks, "Stop"), c => c == "say done");
        Assert.Equal(["notify-send claude"], Commands(hooks, "Notification"));

        foreach (var entry in DevToolClaudeQuestionHooks.Entries)
        {
            var group = hooks[entry.Event]!.AsArray().Single(g => g!["hooks"]!.AsArray().Any(h => h!["command"]!.GetValue<string>() == entry.Command))!;
            Assert.Equal(entry.Matcher, group["matcher"]?.GetValue<string>());
            var handler = group["hooks"]!.AsArray().Single()!;
            Assert.Equal("command", handler["type"]!.GetValue<string>());
            Assert.Equal(entry.Timeout, handler["timeout"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Entries_are_the_five_the_relay_specifies()
    {
        Assert.Equal(
            [
                ("PreToolUse", "AskUserQuestion", "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" pre", 28800),
                ("PostToolUse", "AskUserQuestion", "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" post", 10),
                ("Stop", null, "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" stop", 10),
                ("UserPromptSubmit", null, "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" prompt", 10),
                ("SessionEnd", null, "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" end", 10)
            ],
            DevToolClaudeQuestionHooks.Entries.Select(e => (e.Event, e.Matcher, e.Command, e.Timeout)));
    }

    [Fact]
    public async Task Install_into_an_absent_file_creates_it()
    {
        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        var hooks = (await ReadAsync())["hooks"]!.AsObject();
        Assert.Equal(DevToolClaudeQuestionHooks.Entries.Select(e => e.Event), hooks.Select(p => p.Key));
    }

    [Fact]
    public async Task A_rerun_changes_nothing()
    {
        await WriteAsync(PersonalSettings);
        await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);
        var first = await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken);
        var written = File.GetLastWriteTimeUtc(SettingsPath);
        File.SetLastWriteTimeUtc(SettingsPath, written.AddMinutes(-5));

        var rerun = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);

        Assert.True(rerun.Succeeded, rerun.Message);
        Assert.Contains("already installed", rerun.Message);
        Assert.Equal(first, await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
        Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(SettingsPath));
    }

    [Fact]
    public void Merge_corrects_a_changed_timeout_without_adding_a_second_entry()
    {
        var root = new JsonObject();
        DevToolClaudeQuestionHooks.Merge(root);
        var pre = root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!;
        pre["timeout"] = 600;

        Assert.True(DevToolClaudeQuestionHooks.Merge(root));
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
        Assert.Equal(28800, root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["timeout"]!.GetValue<int>());
        Assert.False(DevToolClaudeQuestionHooks.Merge(root));
    }

    [Fact]
    public async Task Removal_takes_exactly_the_five_and_leaves_other_hooks_untouched()
    {
        await WriteAsync(PersonalSettings);
        var before = JsonNode.Parse(PersonalSettings)!;
        await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);

        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: false, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(JsonNode.DeepEquals(before, await ReadAsync()), (await ReadAsync()).ToJsonString());
    }

    [Fact]
    public async Task Removal_keeps_a_personal_hook_sharing_a_group_with_ours()
    {
        await WriteAsync("""
            {
              "hooks": {
                "Stop": [
                  { "hooks": [
                    { "type": "command", "command": "say done" },
                    { "type": "command", "command": "node \"$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs\" stop", "timeout": 10 }
                  ] }
                ]
              }
            }
            """);

        await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: false, TestContext.Current.CancellationToken);

        Assert.Equal(["say done"], Commands((await ReadAsync())["hooks"]!.AsObject(), "Stop"));
    }

    [Fact]
    public async Task Removal_from_a_file_without_them_writes_nothing()
    {
        await WriteAsync(PersonalSettings);

        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: false, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("already absent", result.Message);
        Assert.Equal(PersonalSettings, await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Removal_with_no_file_creates_none()
    {
        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: false, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task A_file_that_does_not_parse_is_refused_and_left_as_it_is()
    {
        const string broken = "{ \"hooks\": ";
        await WriteAsync(broken);

        var result = await DevToolClaudeQuestionHooks.ApplyAsync(SettingsPath, install: true, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(broken, await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("backlog", true)]
    [InlineData("Backlog", true)]
    [InlineData("microsoft-learn", false)]
    [InlineData(null, false)]
    public void Only_the_backlog_server_carries_the_hooks(string? name, bool expected) =>
        Assert.Equal(expected, DevToolClaudeQuestionHooks.IsBacklogServer(name));

    [Fact]
    public async Task This_repository_lists_none_of_the_hooks_beside_its_mcp_registration()
    {
        var root = RepositoryRoot();
        var mcp = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".mcp.json"), TestContext.Current.CancellationToken))!;
        Assert.NotNull(mcp["mcpServers"]![DevToolClaudeQuestionHooks.ServerName]);

        // The hooks live at user scope only: repository entries would run each hook twice
        // and fire for every contributor, CI run and sub-agent, with or without Backlog.
        var text = await File.ReadAllTextAsync(Path.Combine(root, ".claude", "settings.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("ask-backlog.mjs", text);

        // Removing ours from the file finds nothing to remove.
        var settings = JsonNode.Parse(text)!.AsObject();
        Assert.False(DevToolClaudeQuestionHooks.Remove(settings));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task WriteAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, json, TestContext.Current.CancellationToken);
    }

    private async Task<JsonObject> ReadAsync() =>
        JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken))!.AsObject();

    private static string[] Commands(JsonObject hooks, string eventName) =>
        [.. hooks[eventName]!.AsArray().SelectMany(g => g!["hooks"]!.AsArray()).Select(h => h!["command"]!.GetValue<string>())];

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Backlog.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
