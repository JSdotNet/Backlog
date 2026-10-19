namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// This machine's own MCP server, as far as a catalog row and the Settings
/// screen's MCP server page need to know it.
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
/// <para>The Settings page is what the rest is for: <see cref="IsListening"/> and
/// <see cref="Endpoint"/> say whether the listener holds its port and where — the
/// question a person asks when a client reports the connection refused — and
/// <see cref="SetPort"/> and <see cref="Retry"/> are the two things that page can
/// do about it. A catalog row reads none of them: it registers from
/// <see cref="Port"/>, and its note is <see cref="Unavailable"/>. An earlier
/// <c>Endpoint</c> and <c>IsListening</c> were taken out because nothing read
/// them; they are back because the page does. The address is never built here or
/// in the page from scratch — the worker builds <see cref="Endpoint"/> with the
/// same helper a registration is written with, so the two cannot move
/// apart.</para>
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

    /// <summary>True while the listener holds its port. False while it is being
    /// started, after a bind failed, and while a port change rebinds it; which of
    /// those it is, <see cref="Unavailable"/> says.</summary>
    bool IsListening { get; }

    /// <summary>Where the listener answers — loopback, the bound port and the
    /// endpoint path — or <see langword="null"/> while nothing is listening.
    /// Bound rather than configured, unlike <see cref="Port"/>: it is the answer
    /// to "is it there", and a screen showing where the server would be uses
    /// <see cref="Port"/>.</summary>
    Uri? Endpoint { get; }

    /// <summary>
    /// Moves the server to another loopback port, or answers why not in a
    /// sentence fit to show beside the field — a port outside the usable range,
    /// or one chosen but not saved for next time. <see langword="null"/> when it
    /// was taken.
    /// </summary>
    /// <remarks>The listener rebinds on its own once the port moves, off the
    /// calling thread, and <see cref="Changed"/> says when it has. Every
    /// registration names the port, so each has to change with it.</remarks>
    string? SetPort(int port);

    /// <summary>
    /// Tries the configured port again after a bind that failed — once whatever
    /// held it has let go. Does nothing while the listener is running or still
    /// starting. Never on the calling thread: the outcome arrives through
    /// <see cref="Changed"/>.
    /// </summary>
    void Retry();

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

    /// <summary>The token this machine already has, or <see langword="null"/>
    /// when none has been minted — read, never created.</summary>
    /// <remarks>For masking rather than registering: a value that has to be kept
    /// out of what is written down, such as the last run's transcript, is only a
    /// secret if it exists, and minting one in order to hide it would persist a
    /// credential nobody asked for.</remarks>
    string? CurrentToken { get; }

    /// <summary>Raised when any of the above has changed, so a pane that drew
    /// them can draw them again. The port is a setting a person edits while the
    /// pane is open.</summary>
    event Action? Changed;
}
