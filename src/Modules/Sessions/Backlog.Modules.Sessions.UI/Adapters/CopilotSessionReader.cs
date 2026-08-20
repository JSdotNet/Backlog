using System.Globalization;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The Copilot CLI sessions a machine can account for, read from what Copilot
/// leaves in the user profile.
/// <para>
/// One folder per session under <c>session-state/&lt;id&gt;/</c>, and one file in
/// it worth reading: <c>workspace.yaml</c>, which states the id, the folder, the
/// git root, the repository in <c>owner/name</c> form, the branch, and when the
/// session was created and last updated. Copilot records more about where a
/// session was than Claude does, which is why this reader fills the repository
/// column and the Claude one leaves it empty rather than guessing.
/// </para>
/// <para>
/// Parsed by hand rather than with a YAML library. The file is a flat block of
/// <c>key: value</c> lines with no nesting, no anchors and no multi-line scalars,
/// and taking a package for it would put a YAML parser in the dependency graph of
/// the desktop app to read seven strings. If Copilot ever nests something in here,
/// that trade changes and the package is the right answer then.
/// </para>
/// <para>
/// <b>Copilot has no liveness marker.</b> The folder stays exactly as it is after a
/// session ends, so there is nothing on disk that distinguishes "running and quiet"
/// from "over". This reader therefore calls a recently-updated session Running and
/// everything else Finished, and never Stalled — a state that means "still
/// registered as live" and cannot be evidenced here. Claude's live-session file is
/// what makes the difference, not a difference in care.
/// </para>
/// </summary>
internal sealed class CopilotSessionReader
{
    private readonly string _home;
    private readonly string _environment;
    private readonly TimeProvider _clock;

    internal CopilotSessionReader(string home, string environment, TimeProvider clock)
    {
        _home = home;
        _environment = environment;
        _clock = clock;
    }

    /// <summary>What this reader is called when it cannot be read.</summary>
    internal static string Name => "Copilot";

    internal async Task<SessionReading> ReadAsync(CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(_home, "session-state"));

        if (!folder.Exists) return SessionReading.None;

        var now = _clock.GetUtcNow();
        var sessions = new List<AgentSession>();

        // Every descriptor is found and counted, and only the most recent are read.
        // Copilot keeps a folder per session forever — this machine had 705 of them —
        // so reading all of them would cost 705 file reads on every refresh to
        // produce rows nobody scrolls to.
        var descriptors = folder
            .EnumerateDirectories()
            .Select(directory => new FileInfo(Path.Combine(directory.FullName, "workspace.yaml")))
            .Where(descriptor => descriptor.Exists)
            .ToList();

        var recent = descriptors
            .OrderByDescending(descriptor => descriptor.LastWriteTimeUtc)
            .Take(AgentSessionLimits.PerAgent);

        foreach (var descriptor in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Guarded per descriptor for the reason the Claude reader states at its
            // own read: a session's own process may be writing this file, and an
            // IOException let out of here would cost every Copilot session instead
            // of the one row it actually concerns.
            string[] lines;

            try
            {
                lines = await File.ReadAllLinesAsync(descriptor.FullName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var session = FromDescriptor(Fields(lines), descriptor.Directory!.Name, descriptor, now);

            if (session is not null) sessions.Add(session);
        }

        return new SessionReading(sessions, descriptors.Count);
    }

    private AgentSession? FromDescriptor(
        Dictionary<string, string> fields,
        string folderName,
        FileInfo descriptor,
        DateTimeOffset now)
    {
        // The folder name is the session id too, so a descriptor missing its own id
        // field still produces an identifiable row.
        var id = Value(fields, "id") ?? folderName;

        if (string.IsNullOrWhiteSpace(id)) return null;

        var repository = Value(fields, "repository");
        var folder = Value(fields, "cwd") ?? Value(fields, "git_root") ?? string.Empty;

        // The descriptor's own timestamp is the fallback, not the first choice:
        // updated_at is what Copilot means by last activity, and a file copied or
        // restored would carry a timestamp that means nothing about the session.
        var lastActivity = Timestamp(fields, "updated_at")
            ?? new DateTimeOffset(descriptor.LastWriteTimeUtc, TimeSpan.Zero);

        return new AgentSession(
            Id: id,
            Kind: AgentSessionKind.Copilot,
            Environment: _environment,
            Title: TitleOf(repository, folder, id),
            WorkingFolder: folder,
            Repository: repository,
            Branch: Value(fields, "branch"),
            StartedAt: Timestamp(fields, "created_at"),
            LastActivityAt: lastActivity,
            State: now - lastActivity > AgentSessionStates.StaleAfter
                ? AgentSessionState.Finished
                : AgentSessionState.Running);
    }

    /// <summary>
    /// The flat <c>key: value</c> pairs, lowest line wins nothing — the first
    /// occurrence of a key is kept, because a repeated key in a flat block is
    /// malformed and the first one is the one Copilot wrote.
    /// <para>
    /// Indented lines are skipped rather than parsed. An indented line is a child of
    /// something, and this reader does not claim to understand structure; treating
    /// it as a top-level key would invent one.
    /// </para>
    /// </summary>
    private static Dictionary<string, string> Fields(IEnumerable<string> lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] is '#' or '-') continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');

            if (key.Length != 0 && value.Length != 0) fields.TryAdd(key, value);
        }

        return fields;
    }

    private static string? Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static DateTimeOffset? Timestamp(Dictionary<string, string> fields, string key) =>
        Value(fields, key) is { } text
        && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var value)
            ? value
            : null;

    /// <summary>
    /// The repository if Copilot recorded one, because that is what a person calls a
    /// session; the folder's leaf otherwise, and the short id when there is neither.
    /// </summary>
    private static string TitleOf(string? repository, string folder, string id)
    {
        if (!string.IsNullOrWhiteSpace(repository)) return repository;

        var leaf = string.IsNullOrWhiteSpace(folder)
            ? null
            : Path.GetFileName(folder.TrimEnd('\\', '/'));

        if (!string.IsNullOrWhiteSpace(leaf)) return leaf;

        return id.Length > 8 ? id[..8] : id;
    }
}
