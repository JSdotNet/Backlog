using System.Text.Json.Nodes;
using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.DevPc;

/// <summary>
/// The Claude desktop app's own MCP server list, which is a file and not a CLI.
///
/// <para>The MSIX package declares no execution alias, so nothing of Claude
/// Desktop's is ever on PATH, and the app's internal update channel can only
/// change a server that is already registered — it structurally cannot add one.
/// Editing <c>claude_desktop_config.json</c> is the whole mechanism.</para>
///
/// <para>Which makes this the one host whose registration is a document merge,
/// and the one whose failure mode is losing something. The file is the app's
/// entire settings store — preferences, the global shortcut, the Cowork files
/// path, feature switches — so a writer that serialises its own idea of the
/// document wipes all of it. Everything here reads the document it was given and
/// puts one property back.</para>
/// </summary>
public static class DevToolClaudeDesktopConfig
{
    /// <summary>The file every path candidate below ends in.</summary>
    public const string FileName = "claude_desktop_config.json";

    /// <summary>Where the servers live in it.</summary>
    public const string ServersPropertyName = "mcpServers";

    /// <summary>What has to happen before a change here is live.
    ///
    /// <para>The app reads this file once at startup and memoises it, with no
    /// watch on it. Closing the window is not enough — the process stays in the
    /// tray holding the old list — so a row that reported the server as running
    /// after a write would be wrong until the person happened to reboot.</para></summary>
    public const string RestartRequired = "Quit Claude Desktop from the tray and relaunch it for this to take effect.";

    /// <summary>
    /// Where the config might be, best first.
    ///
    /// <para>Three, because the app has moved: the roaming path is where a
    /// current install keeps it, the packaged <c>LocalCache</c> path is the MSIX
    /// redirection of that same roaming folder, and <c>Claude-Data</c> is the
    /// older layout. A machine that has been upgraded can have more than one of
    /// them on disk, so the order is the answer rather than a preference.</para>
    ///
    /// <para>Takes the two roots as arguments so the order can be checked without
    /// a Claude install to check it against.</para>
    /// </summary>
    public static IReadOnlyList<string> ConfigPathCandidates(string appDataRoaming, string appDataLocal) =>
    [
        Path.Combine(appDataRoaming, "Claude", FileName),
        Path.Combine(appDataLocal, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", FileName),
        Path.Combine(appDataLocal, "Claude-Data", FileName)
    ];

    /// <summary>
    /// What the config says one server is, or nothing when it says nothing.
    ///
    /// <para>An absent <c>mcpServers</c> is zero servers and not a broken file:
    /// the app omits the property entirely until the first server is registered,
    /// so every machine that has never used one reads this way.</para>
    /// </summary>
    public static DevToolClaudeDesktopServer? ReadServer(JsonNode? root, string name)
    {
        if (root?[ServersPropertyName] is not JsonObject servers || servers[name] is not JsonObject entry)
        {
            return null;
        }

        var command = entry["command"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
        var args = new List<string>();

        if (entry["args"] is JsonArray declared)
        {
            foreach (var argument in declared)
            {
                if (argument is JsonValue item && item.TryGetValue<string>(out var argumentText))
                {
                    args.Add(argumentText);
                }
            }
        }

        return new DevToolClaudeDesktopServer(command, args);
    }

    /// <summary>
    /// Puts one server into the document and hands the whole document back.
    ///
    /// <para>The document, not a new one built from the servers: every other key
    /// in it is the person's own settings, and the version of this that emitted
    /// <c>{"mcpServers": …}</c> would be indistinguishable from a correct one
    /// until somebody noticed their preferences had gone.</para>
    ///
    /// <para>Only <c>command</c>, <c>args</c> and <c>env</c> are written, and only
    /// when there is something to write. The app's own validator takes stdio
    /// servers and nothing else — no <c>type</c>, no <c>url</c>, no
    /// <c>transport</c> — and silently strips whatever else it finds, so a
    /// property added here would vanish without ever being reported.</para>
    /// </summary>
    public static JsonObject MergeServer(
        JsonObject root,
        string name,
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (root[ServersPropertyName] is not JsonObject servers)
        {
            servers = [];
            root[ServersPropertyName] = servers;
        }

        var entry = new JsonObject { ["command"] = command };

        if (args is { Count: > 0 })
        {
            var values = new JsonArray();
            foreach (var argument in args)
            {
                values.Add(argument);
            }

            entry["args"] = values;
        }

        if (env is { Count: > 0 })
        {
            var values = new JsonObject();
            foreach (var pair in env)
            {
                values[pair.Key] = pair.Value;
            }

            entry["env"] = values;
        }

        servers[name] = entry;

        return root;
    }

    /// <summary>
    /// Takes one server out of the document, leaving everything else exactly as
    /// it was — including an <c>mcpServers</c> that is now empty.
    ///
    /// <para>The empty object stays rather than being tidied away. It is the
    /// difference between "this machine has no servers registered" and "this
    /// machine has never registered one", and only one of those is a config a
    /// person has been editing.</para>
    /// </summary>
    public static JsonObject RemoveServer(JsonObject root, string name)
    {
        if (root[ServersPropertyName] is JsonObject servers)
        {
            servers.Remove(name);
        }

        return root;
    }
}

/// <summary>One entry of the Claude desktop app's server list, in the only shape
/// its own validator accepts: a command, and the arguments it is given. There is
/// no transport to read — the file holds stdio servers and nothing else.</summary>
public sealed record DevToolClaudeDesktopServer(string Command, IReadOnlyList<string> Args)
{
    /// <summary>The registration as one line, which is what the row compares
    /// against the catalog's own command. The arguments are part of it: a
    /// registration pointing at the right executable with the wrong arguments is
    /// a registration that does not work, and the command alone cannot say
    /// so.</summary>
    public string CommandLine => string.Join(' ', new[] { Command }.Concat(Args));
}
