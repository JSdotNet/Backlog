using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The desktop head's half of the two-mechanism <c>mcpServers</c> array, checked
/// by reading its source.
///
/// <para>A source scan for the reason <see cref="ProcessLaunchTests"/> is one: the
/// file these rules are about lives in <c>src/App/Backlog.Desktop</c>, the MAUI
/// head, which no test project references or can reference. The decisions
/// themselves were pushed into <c>Backlog.Modules.DevPc.Abstractions</c> so that
/// one set of unit tests covers both services — what is left here is that this
/// service actually goes through them.</para>
///
/// <para>Two facts, and both of them were bugs. The enumeration reached for a
/// <c>packageId</c> and skipped any entry without one, so a server registered by
/// the command it declares — which this repository's own catalog ships — was
/// dropped before a row existed. And the action shelled out to <c>dotnet tool</c>
/// with whatever that reach returned, which for such an entry would be a blank
/// package id: an install of nothing, reported as the row's own failure.</para>
/// </summary>
public class DevToolMcpMechanismTests
{
    private const string ServiceFile = "src/App/Backlog.Desktop/Services/DevToolService.cs";

    /// <summary>The argument lists that ask <c>dotnet</c> about one package.
    ///
    /// <para><c>tool list</c> is deliberately not among them: it is one inventory
    /// read for the whole catalog rather than a launch on any row's behalf, and
    /// the listing already declines to run it when no server in the catalog is a
    /// .NET tool at all.</para></summary>
    private static readonly string[] PackageLaunches =
    [
        "\"tool\", \"install\"",
        "\"tool\", \"update\"",
        "\"tool\", \"uninstall\"",
        "\"tool\", \"search\""
    ];

    /// <summary>What a member has to name for a package launch inside it to be
    /// one that cannot happen with a blank id.</summary>
    private const string MechanismCheck = "DevToolMcpMechanism.DotNetTool";

