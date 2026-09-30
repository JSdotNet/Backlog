namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// This machine's own MCP server, as far as a catalog row needs to know it.
///
/// <para>A port rather than a reference to the worker, for the reason every other
/// port in this project is one: the worker lives in the desktop head, which no
/// test project references and no other host has. What a row needs is two facts
/// and a way to be told they changed — which port the server is configured on,
/// and why nothing is listening when nothing is — plus the token a registration
/// has to carry. Whether there is a server at all is not a question: it is always
/// on while the desktop app runs. None of those needs a
/// <c>Microsoft.Extensions.Hosting</c> reference to say, so this project takes no
/// new dependency for it.</para>
///
/// <para><see cref="Port"/> is the configured port and not a bound one, which is
/// the distinction that matters: it is what a registration is written from, and
/// it is answerable whether or not the server ever bound it. Writing a
/// registration only from a bound port would make registering the server require
/// it to already be running.</para>
///
/// <para>Deliberately no <c>Endpoint</c> and no <c>IsListening</c>. Both were
/// here and both were read by nothing: the note they were specified for — the
/// worker's port-collision sentence carried verbatim — is produced from
/// <see cref="Unavailable"/>, and the state beside it ("no error, not bound
/// yet") is the half-second a listener takes to start rather than anything a
/// row should announce. An
/// <c>Endpoint</c> that nothing read was worse than unused: it looked like the
/// one place the address was built while the address a registration actually
/// points at came from the catalog string, so the two could move apart with no
/// build, test or row saying so. What holds them together instead is a test —
/// the catalog's URL path is asserted against the constant the hosts serve
/// from.</para>
/// </summary>
public interface IMcpEndpointSource
{
    /// <summary>The configured port, which is not the same thing as a bound one.
    /// It is what the registration is written from — see the note on the
    /// interface — and it is answerable while the server is stopped.</summary>
    int Port { get; }

    /// <summary>Why nothing is listening, in the words the worker already has for it — a
    /// port somebody else holds, most often — or nothing when there is nothing
    /// wrong. A sentence rather than a flag, because the row shows it and the
    /// operator is the one who has to act on it.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// The token a registration has to carry, minted and persisted if this
    /// machine has none yet.
    /// </summary>
    /// <remarks>On demand, and a method rather than a property, because it has a
    /// side effect worth seeing at the call site: it writes a secret to this
    /// machine's settings. Describing a row must never call it — see the two
    /// resolvers on <see cref="McpPlaceholders"/> — or drawing a table would
    /// persist a credential nobody asked for.</remarks>
    string EnsureToken();

    /// <summary>Raised when any of the above has changed, so a pane that drew
    /// them can draw them again. The port is a setting a person edits while the
    /// pane is open.</summary>
    event Action? Changed;
}
