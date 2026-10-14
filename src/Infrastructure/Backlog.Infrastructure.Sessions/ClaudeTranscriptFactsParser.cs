using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// What the session list reads out of a Claude transcript, folded one line at a time:
/// the folder and branch from the first lines that state them, how many turns the
/// person took, and — through <see cref="ClaudeTranscriptWork"/> — the entry point,
/// the pull requests and the tokens per model.
/// <para>
/// A fold rather than a pass, so a transcript that is being written to can be read
/// from where the last read stopped: everything a later line's meaning depends on is
/// a field here. Most of it is order-dependent — the folder and branch are first-wins
/// and line-counted, a message's usage is last-wins per id and its lines can straddle
/// two reads, a pull request is first-wins per URL — so a resumed read is only right
/// if it continues this state rather than starting a new one. <see cref="Clone"/> is
/// what makes that safe: a reader keeps the state its last read left untouched, folds
/// the new lines into a copy, and keeps the copy only when the read completes.
/// </para>
/// </summary>
internal sealed class ClaudeTranscriptFactsParser : ILineFold<ClaudeTranscriptFactsParser>
{
    /// <summary>How far into a transcript to look for the folder it ran in. The
    /// first few lines are queued prompts and hooks, which carry no <c>cwd</c>; the
    /// first real turn does, and that is within a handful of lines. A cap so a
    /// transcript that never states one costs a few reads rather than its whole
    /// length.</summary>
    private const int HeaderLines = 40;

    private readonly ClaudeTranscriptWork _work;
    private string _folder = string.Empty;
    private string? _branch;
    private int _turns;
    private int _line;

    internal ClaudeTranscriptFactsParser() => _work = new ClaudeTranscriptWork();

    private ClaudeTranscriptFactsParser(ClaudeTranscriptFactsParser other)
    {
        _work = other._work.Clone();
        _folder = other._folder;
        _branch = other._branch;
        _turns = other._turns;
        _line = other._line;
    }

    /// <summary>A copy to fold further lines into, leaving this one as it is.</summary>
    public ClaudeTranscriptFactsParser Clone() => new(this);

    /// <summary>One line of the transcript, in the order the file holds it.</summary>
    public void Observe(string text)
    {
        // The header is still only looked for in the first lines, and still only until
        // it is found. Widening the read to the whole file is about the count; a cwd
        // stated three thousand lines in would be a different session's, from a resume
        // this record has no way to attribute.
        if (_folder.Length == 0 && _line++ < HeaderLines)
        {
            var (lineFolder, lineBranch) = Header(text);

            _folder = lineFolder ?? string.Empty;
            _branch ??= lineBranch;
        }

        if (IsTurn(text)) _turns++;

        _work.Observe(text);
    }

    /// <summary>
    /// What the lines folded so far establish.
    /// <para>
    /// Zero turns is reported as absent, never as 0. A count of 0 claims a person
    /// opened this session and never spoke in it; what a transcript with nothing
    /// countable in it actually supports is that there is nothing here to count. See
    /// <c>AgentSession.TurnCount</c>, and the Session Log's invariant behind it.
    /// </para>
    /// </summary>
    internal TranscriptFacts Facts() =>
        new(_folder, _branch, _turns == 0 ? null : _turns)
        {
            Entrypoint = _work.Entrypoint,
            PullRequests = _work.PullRequests,
            ModelUsage = _work.ModelUsage
        };

    /// <summary>
    /// What a read that was cut short may still say.
    /// <para>
    /// Null rather than the turns counted so far. A count interrupted at line nine of
    /// ninety is not a nearly-right number, it is a wrong one with nothing on the row
    /// to say so, and a reader comparing two sessions would be comparing one real count
    /// against one lost race. The folder and branch are kept because they are complete
    /// or absent, never half-read.
    /// </para>
    /// </summary>
    internal TranscriptFacts Interrupted() =>
        new(_folder, _branch, null) { Entrypoint = _work.Entrypoint };

