using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The sessions on this machine: what the two agents left in the user profile,
/// answered as one list.
/// <para>
/// The two readers are asked independently and their failures are collected rather
/// than thrown. A machine with only one of the two agents installed is the ordinary
/// case, not an error — the other folder simply is not there — and a folder that
/// exists but cannot be read is worth naming rather than worth blanking the
/// surface for. Either way the reader sees the half of the picture that is
/// available, and is told which half is missing.
/// </para>
/// <para>
/// Every session is stamped with this machine's name, because that is the only
/// environment this source can speak for: neither agent records a hostname in what
/// it writes, so a session found here ran here. Sessions from another environment
/// arrive when that environment reports them, and this source is deliberately not
/// the thing that would have to be widened for that: it answers a port, and a second
/// implementation of that port can answer for a fleet without this one changing. See
/// <c>.domain/sessions/dependencies.md</c> for how an Environment lines up
/// with a registered Machine when the two name the same box.
/// </para>
/// </summary>
internal sealed class LocalAgentSessionSource : IAgentSessionSource
{
    private readonly ClaudeSessionReader _claude;
    private readonly CopilotSessionReader _copilot;

    /// <summary>What a host composes: the two agents' own folders in the profile of
    /// whoever is signed in, and the wall clock.</summary>
    internal LocalAgentSessionSource()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"),
            Environment.MachineName,
            TimeProvider.System)
    {
    }

    /// <summary>Every input named, so the mapping can be tested against fixture
    /// folders and a fixed clock rather than against whatever this machine happens
    /// to have been doing.</summary>
    internal LocalAgentSessionSource(
        string claudeHome,
        string copilotHome,
        string environment,
        TimeProvider clock)
    {
        _claude = new ClaudeSessionReader(claudeHome, environment, clock);
        _copilot = new CopilotSessionReader(copilotHome, environment, clock);
    }

    public async Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = new List<AgentSession>();
        var unreadable = new List<string>();
        var discovered = 0;

        foreach (var reader in Readers(cancellationToken))
        {
            var reading = await Collect(reader.Name, reader.Read, unreadable).ConfigureAwait(false);

            sessions.AddRange(reading.Sessions);
            discovered += reading.Discovered;
        }

        return new AgentSessionCatalog(sessions, unreadable, discovered);
    }

    private (string Name, Func<Task<SessionReading>> Read)[] Readers(CancellationToken cancellationToken) =>
    [
        (ClaudeSessionReader.Name, () => _claude.ReadAsync(cancellationToken)),
        (CopilotSessionReader.Name, () => _copilot.ReadAsync(cancellationToken))
    ];

    /// <summary>
    /// One reader's answer, or its name on the unreadable list.
    /// <para>
    /// The three caught types are the three ways reading somebody else's folder goes
    /// wrong: it is not there, it is not ours to read, or what is in it is not what
    /// was expected. Anything else is a fault in this code rather than a fact about
    /// the machine, and swallowing it here would hide it.
    /// </para>
    /// </summary>
    private static async Task<SessionReading> Collect(
        string name,
        Func<Task<SessionReading>> read,
        List<string> unreadable)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            unreadable.Add(name);

            return SessionReading.None;
        }
    }
}
