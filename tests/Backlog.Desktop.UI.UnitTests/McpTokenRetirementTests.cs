using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shared MCP token's retirement on the first start after the upgrade (local ADR 0052,
/// Phase 1): three grants in its place, recorded by id; Claude Code's own files rewritten where
/// they still carry a retired token, and only there; the retired tokens dropped from
/// <c>settings.json</c> once that is done.
/// </summary>
public sealed class McpTokenRetirementTests : IDisposable
{
    private const string Helper = @"C:\Users\me\AppData\Local\Backlog\hooks\backlog-mcp-headers.mjs";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-token-retirement-" + Guid.NewGuid().ToString("N"));

    public McpTokenRetirementTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string AppData => Path.Combine(_root, "Backlog");

    private ClaudeConfigFiles Files => new(Path.Combine(_root, ".claude.json"), Path.Combine(_root, ".claude", "settings.json"));

    private FileMcpGrantStore Grants() => new(Path.Combine(AppData, FileMcpGrantStore.FileName));

    /// <summary>A settings store over <see cref="AppData"/>, its settings.json first holding
    /// <paramref name="json"/> when one is given.</summary>
    private WorkspaceSettingsStore Settings(string? json = null)
    {
        Directory.CreateDirectory(AppData);
        if (json is not null) File.WriteAllText(Path.Combine(AppData, "settings.json"), json);
        return new WorkspaceSettingsStore(AppData, Path.Combine(AppData, "settings.json"), _ => null);
    }

    private static string Upgraded => """
        { "mcpServer": { "token": "shared-token", "hookToken": "hook-token", "telemetryToken": "telemetry-token" } }
        """;

