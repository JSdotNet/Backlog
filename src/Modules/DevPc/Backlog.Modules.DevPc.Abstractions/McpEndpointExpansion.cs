using System.Text.RegularExpressions;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>What one placeholder resolves to, or <see langword="null"/> when this
/// resolver does not answer for it.
///
/// <para>Not answering is a real answer and is not the same as failing: a
/// <c>${NAME:-default}</c> falls back to its literal default, and a placeholder
/// with no default is left exactly as it was and reported. The one thing a
/// resolver may never do is invent a value.</para></summary>
internal delegate string? McpPlaceholderResolver(string name);

/// <summary>
/// The two placeholders a catalog entry may carry, and the one thing that fills
/// them in.
///
/// <para>The vocabulary is closed on purpose, and that is the whole design.
/// The catalog is a hand-edited JSON file in a folder that syncs between
/// machines, and what comes out of this ends up on a command line and in an AI
/// host's own configuration. A general <c>${VAR}</c> expander over
/// <c>Environment.GetEnvironmentVariable</c> would make that file a way to lift
/// any environment variable of the machine reading it — a key, a connection
/// string, a password — into both. So nothing here reads the environment, and
/// two names are all that resolve.</para>
///
/// <para><c>${NAME:-default}</c> is accepted because a person's first HTTP entry
/// is pasted out of a <c>.mcp.json</c> and that is the syntax those files use.
/// The source wins wherever it answers: the default in the file is what the file
/// guessed, and the source is what this machine is actually doing. The default is
/// what is left when nothing answers.</para>
///
/// <para>A name this build does not fill in may still carry a default, and that
/// default is used: it is a literal out of the same file rather than anything
/// read off this machine, so honouring it takes nothing from the paragraph above
/// and is what makes a pasted line work unaltered. What is never resolved is a
/// name this build does not know <em>with nothing written beside it</em> — that
/// one is left literal and named in
/// <see cref="McpExpansion.Problem"/>. Half-expanding and registering anyway is
/// the one outcome that must not happen — a URL still carrying <c>${</c> is not a
/// URL, and a registration made from one fails at some later moment, on a machine,
/// with no trace of why.</para>
/// </summary>
internal static partial class McpPlaceholders
{
    /// <summary>The port this machine's MCP server is configured to listen
    /// on.</summary>
    internal const string PortName = "BACKLOG_MCP_PORT";

    /// <summary>The token a registration has to carry. Resolving it mints one —
    /// which is why only one of the two resolvers below will.</summary>
    internal const string TokenName = "BACKLOG_MCP_TOKEN";

    /// <summary>
    /// The resolver for reading a row: the port, and never the token.
    ///
    /// <para>Describing a row happens on every refresh, for every host, whether or
    /// not anybody presses anything. Minting a secret to draw a table with would
    /// persist a credential nobody asked for — so this one refuses, the token
    /// placeholder stays literal, and the expansion says it did not resolve.</para>
    /// </summary>
    internal static McpPlaceholderResolver Describing(IMcpEndpointSource source) =>
        name => name == PortName ? source.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>The resolver for making a registration: both, because this is the
    /// moment a token is genuinely needed and the moment a person asked for
    /// one.</summary>
    internal static McpPlaceholderResolver Applying(IMcpEndpointSource source) => name => name switch
    {
        PortName => source.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TokenName => source.EnsureToken(),
        _ => null
    };

    /// <summary>
    /// One string with its placeholders filled in, and the first one that could
    /// not be.
    /// </summary>
    /// <remarks>Pure, and takes the resolver as a delegate, so that the decision
    /// "may this call site mint a token" is made by the caller and is visible in
    /// which resolver it passed.</remarks>
    internal static McpExpansion Expand(string? text, McpPlaceholderResolver resolve)
    {
        var subject = text ?? string.Empty;

        if (!subject.Contains("${", StringComparison.Ordinal))
        {
            return new McpExpansion(subject, null);
        }

        string? unresolved = null;

        var expanded = PlaceholderRegex().Replace(subject, match =>
        {
            var name = match.Groups["name"].Value;

            // Only these two may be asked of this machine, and nothing else ever
            // is: that closed vocabulary is what stops the catalog being a way to
            // lift an environment variable onto a command line. A blank answer is
            // read as no answer — an empty port is not a port, and a resolver that
            // returned one would produce `http://127.0.0.1:/mcp` and call it
            // resolved.
            if (name is PortName or TokenName && resolve(name) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            // The literal default the file itself carries, for any name. It reads
            // nothing off this machine — it is text out of the same document — and
            // it is the whole reason the `:-` syntax is accepted: a line pasted
            // out of a `.mcp.json` works unaltered, including one naming something
            // this build has never filled in. An unknown name with no default is
            // still refused by name below, which is the case that matters.
            //
            // An empty default is not a default. `${BACKLOG_MCP_PORT:-}` resolving
            // to "" would describe the row as `http://127.0.0.1:/mcp` with nothing
            // reported against it.
            if (match.Groups["default"].Value is { Length: > 0 } fallback && !string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            // Left exactly as the file spelled it. The caller fails the row on
            // Problem rather than registering a half-expanded string.
            unresolved ??= name;
            return match.Value;
        });

        // A `${` the pattern above did not match at all — `${}`, or an unclosed
        // one. There is no name to report, so the text is reported instead: it is
        // still a string that must not be registered.
        if (unresolved is null && expanded.Contains("${", StringComparison.Ordinal))
        {
            unresolved = expanded;
        }

        return new McpExpansion(expanded, unresolved);
    }

    /// <summary><c>${NAME}</c> and <c>${NAME:-default}</c>, and nothing else. The
    /// name is restricted to the shape an environment variable has so that a
    /// stray <c>${</c> in a URL fails the row rather than silently matching half
    /// of it.</summary>
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::-(?<default>[^}]*))?\}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>
/// What an expansion produced, and whether it may be used.
///
/// <para><see cref="Unresolved"/> is the whole point: a caller that only looked at
/// <see cref="Text"/> would register a string still carrying <c>${</c>.</para>
/// </summary>
internal sealed record McpExpansion(string Text, string? Unresolved)
{
    /// <summary>Whether every placeholder was filled in. The only state in which
    /// <see cref="Text"/> may be registered.</summary>
    internal bool Resolved => Unresolved is null;

    /// <summary>Why the row cannot be registered, in the words the person reading
    /// the pane needs — which differ by what went wrong, because "we do not fill
    /// that in" and "we would not mint that here" are two different problems with
    /// two different answers.</summary>
    internal string? Problem => Unresolved switch
    {
        null => null,
        McpPlaceholders.TokenName => "This registration needs the MCP server's token, and nothing that only reads a row may mint one.",
        McpPlaceholders.PortName => "This registration needs the MCP server's port, and nothing on this machine could answer it.",
        var name => $"\"{name}\" is not something this build fills in. Only ${{{McpPlaceholders.PortName}}} and ${{{McpPlaceholders.TokenName}}} are expanded, and never an environment variable."
    };
}
