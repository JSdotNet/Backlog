namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Who the person on this machine is, as the Devbook context names them when it
/// records something they did: the machine's label.
/// <para>
/// One name for a remark and for a subject's state, so the two read alike. Here
/// rather than beside the panels because a subject's state is set from places that
/// see only this project — the MCP tools among them — and <c>devbook.decision@1</c>
/// has the host, never the caller, say who decided. A panel reads it back as "You"
/// when it is this machine's.
/// </para>
/// </summary>
public static class DevbookLocalAuthor
{
    public static string Name => Environment.MachineName;

    /// <summary>
    /// The author every Devbook write that arrives over MCP is stamped with — a
    /// note, a reply, a proposal set back to draft — in place of <see cref="Name"/>.
    /// <para>
    /// Local ADR 0052 §7: the MCP token is an agent's, so a write through it is
    /// never recorded as the person's. A record already stored keeps the author it
    /// was written with; <see cref="Display"/> reads this one back as written.
    /// </para>
    /// </summary>
    public const string AgentViaMcp = "agent via MCP";

    /// <summary>
    /// How a recorded author reads on this host: "You" when it is
    /// <see cref="Name"/>, compared without case as a machine label is, and the
    /// recorded name otherwise. One rule for a remark and a subject's state, so the
    /// two can never disagree about who "You" is.
    /// </summary>
    public static string? Display(string? by) =>
        string.Equals(by, Name, StringComparison.OrdinalIgnoreCase) ? "You" : by;
}