    private void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static JsonObject Read(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    [Fact]
    public void The_first_start_makes_the_three_grants_and_records_them()
    {
        var settings = Settings(Upgraded);
        var grants = Grants();

        var made = McpTokenRetirement.EnsureGrants(settings, grants);

        var delivery = grants.Find(made.Delivery)!;
        Assert.Equal("Backlog delivery", delivery.Name);
        Assert.Equal(McpTokenRetirement.DeliveryScopes, delivery.Scopes);
        Assert.Contains(McpScopes.RunsWrite, delivery.Scopes);
        Assert.Equal(["notify:ask"], grants.Find(made.QuestionHook)!.Scopes);
        Assert.Equal("Question hook", grants.Find(made.QuestionHook)!.Name);
        Assert.Equal(["telemetry:write"], grants.Find(made.Telemetry)!.Scopes);
        Assert.Equal("Telemetry", grants.Find(made.Telemetry)!.Name);

        // Recorded, so the next start reads them back instead of making three more.
        Assert.Equal(made, Settings().McpServerGrants);
        Assert.Equal(made, McpTokenRetirement.EnsureGrants(Settings(), Grants()));
        Assert.Equal(3, Grants().List().Count);
    }

    /// <summary>A grant the person revoked stays revoked: the migration never makes it again.</summary>
    [Fact]
    public void A_revoked_grant_is_not_made_again()
    {
        var made = McpTokenRetirement.EnsureGrants(Settings(Upgraded), Grants());
        Grants().Revoke(made.Telemetry);

        var again = McpTokenRetirement.EnsureGrants(Settings(), Grants());

        Assert.Equal(made, again);
        Assert.True(Grants().Find(made.Telemetry)!.Revoked);
        Assert.Equal(3, Grants().List().Count);
    }

    /// <summary>
    /// The registrations the app installed carry the retired token as a literal header: each
    /// such <c>backlog</c> server — user scope and project scope — now runs the installed headers
    /// helper for the delivery grant. A <c>backlog</c> server carrying any other bearer, and every
    /// other server and key, is left exactly as it was.
    /// </summary>
    [Fact]
    public void Registrations_carrying_the_retired_token_run_the_headers_helper_for_the_delivery_grant()
    {
        Write(Files.UserConfigPath, """
            {
              "numStartups": 12,
              "mcpServers": {
                "backlog": { "type": "http", "url": "http://127.0.0.1:5757/mcp",
                             "headers": { "Authorization": "Bearer shared-token", "X-Other": "kept" } },
                "github": { "type": "http", "url": "https://api.example/mcp", "headers": { "Authorization": "Bearer ghp_secret" } }
              },
              "projects": {
                "D:/Repos/One": { "mcpServers": { "backlog": { "type": "http", "url": "http://127.0.0.1:5757/mcp",
                                                                "headers": { "authorization": "Bearer shared-token" } } } },
                "D:/Repos/Two": { "mcpServers": { "backlog": { "type": "http", "url": "http://127.0.0.1:5757/mcp",
                                                                "headers": { "Authorization": "Bearer someone-elses" } } } }
              }
            }
            """);
        var settings = Settings(Upgraded);

        var result = McpTokenRetirement.Run(settings, Grants(), Files, Helper, debug: false);

        Assert.Empty(result.Problems);
        Assert.Equal(2, result.RegistrationsRewritten);

        var command = $"node \"C:/Users/me/AppData/Local/Backlog/hooks/backlog-mcp-headers.mjs\" --grant {result.Grants.Delivery}";
        var config = Read(Files.UserConfigPath);
        var user = config["mcpServers"]!["backlog"]!.AsObject();
        Assert.Equal(command, user["headersHelper"]!.GetValue<string>());
        Assert.Equal("kept", user["headers"]!["X-Other"]!.GetValue<string>());
        Assert.Null(user["headers"]!["Authorization"]);

        var one = config["projects"]!["D:/Repos/One"]!["mcpServers"]!["backlog"]!.AsObject();
        Assert.Equal(command, one["headersHelper"]!.GetValue<string>());
        Assert.False(one.ContainsKey("headers"));

        var two = config["projects"]!["D:/Repos/Two"]!["mcpServers"]!["backlog"]!.AsObject();
        Assert.False(two.ContainsKey("headersHelper"));
        Assert.Equal("Bearer someone-elses", two["headers"]!["Authorization"]!.GetValue<string>());

        Assert.Equal("Bearer ghp_secret", config["mcpServers"]!["github"]!["headers"]!["Authorization"]!.GetValue<string>());
        Assert.Equal(12, config["numStartups"]!.GetValue<int>());
    }

    /// <summary>The telemetry block the Settings page had a person paste carried the telemetry
    /// token as an OTLP header: it now runs the headers helper for the Telemetry grant, refreshed
    /// under the token's 15-minute life, and the rest of the file is kept.</summary>
    [Fact]
    public void A_telemetry_block_carrying_the_retired_token_runs_the_headers_helper_for_the_telemetry_grant()
    {
        Write(Files.UserSettingsPath, """
            {
              "permissions": { "allow": ["Bash(git status)"] },
              "env": {
                "CLAUDE_CODE_ENABLE_TELEMETRY": "1",
                "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT": "http://127.0.0.1:5757/v1/logs",
                "OTEL_EXPORTER_OTLP_LOGS_HEADERS": "Authorization=Bearer telemetry-token",
                "BACKLOG_MCP_TOKEN": "shared-token"
              }
            }
            """);

        var result = McpTokenRetirement.Run(Settings(Upgraded), Grants(), Files, Helper, debug: true);

        Assert.Empty(result.Problems);
        Assert.True(result.TelemetryRewritten);

        var settings = Read(Files.UserSettingsPath);
        Assert.Equal(
            $"node \"C:/Users/me/AppData/Local/Backlog/hooks/backlog-mcp-headers.mjs\" --grant {result.Grants.Telemetry} --debug",
            settings["otelHeadersHelper"]!.GetValue<string>());
        var env = settings["env"]!.AsObject();
        Assert.False(env.ContainsKey("OTEL_EXPORTER_OTLP_LOGS_HEADERS"));
        // The retired shared token a person copied into the environment block goes too.
        Assert.False(env.ContainsKey("BACKLOG_MCP_TOKEN"));
        Assert.Equal("600000", env["CLAUDE_CODE_OTEL_HEADERS_HELPER_DEBOUNCE_MS"]!.GetValue<string>());
        Assert.Equal("http://127.0.0.1:5757/v1/logs", env["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"]!.GetValue<string>());
        Assert.Equal("Bash(git status)", settings["permissions"]!["allow"]![0]!.GetValue<string>());
    }

    /// <summary>Once both files are done the retired tokens leave settings.json, and a second
    /// start changes nothing: no file is written and no grant is made.</summary>
    [Fact]
    public void The_retired_tokens_are_dropped_once_and_a_second_start_changes_nothing()
    {
        Write(Files.UserConfigPath, """{ "mcpServers": { "backlog": { "headers": { "Authorization": "Bearer shared-token" } } } }""");

        var first = McpTokenRetirement.Run(Settings(Upgraded), Grants(), Files, Helper, debug: false);
        Assert.Empty(first.Problems);

        var settingsText = File.ReadAllText(Path.Combine(AppData, "settings.json"));
        Assert.DoesNotContain("shared-token", settingsText, StringComparison.Ordinal);
        Assert.DoesNotContain("hook-token", settingsText, StringComparison.Ordinal);
        Assert.DoesNotContain("telemetry-token", settingsText, StringComparison.Ordinal);
        Assert.Empty(Settings().RetiredMcpTokens);

        var written = File.GetLastWriteTimeUtc(Files.UserConfigPath);
        var second = McpTokenRetirement.Run(Settings(), Grants(), Files, Helper, debug: false);

        Assert.Equal(first.Grants, second.Grants);
        Assert.Equal(0, second.RegistrationsRewritten);
        Assert.False(second.TelemetryRewritten);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Files.UserConfigPath));
        Assert.Equal(3, Grants().List().Count);
    }

    /// <summary>A fresh install — no retired token, no Claude files — still gets its grants, so
    /// the question hook the Dev PC row installs later has one to ask for.</summary>
    [Fact]
    public void A_fresh_install_gets_its_grants_and_touches_no_file()
    {
        var result = McpTokenRetirement.Run(Settings(), Grants(), Files, Helper, debug: false);

        Assert.Empty(result.Problems);
        Assert.NotNull(Settings().McpServerGrants);
        Assert.False(File.Exists(Files.UserConfigPath));
        Assert.False(File.Exists(Files.UserSettingsPath));
    }

    /// <summary>A Claude file that does not parse is left alone and reported, and the retired
    /// tokens stay recorded so the next start tries again.</summary>
    [Fact]
    public void A_file_that_does_not_parse_keeps_the_retired_tokens_for_the_next_start()
    {
        Write(Files.UserConfigPath, "{ not json");

        var result = McpTokenRetirement.Run(Settings(Upgraded), Grants(), Files, Helper, debug: false);

        Assert.Contains(result.Problems, problem => problem.Contains(".claude.json", StringComparison.Ordinal));
        Assert.Equal("{ not json", File.ReadAllText(Files.UserConfigPath));
        Assert.Equal(["shared-token", "hook-token", "telemetry-token"], Settings().RetiredMcpTokens);
    }

    /// <summary>The delivery grant serves the delivery flows' review notes, the windows they open
    /// and the Manager session's dispatch, so it carries those three beyond the Delivery flow preset.</summary>
    [Fact]
    public void The_delivery_grant_carries_devbook_write_ui_open_and_sessions_dispatch()
    {
        var grants = Grants();

        var made = McpTokenRetirement.EnsureGrants(Settings(Upgraded), grants);

        var scopes = grants.Find(made.Delivery)!.Scopes;
        Assert.Contains(McpScopes.DevbookWrite, scopes);
        Assert.Contains(McpScopes.UiOpen, scopes);
        Assert.Contains(McpScopes.SessionsDispatch, scopes);
        Assert.Superset(McpTokenRetirement.DeliveryPreset.ToHashSet(), scopes.ToHashSet());
        Assert.Equal(scopes.Count, scopes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A delivery grant entry 9 already made holds the bare preset: the next start gives
    /// it the three scopes once, raising its version so a token minted under the old scopes is
    /// refused, and a start after that changes nothing.</summary>
    [Fact]
    public void An_already_migrated_delivery_grant_is_topped_up_once_on_the_next_start()
    {
        var made = MigratedBeforeTheAdditions();
        var before = Grants().Find(made.Delivery)!;

        var again = McpTokenRetirement.EnsureGrants(Settings(), Grants());

        Assert.Equal(made, again);
        var after = Grants().Find(made.Delivery)!;
        Assert.Equal(McpTokenRetirement.DeliveryScopes.ToHashSet(), after.Scopes.ToHashSet());
        Assert.True(after.Version > before.Version);

        _ = McpTokenRetirement.EnsureGrants(Settings(), Grants());
        Assert.Equal(after.Version, Grants().Find(made.Delivery)!.Version);
        Assert.Equal(3, Grants().List().Count);
    }

    /// <summary>A delivery grant whose scopes the person changed on the Agents card keeps their
    /// choice, and a revoked one stays as it was revoked.</summary>
    [Fact]
    public void A_delivery_grant_the_person_changed_or_revoked_is_not_topped_up()
    {
        var made = MigratedBeforeTheAdditions();
        var edited = Grants().SetScopes(made.Delivery, [McpScopes.TasksRead])!;

        _ = McpTokenRetirement.EnsureGrants(Settings(), Grants());

        Assert.Equal([McpScopes.TasksRead], Grants().Find(made.Delivery)!.Scopes);
        Assert.Equal(edited.Version, Grants().Find(made.Delivery)!.Version);

        _ = Grants().SetScopes(made.Delivery, McpTokenRetirement.DeliveryPreset);
        var revoked = Grants().Revoke(made.Delivery)!;

        _ = McpTokenRetirement.EnsureGrants(Settings(), Grants());

        var kept = Grants().Find(made.Delivery)!;
        Assert.True(kept.Revoked);
        Assert.Equal(revoked.Version, kept.Version);
        Assert.DoesNotContain(McpScopes.SessionsDispatch, kept.Scopes);
    }

    /// <summary>
    /// Claude Code writes <c>~/.claude.json</c> on its own while the app rewrites it. A write that
    /// lands between the app's read and its replace is kept: the file is read again before the
    /// replace, the edit is made again on what is there now, and no temporary file is left behind.
    /// </summary>
    [Fact]
    public void A_write_made_while_the_file_is_rewritten_is_kept()
    {
        Write(Files.UserConfigPath, """{ "numStartups": 1, "mcpServers": { "backlog": { "headers": { "Authorization": "Bearer shared-token" } } } }""");
        var problems = new List<string>();
        var concurrent = 0;

        McpTokenRetirement.Edit(
            Files.UserConfigPath,
            root => McpTokenRetirement.RewriteRegistrations(root, ["shared-token"], "helper") > 0,
            problems,
            beforeReplace: () =>
            {
                if (concurrent++ > 0) return;
                File.WriteAllText(Files.UserConfigPath, """{ "numStartups": 2, "tipsHistory": { "x": 1 }, "mcpServers": { "backlog": { "headers": { "Authorization": "Bearer shared-token" } } } }""");
            });

        Assert.Empty(problems);
        var config = Read(Files.UserConfigPath);
        Assert.Equal(2, config["numStartups"]!.GetValue<int>());
        Assert.Equal(1, config["tipsHistory"]!["x"]!.GetValue<int>());
        Assert.Equal("helper", config["mcpServers"]!["backlog"]!["headersHelper"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(_root, ".claude.json*"));
    }

    /// <summary>A file that changes under every attempt is left as its other writer last wrote it
    /// and reported, so the retired tokens stay recorded and the next start tries again.</summary>
    [Fact]
    public void A_file_that_keeps_changing_is_left_for_the_next_start()
    {
        Write(Files.UserConfigPath, """{ "mcpServers": { "backlog": { "headers": { "Authorization": "Bearer shared-token" } } } }""");
        var problems = new List<string>();
        var writes = 0;
        string Latest() => $$"""{ "numStartups": {{writes}}, "mcpServers": { "backlog": { "headers": { "Authorization": "Bearer shared-token" } } } }""";

        McpTokenRetirement.Edit(
            Files.UserConfigPath,
            root => McpTokenRetirement.RewriteRegistrations(root, ["shared-token"], "helper") > 0,
            problems,
            beforeReplace: () =>
            {
                writes++;
                File.WriteAllText(Files.UserConfigPath, Latest());
            });

        Assert.Equal(McpTokenRetirement.EditAttempts, writes);
        Assert.Contains(problems, problem => problem.Contains("kept changing", StringComparison.Ordinal));
        Assert.Equal(Latest(), File.ReadAllText(Files.UserConfigPath));
        Assert.Single(Directory.GetFiles(_root, ".claude.json*"));
    }

    /// <summary>The state entry 9 left: grants recorded, the delivery grant holding the bare
    /// Delivery flow preset it was made with then.</summary>
    private McpInstalledGrants MigratedBeforeTheAdditions()
    {
        var grants = Grants();
        var made = new McpInstalledGrants(
            grants.Create(McpTokenRetirement.DeliveryGrantName, McpTokenRetirement.DeliveryPreset, McpTokenRetirement.CreatedBy).Id,
            grants.Create(McpTokenRetirement.QuestionHookGrantName, [McpScopes.NotifyAsk], McpTokenRetirement.CreatedBy).Id,
            grants.Create(McpTokenRetirement.TelemetryGrantName, [McpScopes.TelemetryWrite], McpTokenRetirement.CreatedBy).Id);
        _ = Settings().SetMcpServerGrants(made);
        return made;
    }
}
