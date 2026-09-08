using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

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
/// Every session is stamped with this machine's identity, because that is the only
/// environment this source can speak for: neither agent records a hostname in what
/// it writes, so a session found here ran here. Sessions from another environment
/// arrive when that environment reports them, and this source is deliberately not
/// the thing that would have to be widened for that: it answers a port, and a second
/// implementation of that port can answer for a fleet without this one changing. The
/// stamp is the kernel's device identity, so a session and a registered Machine are the
/// same environment by identity rather than by their names agreeing; see
/// <c>.domain/sessions/dependencies.md</c>.
/// </para>
/// <para>
/// Every session is also stamped <see cref="AgentSessionOrigin.Local"/>, which is
/// the one field on a session record that describes the reading rather than the
/// session. This source read it off this machine's own disk and can open the same
/// file again to check; a record that arrived from elsewhere cannot say that, and
/// the source that produces those says <see cref="AgentSessionOrigin.Replicated"/>
/// on all of them. Stamped at the source rather than worked out later from whether
/// the environment id matches this device: the readers know without comparing
/// anything, and a comparison gets the wrong answer for a record this device pushed
/// and received back.
/// </para>
/// <para>
/// The identity is the kernel's <see cref="IDeviceIdentitySource"/> rather than
/// <c>Environment.MachineName</c>, and that is the substantive change in this class:
/// a name is not an identity. It is asked for once, at composition, because a machine
/// is not renamed mid-session and the identity source reads once too.
/// </para>
/// </summary>
internal sealed class LocalAgentSessionSource : IAgentSessionSource
{
    private readonly ClaudeSessionReader _claude;
    private readonly CopilotSessionReader _copilot;

    /// <summary>What a host composes: the two agents' own folders in the profile of
    /// whoever is signed in, this device's identity, and the wall clock.</summary>
    internal LocalAgentSessionSource(IDeviceIdentitySource identity)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"),
            IdOf(identity),
            identity.Current.Name,
            TimeProvider.System)
    {
    }

    /// <summary>Every input named, so the mapping can be tested against fixture
    /// folders and a fixed clock rather than against whatever this machine happens
    /// to have been doing.</summary>
    internal LocalAgentSessionSource(
        string claudeHome,
        string copilotHome,
        string environmentId,
        string environment,
        TimeProvider clock)
    {
        _claude = new ClaudeSessionReader(claudeHome, environmentId, environment, clock);
        _copilot = new CopilotSessionReader(copilotHome, environmentId, environment, clock);
    }

    /// <summary>The device id as this contract carries identifiers: a string, in the
    /// Guid's plain lower-case "D" form. Formatted once here so every session written
    /// by this source spells it the same way — a dashboard filter comparing ids
    /// ordinally cannot afford two spellings of one machine.</summary>
    private static string IdOf(IDeviceIdentitySource identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.Current.Id.ToString();
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