    /// <summary>
    /// Whether this line is a turn the person took — one prompt they sent, which is
    /// what <c>AgentSession.TurnCount</c> defines a turn to be.
    /// <para>
    /// Three things have to be true, and each of them rules out a cheaper answer that
    /// is wrong on this machine's real transcripts.
    /// </para>
    /// <list type="number">
    /// <item><description>The line's <em>root</em> <c>type</c> is <c>user</c>. The
    /// substring occurs nested as well — an assistant quoting a transcript back, an
    /// attachment carrying a user object — so counting occurrences over-reads, by
    /// about a hundred lines in the hundred most recent transcripts here.</description></item>
    /// <item><description>It was not injected. A skill body arrives as a user-role
    /// message marked <c>isMeta</c>, and a subagent's task prompt as one marked
    /// <c>isSidechain</c>; neither was typed by anybody, and the second belongs to a
    /// transcript this reader lists as its own session anyway.</description></item>
    /// <item><description>It carries something the person said rather than something a
    /// tool returned — see <see cref="Prompted"/>, which is where the real difference
    /// is. Counting every root-level user line instead would have reported about 6,100
    /// turns across the hundred transcripts where a person sent about 180.</description></item>
    /// </list>
    /// </summary>
    private static bool IsTurn(string line)
    {
        // A necessary condition that costs a substring scan, taken before the line is
        // parsed at all: on this machine's transcripts about three lines in four fail
        // it, and parsing every line of 64 MB to count part of it is the version of
        // this that is too slow to ship. Claude writes compact JSON, so this is the
        // exact byte sequence a user line carries. A future format that spaced its
        // separators would fail this filter on every line and the count would go
        // absent rather than wrong — which is the direction this product's counts are
        // meant to fail in, and the reason a substring is allowed to be load-bearing
        // here at all.
        if (!line.Contains("\"type\":\"user\"", StringComparison.Ordinal)) return false;

        if (line.Length == 0 || line[0] is not '{') return false;

        try
        {
            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return false;
            if (ClaudeJson.Text(root, "type") is not "user") return false;
            if (Flag(root, "isMeta") || Flag(root, "isSidechain")) return false;

            return Prompted(root);
        }
        catch (JsonException)
        {
            // A half-written last line, or a line this reader does not understand. It
            // is not counted, which loses at most one turn off a running session's
            // count and never invents one.
            return false;
        }
    }

    /// <summary>
    /// Whether a user-role line holds something the person sent.
    /// <para>
    /// This is the check that decides the number. Claude files a tool's output as a
    /// user-role message — the result is addressed to the model in the person's role —
    /// so on the hundred most recent transcripts on this machine about 6,100 lines are
    /// user-role and about 180 of them are prompts. A count that did not draw this line
    /// would be a tool-call count with a turn count's name on it, off by a factor of
    /// more than thirty.
    /// </para>
    /// <para>
    /// A string content is a typed prompt. An array is judged by what is in it: a text
    /// or image block is the person's, a <c>tool_result</c> block is not, and a line
    /// holding only tool results is not a turn. A user-role line with no message at all
    /// is not counted either — the reader cannot tell what it was, and the whole point
    /// of the null is that it does not have to pretend.
    /// </para>
    /// </summary>
    private static bool Prompted(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)) return false;
        if (message.ValueKind is not JsonValueKind.Object) return false;
        if (!message.TryGetProperty("content", out var content)) return false;

        return content.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(content.GetString()),
            JsonValueKind.Array => content.EnumerateArray().Any(block =>
                block.ValueKind is JsonValueKind.Object && ClaudeJson.Text(block, "type") is not "tool_result"),
            _ => false
        };
    }

    /// <summary>Present and true. A flag Claude did not write is not a flag set to
    /// false, but nothing here needs to tell those apart.</summary>
    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static (string? Folder, string? Branch) Header(string line)
    {
        if (line.Length == 0 || line[0] is not '{') return (null, null);

        try
        {
            using var document = JsonDocument.Parse(line);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? (ClaudeJson.Text(document.RootElement, "cwd"), ClaudeJson.Text(document.RootElement, "gitBranch"))
                : (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