    [Fact]
    public void The_desktop_head_reads_its_mcp_rows_through_the_shared_reader()
    {
        var source = Source();

        Assert.Contains("DevToolConfiguration.ReadMcpServer(", source, StringComparison.Ordinal);

        // The two reaches that dropped the row and then acted with what they
        // found. Both are the abstraction's job now, and the reader answers for an
        // entry of either mechanism.
        Assert.DoesNotContain("GetString(server, \"packageId\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredString(server, \"packageId\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_dotnet_tool_launch_sits_behind_a_mechanism_check()
    {
        var members = Members().ToList();
        var launching = members
            .Where(member => PackageLaunches.Any(launch => member.Body.Contains(launch, StringComparison.Ordinal)))
            .ToList();

        // Otherwise the rule below passes by finding nothing — which is exactly
        // what it would do if the strings it looks for were ever reworded.
        Assert.NotEmpty(launching);

        var offenders = launching
            .Where(member => !member.Body.Contains(MechanismCheck, StringComparison.Ordinal))
            .Select(member => member.Declaration)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These members run `dotnet tool` against one package without naming the mechanism that says the "
            + $"entry has one, so a command-registered MCP server reaches them with a blank package id. Take a "
            + $"DevToolMcpServer and check `{MechanismCheck}` before the launch:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// A token this app minted must never be written down where somebody can
    /// read it back.
    /// </summary>
    /// <remarks>
    /// <para>It reaches the command log twice over, and neither is obvious from
    /// the call site: once as the <c>--header</c> argument, joined verbatim into
    /// the paste-ready line the pane renders in full, and again in the
    /// <c>Headers:</c> block <c>claude mcp get</c> echoes back. A secret written
    /// into a pane somebody can screenshot is a secret that has to be
    /// rotated.</para>
    ///
    /// <para>A source scan rather than a unit test for the reason the rest of
    /// this file is one: the only member that mints lives in the MAUI head, which
    /// no test project references. What the scan holds is the structural half —
    /// the mint and the redaction are in the same member, so a later member that
    /// grows a mint cannot quietly leave the redaction behind.</para>
    /// </remarks>
    [Fact]
    public void Every_member_that_mints_a_token_also_redacts_it()
    {
        var minting = Members()
            .Where(member => member.Body.Contains(MintCall, StringComparison.Ordinal))
            .ToList();

        // Otherwise the rule below passes by finding nothing, which is what it
        // would do the day the port renames its one minting method.
        Assert.NotEmpty(minting);

        var offenders = minting
            .Where(member => !member.Body.Contains(RedactionCall, StringComparison.Ordinal))
            .Select(member => member.Declaration)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These members mint an MCP token with `{MintCall}` and never tell the command log to keep it out of "
            + "what it records. The token reaches the log twice — as the `--header` argument and in the `Headers:` "
            + $"block the CLI echoes back — and the pane shows both. Call `{RedactionCall}` with it first:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// A failure sentence is process output, and process output is where a
    /// credential comes back.
    /// </summary>
    /// <remarks>
    /// <para>The defect this rule is written against: a static
    /// <c>CommandFailure</c> returned <c>result.Error</c> verbatim into a
    /// <c>DevToolActionResult.Message</c>, which the pane renders in the status
    /// line directly above the command log. So a failing
    /// <c>claude mcp add … --header "Authorization: Bearer …"</c> showed the
    /// token in the sentence and masked it in the transcript underneath — the
    /// redaction was real and was simply not on the path anybody read
    /// first.</para>
    ///
    /// <para>What is held here is the structural repair rather than the one
    /// call: the only member allowed to turn what a process printed into a
    /// sentence is one that also masks, so a second such member cannot be
    /// written without the masking. A source scan for the same reason as every
    /// rule above it.</para>
    /// </remarks>
    [Fact]
    public void Every_sentence_made_out_of_process_output_is_masked()
    {
        var reporting = Members()
            .Where(member => ProcessText.IsMatch(member.Body))
            .Where(member => Reported.IsMatch(member.Body))
            .ToList();

        // Otherwise this passes by finding nothing, which is what it would do the
        // day either shape above is reworded.
        Assert.NotEmpty(reporting);

        var offenders = reporting
            .Where(member => !MaskingCalls.Any(call => member.Body.Contains(call, StringComparison.Ordinal)))
            .Select(member => member.Declaration)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These members build something a person reads out of what a process printed, and never mask it. "
            + "What comes back on stderr includes the `Headers:` block the CLI echoes, and the pane renders the "
            + "sentence above the log it redacts. Go through the command log — one of "
            + $"{string.Join(", ", MaskingCalls)}:\n"
            + string.Join('\n', offenders));

        // And the old shape is gone by name, so nothing reintroduces it beside
        // the one that replaced it.
        Assert.DoesNotContain("string CommandFailure(", Source(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The secrets are known to the log before the apply runs its first command.
    /// </summary>
    /// <remarks>
    /// <para>The log masks as it records, not when it is read, so a value handed
    /// to it afterwards is a value already written into a line the pane will
    /// render. The mint used to sit two commands in — after
    /// <c>claude mcp get</c>, which echoes back the <c>Headers:</c> block of
    /// whatever is registered today — under a remark claiming it came first.
    /// Ordering is the whole of this rule, so it is read as one: the seeding call
    /// appears before either command in the member's own text.</para>
    ///
    /// <para>And it seeds from the entry's headers rather than from Backlog's own
    /// token alone. The Add-a-tool form takes free-text headers, so
    /// <c>X-Api-Key: sk-live-…</c> is a shape the catalog can hold, and it is
    /// joined verbatim into the paste-ready <c>--header</c> line.</para>
    /// </remarks>
    [Fact]
    public void The_registration_seeds_its_secrets_before_it_runs_anything()
    {
        var apply = Assert.Single(
            Members(),
            member => member.Declaration.Contains("ApplyClaudeMcpRegistrationAsync", StringComparison.Ordinal));

        var seeded = apply.Body.IndexOf(SeedCall, StringComparison.Ordinal);

        Assert.True(seeded >= 0, $"`ApplyClaudeMcpRegistrationAsync` never calls `{SeedCall}`.");

        foreach (var command in FirstCommands)
        {
            var ran = apply.Body.IndexOf(command, StringComparison.Ordinal);

            Assert.True(
                ran < 0 || seeded < ran,
                $"`{command}` is recorded before `{SeedCall}` tells the log what it may not write down. The log "
                + "masks at record time, so anything seeded after this point is already on screen.");
        }

        var seeding = Assert.Single(
            Members(),
            member => member.Declaration.Contains("void RedactRegistrationSecrets", StringComparison.Ordinal));

        // Every header value of the entry, not the one header this feature
        // happens to have shipped with.
        Assert.Contains("ReadHeaders(claude, \"headers\")", seeding.Body, StringComparison.Ordinal);
        Assert.Contains("QueryStringValues(", seeding.Body, StringComparison.Ordinal);
        Assert.Contains(RedactionCall, seeding.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The HTTP registration vector is not rebuilt in the head.
    /// </summary>
    /// <remarks>
    /// <para>The stdio call this sits beside separates its flags from the command
    /// with a <c>--</c>. Put one before a URL and the CLI reads the URL as a
    /// command to run: the add succeeds, and registers a server that can never
    /// answer. That trap is written down exactly once, with the reasoning, in
    /// <c>DevToolCommands.ClaudeMcpAddHttp</c> — and it is only written down once
    /// if the head goes through it rather than assembling its own argument
    /// list.</para>
    ///
    /// <para>A source scan for the same reason as the rules above it: the file
    /// lives in the MAUI head and the decision it has to defer to lives in
    /// <c>Backlog.Modules.DevPc.Abstractions</c>, where a unit test already covers
    /// what the vector is.</para>
    /// </remarks>
    [Fact]
    public void The_head_does_not_rebuild_the_http_registration_vector()
    {
        var source = Source();

        Assert.Contains(HttpVectorBuilder, source, StringComparison.Ordinal);

        foreach (var argument in HttpArguments)
        {
            Assert.DoesNotContain(argument, source, StringComparison.Ordinal);
        }
    }

    /// <summary>The one call that writes a credential to this machine. The
    /// parenthesis is part of it: what this rule is about is the call, and the
    /// prose around it names the method too.</summary>
    private const string MintCall = "EnsureToken(";

    /// <summary>What has to be in the same member as it: the command log being
    /// told to keep that value out of everything it records.</summary>
    private const string RedactionCall = "log.Redact";

    /// <summary>The one place the HTTP <c>claude mcp add</c> vector is
    /// written.</summary>
    private const string HttpVectorBuilder = "ClaudeMcpAddHttp";

    /// <summary>Reading what a process printed, either stream.</summary>
    private static readonly Regex ProcessText = new(@"\bresult\.(Output|Error)\b");

    /// <summary>Handing something to a person: the message of a failed action,
    /// or the message of an exception this service throws out of one.</summary>
    private static readonly Regex Reported = new(@"DevToolActionResult\.Failed\(|new InvalidOperationException\(");

    /// <summary>What such a member has to name: the masking itself, or one of
    /// the command log's two ways of reaching it. Every one of these ends in
    /// <c>DevToolOutput.Redact</c> against the secrets the log holds — which is
    /// the point of routing through the log rather than masking by hand.</summary>
    private static readonly string[] MaskingCalls = ["DevToolOutput.Redact", "log.Failure(", "log.Describe("];

    /// <summary>The call that tells the log what this apply may never write
    /// down.</summary>
    private const string SeedCall = "RedactRegistrationSecrets(";

    /// <summary>The two things the apply runs, both of which record a command —
    /// and the second of which prints back the <c>Headers:</c> block of the
    /// registration that is already there.</summary>
    private static readonly string[] FirstCommands = ["ResolveClaudeCliAsync(", "GetClaudeMcpServerAsync("];

    /// <summary>The flags only that vector carries — as C# string literals, so
    /// the prose that explains the rule is not itself a violation of it.</summary>
    private static readonly string[] HttpArguments = ["\"--transport\"", "\"--header\""];

    private static string Source() => File.ReadAllText(Path.Combine(Repository.Root.FullName, ServiceFile));

    /// <summary>The file's members, each with the text of its own body — split on
    /// a declaration at class indentation, which is how every member in this file
    /// is written.</summary>
    private static IEnumerable<Member> Members()
    {
        var declaration = new Regex(@"^    (private|public|internal|protected)\b.*$");
        var lines = Source().Split('\n');
        var current = "class DevToolService";
        var body = new List<string>();

        foreach (var line in lines)
        {
            if (declaration.IsMatch(line.TrimEnd('\r')))
            {
                yield return new Member(current, string.Join('\n', body));

                current = line.Trim();
                body.Clear();
            }

            body.Add(line);
        }

        yield return new Member(current, string.Join('\n', body));
    }

    private sealed record Member(string Declaration, string Body);
}
