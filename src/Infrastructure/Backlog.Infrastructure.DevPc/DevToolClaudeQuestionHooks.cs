using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.DevPc;

/// <summary>
/// The question relay's five hooks (local ADR 0041), installed beside the user-scope
/// <c>backlog</c> MCP server registration in Claude's <c>settings.json</c>.
///
/// <para>The hooks go wherever the server is registered and nowhere else, so a session
/// that cannot reach Backlog never runs a hook that asks it. They are installed at user
/// scope only, by this class, driven by the Dev PC catalog's <c>backlog</c> row. This
/// repository's <c>.mcp.json</c> registration installs none: user-scope hooks already fire
/// in it, and repository entries would fire for every contributor, CI run and sub-agent,
/// failing wherever no release Backlog is installed.</para>
///
/// <para>The file is the person's whole Claude configuration — permissions, plugins, their
/// own hooks — so, like <see cref="DevToolClaudeDesktopConfig"/>, everything here edits the
/// document it read and puts back only what it owns. An entry is ours when its command is
/// one of the five commands below, byte for byte; nothing else is touched.</para>
/// </summary>
public static class DevToolClaudeQuestionHooks
{
    /// <summary>The Claude server name whose registration carries the hooks.</summary>
    public const string ServerName = "backlog";

    /// <summary>Where the hooks live in <c>settings.json</c>.</summary>
    public const string HooksPropertyName = "hooks";

    /// <summary>The script as the desktop app installs it. A release build writes it to
    /// <c>%LOCALAPPDATA%\Backlog\hooks</c> on every start; the path is spelled with
    /// <c>$LOCALAPPDATA</c> because Claude Code runs hook commands through a shell that
    /// expands it.</summary>
    public const string ScriptPath = "$LOCALAPPDATA/Backlog/hooks/ask-backlog.mjs";

    /// <summary>One hook: the event, its matcher (or none), the script's mode and the
    /// timeout in seconds.</summary>
    public sealed record Entry(string Event, string? Matcher, string Mode, int Timeout)
    {
        /// <summary>The command line the hook runs.</summary>
        public string Command => $"node \"{ScriptPath}\" {Mode}";
    }

    /// <summary>The five hooks. <c>pre</c> holds a dialog for as long as the longest wait
    /// window (8 hours); every other mode returns at once.</summary>
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("PreToolUse", "AskUserQuestion", "pre", 28800),
        new("PostToolUse", "AskUserQuestion", "post", 10),
        new("Stop", null, "stop", 10),
        new("UserPromptSubmit", null, "prompt", 10),
        new("SessionEnd", null, "end", 10)
    ];

    /// <summary>Whether a Claude server name is the one the hooks travel with.</summary>
    public static bool IsBacklogServer(string? name) =>
        string.Equals(name, ServerName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds each of the five hooks the document does not already carry, and corrects the
    /// timeout of one it carries with a different one. Answers whether anything changed, so
    /// a rerun writes nothing.
    /// </summary>
    public static bool Merge(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root[HooksPropertyName] is not JsonObject hooks)
        {
            if (root[HooksPropertyName] is not null)
            {
                throw new InvalidOperationException($"'{HooksPropertyName}' is not an object, so the question hooks cannot be added to it.");
            }

            hooks = [];
            root[HooksPropertyName] = hooks;
        }

        var changed = false;
        foreach (var entry in Entries)
        {
            if (hooks[entry.Event] is not JsonArray groups)
            {
                if (hooks[entry.Event] is not null)
                {
                    throw new InvalidOperationException($"'{HooksPropertyName}.{entry.Event}' is not an array, so the question hook cannot be added to it.");
                }

                groups = [];
                hooks[entry.Event] = groups;
            }

            if (FindOurs(groups, entry) is { } existing)
            {
                if (existing["timeout"] is not JsonValue timeout || !timeout.TryGetValue<int>(out var seconds) || seconds != entry.Timeout)
                {
                    existing["timeout"] = entry.Timeout;
                    changed = true;
                }

                continue;
            }

            var group = new JsonObject();
            if (entry.Matcher is { } matcher)
            {
                group["matcher"] = matcher;
            }

            group["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = entry.Command,
                ["timeout"] = entry.Timeout
            });
            groups.Add(group);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Takes exactly the five hooks out of the document. A matcher group left with no hook
    /// goes with them, as does an event left with no group and a <c>hooks</c> object left
    /// with no event — each of those was only there to hold ours. Everything else stays.
    /// Answers whether anything changed.
    /// </summary>
    public static bool Remove(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root[HooksPropertyName] is not JsonObject hooks)
        {
            return false;
        }

        var changed = false;
        foreach (var entry in Entries)
        {
            if (hooks[entry.Event] is not JsonArray groups)
            {
                continue;
            }

            var removedHere = false;
            for (var g = groups.Count - 1; g >= 0; g--)
            {
                if (groups[g] is not JsonObject group || group["hooks"] is not JsonArray handlers)
                {
                    continue;
                }

                for (var h = handlers.Count - 1; h >= 0; h--)
                {
                    if (IsOurs(handlers[h], entry))
                    {
                        handlers.RemoveAt(h);
                        removedHere = true;

                        if (handlers.Count == 0)
                        {
                            groups.RemoveAt(g);
                        }
                    }
                }
            }

            if (removedHere && groups.Count == 0)
            {
                hooks.Remove(entry.Event);
            }

            changed |= removedHere;
        }

        if (changed && hooks.Count == 0)
        {
            root.Remove(HooksPropertyName);
        }

        return changed;
    }

    /// <summary>
    /// Installs or removes the hooks in the <c>settings.json</c> at <paramref name="path"/>,
    /// and answers what happened in a sentence the row reports. A file that does not parse
    /// is refused rather than replaced; an absent file is an empty one, and removing from it
    /// writes nothing.
    /// </summary>
    public static async Task<DevToolActionResult> ApplyAsync(string path, bool install, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                root = string.IsNullOrWhiteSpace(text)
                    ? []
                    : JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                        ?? throw new JsonException("the document is not a JSON object");
            }
            catch (JsonException ex)
            {
                return DevToolActionResult.Failed($"question hooks: {path} could not be read ({ex.Message}), so it was left as it is.");
            }
        }
        else
        {
            if (!install)
            {
                return DevToolActionResult.Ok("question hooks were already absent.");
            }

            root = [];
        }

        bool changed;
        try
        {
            changed = install ? Merge(root) : Remove(root);
        }
        catch (InvalidOperationException ex)
        {
            return DevToolActionResult.Failed($"question hooks: {ex.Message} {path} was left as it is.");
        }

        if (!changed)
        {
            return DevToolActionResult.Ok(install ? "question hooks were already installed." : "question hooks were already absent.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);

        // Through a temp file, as the desktop config is written: Claude may be reading this
        // file, and a serialise straight into it truncates first.
        var tempPath = path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, root, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);

        return DevToolActionResult.Ok(install
            ? $"question hooks were installed in {path}."
            : $"question hooks were removed from {path}.");
    }

    private static JsonObject? FindOurs(JsonArray groups, Entry entry)
    {
        foreach (var group in groups)
        {
            if (group?["hooks"] is not JsonArray handlers)
            {
                continue;
            }

            foreach (var handler in handlers)
            {
                if (IsOurs(handler, entry))
                {
                    return (JsonObject)handler!;
                }
            }
        }

        return null;
    }

    private static bool IsOurs(JsonNode? handler, Entry entry) =>
        handler is JsonObject candidate
        && candidate["command"] is JsonValue command
        && command.TryGetValue<string>(out var text)
        && string.Equals(text, entry.Command, StringComparison.Ordinal);

    /// <summary>Indented: a file people open and edit by hand.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
