using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// <c>ask-backlog.mjs</c>, run rather than read (local ADR 0041): the installed script is started
/// in Node once per hook event against a fake MCP endpoint — a Node HTTP server answering
/// <c>tools/call</c> from a script of replies, the way the SDK's stateless transport does, as one
/// server-sent event — and what it sent, what it printed and how it exited are asserted.
/// </summary>
public sealed class AskBacklogScriptTests : IDisposable
{
    private const string SessionId = "session-1";
    private const string ToolUseId = "toolu_01";
    private const string DialogId = "11111111-1111-1111-1111-111111111111";
    private const string ProseId = "22222222-2222-2222-2222-222222222222";
    private const string Question = "Which database?";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ask-backlog-script-" + Guid.NewGuid().ToString("N"));
    private readonly string _script;

    public AskBacklogScriptTests()
    {
        // The script as the app installs it, so what runs is the embedded bytes.
        _script = AskBacklogHook.Install(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static JsonArray Questions() => JsonNode.Parse("""
        [{ "question": "Which database?", "header": "Database", "multiSelect": false,
           "options": [{ "label": "SQLite", "description": "Local" }, { "label": "Cosmos", "description": "Cloud" }] }]
        """)!.AsArray();

    private static JsonObject PreInput() => new()
    {
        ["session_id"] = SessionId,
        ["hook_event_name"] = "PreToolUse",
        ["tool_name"] = "AskUserQuestion",
        ["tool_use_id"] = ToolUseId,
        ["tool_input"] = new JsonObject { ["questions"] = Questions() },
    };

    private static JsonObject Asked(string id, string route, int waitSeconds = 28800) =>
        new() { ["result"] = new JsonObject { ["id"] = id, ["route"] = route, ["waitSeconds"] = waitSeconds } };

    private static JsonObject Status(string status, JsonObject? answers = null, int delayMs = 0)
    {
        var result = new JsonObject { ["id"] = DialogId, ["status"] = status, ["choices"] = new JsonArray() };
        if (answers is not null) result["answers"] = answers;
        return new JsonObject { ["result"] = result, ["delayMs"] = delayMs };
    }

    private static JsonObject Unavailable() => new() { ["status"] = 503 };

    // ---- pre ---------------------------------------------------------------------------

    /// <summary>At the keyboard: one ask, nothing printed, so the dialog opens at once.</summary>
    [Fact]
    public void Pre_with_the_session_route_asks_once_and_lets_the_dialog_open()
    {
        var run = Run(
            Replies(("ask", [Asked(DialogId, "session")])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        var ask = Assert.Single(run.Calls);
        Assert.Equal("ask", ask.Name);
        Assert.Equal("Bearer test-token", ask.Authorization);
        Assert.Equal(SessionId, ask.Arguments["sessionId"]!.GetValue<string>());
        Assert.Equal(ToolUseId, ask.Arguments["toolUseId"]!.GetValue<string>());
        Assert.Equal("dialog", ask.Arguments["kind"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(Questions(), ask.Arguments["questions"]));
    }

    /// <summary>Away: it holds on get_answer, 50 seconds a call, and returns the Backlog answer
    /// as the tool's answers with the original questions.</summary>
    [Fact]
    public void Pre_with_the_backlog_route_waits_for_the_answer_and_allows_with_it()
    {
        var answers = new JsonObject { [Question] = "SQLite" };

        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog")]),
                ("get_answer", [Status("open"), Status("answered", answers)])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "ask", "get_answer", "get_answer" }, run.Calls.Select(call => call.Name));
        var poll = run.Calls[1].Arguments;
        Assert.Equal(DialogId, poll["id"]!.GetValue<string>());
        Assert.Equal(SessionId, poll["sessionId"]!.GetValue<string>());
        Assert.Equal(50, poll["waitSeconds"]!.GetValue<int>());

        var output = JsonNode.Parse(run.Output(0))!["hookSpecificOutput"]!;
        Assert.Equal("PreToolUse", output["hookEventName"]!.GetValue<string>());
        Assert.Equal("allow", output["permissionDecision"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(Questions(), output["updatedInput"]!["questions"]));
        Assert.True(JsonNode.DeepEquals(answers, output["updatedInput"]!["answers"]));
    }

    /// <summary>Closed elsewhere while it waited: nothing printed and nothing closed again.</summary>
    [Fact]
    public void Pre_stops_waiting_when_the_question_was_closed_elsewhere()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog")]),
                ("get_answer", [Status("closed")])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        Assert.Equal(new[] { "ask", "get_answer" }, run.Calls.Select(call => call.Name));
    }

    /// <summary>The dialog wait runs out: the question is closed timed-out and the dialog opens.</summary>
    [Fact]
    public void Pre_closes_the_question_timed_out_when_the_wait_runs_out()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog", waitSeconds: 2)]),
                ("get_answer", [Status("open", delayMs: 400)]),
                ("close", [Status("closed")])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        Assert.True(run.Calls[1].Arguments["waitSeconds"]!.GetValue<int>() <= 2, "get_answer is never held past the dialog wait.");
        var close = run.Calls[^1];
        Assert.Equal("close", close.Name);
        Assert.Equal(DialogId, close.Arguments["id"]!.GetValue<string>());
        Assert.Equal("timed-out", close.Arguments["reason"]!.GetValue<string>());
    }

    /// <summary>Backlog stops answering mid-wait — restarting — and the hook asks again after
    /// the retry interval rather than giving up.</summary>
    [Fact]
    public void Pre_retries_get_answer_while_Backlog_does_not_answer()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog")]),
                ("get_answer", [Unavailable(), Status("answered", new JsonObject { [Question] = "Cosmos" })])),
            new Dictionary<string, string> { ["ASK_BACKLOG_RETRY_MS"] = "300" },
            Step("pre", PreInput()));

        Assert.Equal(new[] { "ask", "get_answer", "get_answer" }, run.Calls.Select(call => call.Name));
        Assert.True(run.Calls[2].At - run.Calls[1].At >= 290, $"retried after {run.Calls[2].At - run.Calls[1].At} ms");
        Assert.Contains("\"allow\"", run.Output(0), StringComparison.Ordinal);
    }

    /// <summary>The retry interval as shipped: fifteen seconds, not sooner.</summary>
    [Fact]
    public void Pre_retries_every_fifteen_seconds_by_default()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog")]),
                ("get_answer", [Unavailable(), Status("answered", new JsonObject { [Question] = "Cosmos" })])),
            Step("pre", PreInput()));

        var gap = run.Calls[2].At - run.Calls[1].At;
        Assert.InRange(gap, 14_900, 20_000);
        Assert.Contains("\"allow\"", run.Output(0), StringComparison.Ordinal);
    }

    // ---- Backlog down -----------------------------------------------------------------

    /// <summary>Nothing listening on the port: every mode exits 0 at once with no output, so a
    /// session never waits on Backlog being up.</summary>
    [Theory]
    [InlineData("pre")]
    [InlineData("post")]
    [InlineData("stop")]
    [InlineData("prompt")]
    [InlineData("end")]
    public void Every_mode_exits_quietly_while_Backlog_is_down(string mode)
    {
        var transcript = Transcript("Shall I go on?");
        var input = PreInput();
        input["transcript_path"] = transcript;
        input["prompt"] = "yes";

        var run = Run(new JsonObject(), new Dictionary<string, string>(), down: true, Step(mode, input));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        Assert.True(run.Elapsed(0) < 10_000, $"{mode} took {run.Elapsed(0)} ms with Backlog down.");
    }

    /// <summary>A refusal from the tool is an error like any other: no output, exit 0.</summary>
    [Fact]
    public void Pre_lets_the_dialog_open_when_ask_is_refused()
    {
        var run = Run(
            Replies(("ask", [new JsonObject { ["error"] = "notifications.not_available" }])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        Assert.Single(run.Calls);
    }

    /// <summary>Refused mid-wait — the relay switched off, the question gone — is not Backlog
    /// being down: the hook stops waiting at once and the dialog opens, rather than holding it
    /// shut for the whole dialog wait.</summary>
    [Fact]
    public void Pre_stops_waiting_when_get_answer_is_refused()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "backlog")]),
                ("get_answer", [new JsonObject { ["error"] = "notifications.not_found" }])),
            Step("pre", PreInput()));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        Assert.Equal(new[] { "ask", "get_answer" }, run.Calls.Select(call => call.Name));
        Assert.True(run.Elapsed(0) < 10_000, $"pre took {run.Elapsed(0)} ms after a refusal.");
        Assert.Empty(StateFiles());
    }

    // ---- post -------------------------------------------------------------------------

    /// <summary>Answered in the dialog: the question is closed with what was picked.</summary>
    [Fact]
    public void Post_closes_the_dialog_question_answered_in_session_with_its_answers()
    {
        var post = new JsonObject
        {
            ["session_id"] = SessionId,
            ["hook_event_name"] = "PostToolUse",
            ["tool_name"] = "AskUserQuestion",
            ["tool_use_id"] = ToolUseId,
            ["tool_input"] = new JsonObject { ["questions"] = Questions() },
            ["tool_response"] = new JsonObject { ["questions"] = Questions(), ["answers"] = new JsonObject { [Question] = "Cosmos" } },
        };

        var run = Run(
            Replies(("ask", [Asked(DialogId, "session")]), ("close", [Status("closed")])),
            Step("pre", PreInput()),
            Step("post", post));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(1));
        var close = run.Calls[^1];
        Assert.Equal("close", close.Name);
        Assert.Equal(DialogId, close.Arguments["id"]!.GetValue<string>());
        Assert.Equal(SessionId, close.Arguments["sessionId"]!.GetValue<string>());
        Assert.Equal("answered-in-session", close.Arguments["reason"]!.GetValue<string>());
        Assert.Equal("Cosmos", close.Arguments["answers"]![Question]!.GetValue<string>());
        Assert.Empty(StateFiles());
    }

    /// <summary>A dialog this script never registered — Backlog was down at PreToolUse — has
    /// nothing to close.</summary>
    [Fact]
    public void Post_calls_nothing_for_a_dialog_it_did_not_register()
    {
        var post = PreInput();
        post["tool_response"] = new JsonObject { ["answers"] = new JsonObject { [Question] = "Cosmos" } };

        var run = Run(Replies(), Step("post", post));

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Calls);
    }

    // ---- stop -------------------------------------------------------------------------

    /// <summary>The turn ended on a question: it is asked as one prose question.</summary>
    [Fact]
    public void Stop_asks_a_prose_question_when_the_last_message_ends_in_one()
    {
        const string message = "The migration is ready. Should I run it against the shared database?";

        var run = Run(
            Replies(("ask", [Asked(ProseId, "session")])),
            Step("stop", StopInput(Transcript(message))));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(0));
        var ask = Assert.Single(run.Calls);
        Assert.Equal("ask", ask.Name);
        Assert.Equal("prose", ask.Arguments["kind"]!.GetValue<string>());
        Assert.Equal(SessionId, ask.Arguments["sessionId"]!.GetValue<string>());
        Assert.Null(ask.Arguments["toolUseId"]);
        var question = Assert.Single(ask.Arguments["questions"]!.AsArray())!;
        Assert.Equal(message, question["question"]!.GetValue<string>());
    }

    /// <summary>A question earlier in the message is not the turn ending on one.</summary>
    [Theory]
    [InlineData("Should I go on? I went on and it is done.")]
    [InlineData("Done.")]
    public void Stop_asks_nothing_when_the_last_sentence_is_not_a_question(string message)
    {
        var run = Run(Replies(("ask", [Asked(ProseId, "session")])), Step("stop", StopInput(Transcript(message))));

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Calls);
    }

    /// <summary>Trailing emphasis does not hide the question mark.</summary>
    [Fact]
    public void Stop_reads_a_question_through_trailing_emphasis()
    {
        var run = Run(
            Replies(("ask", [Asked(ProseId, "session")])),
            Step("stop", StopInput(Transcript("**Which of the two do you want?**"))));

        Assert.Equal("ask", Assert.Single(run.Calls).Name);
    }

    // ---- prompt -----------------------------------------------------------------------

    /// <summary>The next prompt answers the prose question in the session.</summary>
    [Fact]
    public void Prompt_closes_the_open_prose_question_with_the_prompt_as_its_answer()
    {
        const string message = "Should I run it?";

        var run = Run(
            Replies(("ask", [Asked(ProseId, "session")]), ("close", [Status("closed")])),
            Step("stop", StopInput(Transcript(message))),
            Step("prompt", new JsonObject { ["session_id"] = SessionId, ["hook_event_name"] = "UserPromptSubmit", ["prompt"] = "Yes, run it." }));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Output(1));
        var close = run.Calls[^1];
        Assert.Equal("close", close.Name);
        Assert.Equal(ProseId, close.Arguments["id"]!.GetValue<string>());
        Assert.Equal("answered-in-session", close.Arguments["reason"]!.GetValue<string>());
        Assert.Equal("Yes, run it.", close.Arguments["answers"]![message]!.GetValue<string>());
        Assert.Empty(StateFiles());
    }

    /// <summary>A prompt with no prose question open closes nothing — not an open dialog.</summary>
    [Fact]
    public void Prompt_leaves_dialog_questions_alone()
    {
        var run = Run(
            Replies(("ask", [Asked(DialogId, "session")])),
            Step("pre", PreInput()),
            Step("prompt", new JsonObject { ["session_id"] = SessionId, ["prompt"] = "go on" }));

        Assert.Equal(new[] { "ask" }, run.Calls.Select(call => call.Name));
    }

    // ---- end --------------------------------------------------------------------------

    /// <summary>The session ends: every question it left open is closed session-ended.</summary>
    [Fact]
    public void End_closes_every_open_question_of_the_session()
    {
        var run = Run(
            Replies(
                ("ask", [Asked(DialogId, "session"), Asked(ProseId, "session")]),
                ("close", [Status("closed")])),
            Step("pre", PreInput()),
            Step("stop", StopInput(Transcript("Shall I?"))),
            Step("end", new JsonObject { ["session_id"] = SessionId, ["hook_event_name"] = "SessionEnd", ["reason"] = "exit" }));

        Assert.Equal(0, run.ExitCode);
        var closes = run.Calls.Where(call => call.Name == "close").ToList();
        Assert.Equal(new[] { DialogId, ProseId }, closes.Select(call => call.Arguments["id"]!.GetValue<string>()).Order());
        Assert.All(closes, call => Assert.Equal("session-ended", call.Arguments["reason"]!.GetValue<string>()));
        Assert.Empty(StateFiles());
    }

    // ---- harness ----------------------------------------------------------------------

    private JsonObject StopInput(string transcript) => new()
    {
        ["session_id"] = SessionId,
        ["hook_event_name"] = "Stop",
        ["stop_hook_active"] = false,
        ["transcript_path"] = transcript,
    };

    /// <summary>A Claude Code transcript whose last assistant message is <paramref name="message"/>,
    /// after a tool-use-only assistant entry and before nothing.</summary>
    private string Transcript(string message)
    {
        var path = Path.Combine(_root, $"transcript-{Guid.NewGuid():N}.jsonl");
        var lines = new[]
        {
            new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = "Do the thing." } },
            new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }) },
            },
            new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["name"] = "Bash" }) },
            },
        };
        File.WriteAllLines(path, lines.Select(line => line.ToJsonString()));
        return path;
    }

    private string[] StateFiles()
    {
        var folder = Path.Combine(_root, "state");
        return Directory.Exists(folder) ? Directory.GetFiles(folder) : [];
    }

    private static JsonObject Replies(params (string Tool, JsonObject[] Replies)[] replies)
    {
        var map = new JsonObject();
        foreach (var (tool, list) in replies) map[tool] = new JsonArray([.. list]);
        return map;
    }

    private static JsonObject Step(string mode, JsonObject input) => new() { ["mode"] = mode, ["input"] = input };

    private Result Run(JsonObject replies, params JsonObject[] steps) =>
        Run(replies, new Dictionary<string, string>(), down: false, steps);

    private Result Run(JsonObject replies, Dictionary<string, string> env, params JsonObject[] steps) =>
        Run(replies, env, down: false, steps);

    private Result Run(JsonObject replies, Dictionary<string, string> env, bool down, params JsonObject[] steps)
    {
        var environment = new JsonObject
        {
            ["BACKLOG_MCP_TOKEN"] = "test-token",
            ["ASK_BACKLOG_STATE_DIR"] = Path.Combine(_root, "state"),
            ["ASK_BACKLOG_RETRY_MS"] = "",
            // Never this machine's own settings.json, whatever the test sets.
            ["LOCALAPPDATA"] = _root,
            ["XDG_DATA_HOME"] = _root,
        };
        foreach (var (key, value) in env) environment[key] = value;

        var config = new JsonObject
        {
            ["script"] = _script,
            ["down"] = down,
            ["replies"] = replies,
            ["env"] = environment,
            ["steps"] = new JsonArray([.. steps]),
        };

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This test runs ask-backlog.mjs and needs Node on the PATH: {exception.Message}");
            return null!;
        }

        using (process)
        {
            process.StandardInput.Write(Driver.Replace("/*CONFIG*/", config.ToJsonString(), StringComparison.Ordinal));
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(60)), "node did not finish in sixty seconds.");
            Assert.True(process.ExitCode == 0, $"the driver failed ({process.ExitCode}):\n{error.Result}");

            return Result.Parse(output.Result);
        }
    }

    private sealed record Call(string Name, JsonObject Arguments, string? Authorization, long At);

    private sealed record Result(int ExitCode, IReadOnlyList<Call> Calls, JsonArray Steps)
    {
        public string Output(int step) => Steps[step]!["stdout"]!.GetValue<string>();

        public long Elapsed(int step) => Steps[step]!["elapsedMs"]!.GetValue<long>();

        public static Result Parse(string json)
        {
            var root = JsonNode.Parse(json)!;
            var steps = root["steps"]!.AsArray();
            var calls = root["calls"]!.AsArray()
                .Select(call => new Call(
                    call!["name"]!.GetValue<string>(),
                    call["arguments"]!.AsObject(),
                    call["authorization"]?.GetValue<string>(),
                    call["at"]!.GetValue<long>()))
                .ToList();
            var exit = steps.Select(step => step!["exitCode"]!.GetValue<int>()).DefaultIfEmpty(0).Max();
            return new Result(exit, calls, steps);
        }
    }

    /// <summary>
    /// The fake MCP endpoint and the hook runs, in one Node process: an HTTP server on a free
    /// loopback port answering each <c>tools/call</c> with the next reply scripted for that tool
    /// (the last one repeats) as one server-sent event, then the script run once per step with
    /// the step's hook input on stdin. With <c>down</c> the port is closed again before any step
    /// runs, so every call is refused.
    /// </summary>
    private const string Driver = """
        const http = require('node:http');
        const { spawn } = require('node:child_process');
        const config = /*CONFIG*/;
        const calls = [];
        const queues = config.replies;

        const server = http.createServer((req, res) => {
            let body = '';
            req.on('data', chunk => body += chunk);
            req.on('end', () => {
                const message = JSON.parse(body);
                const name = message.params.name;
                calls.push({ name, arguments: message.params.arguments, authorization: req.headers.authorization ?? null, at: Date.now() });
                const queue = queues[name] ?? [];
                const reply = queue.length > 1 ? queue.shift() : queue[0];
                const answer = () => {
                    if (!reply || reply.status) { res.writeHead(reply?.status ?? 500); res.end(); return; }
                    const result = reply.error
                        ? { isError: true, content: [{ type: 'text', text: reply.error }] }
                        : { content: [{ type: 'text', text: JSON.stringify(reply.result) }] };
                    res.writeHead(200, { 'content-type': 'text/event-stream' });
                    res.end('event: message\ndata: ' + JSON.stringify({ jsonrpc: '2.0', id: message.id, result }) + '\n\n');
                };
                setTimeout(answer, reply?.delayMs ?? 0);
            });
        });

        function runStep(step, port) {
            return new Promise(resolve => {
                const started = Date.now();
                const env = { ...process.env, ...config.env, BACKLOG_MCP_PORT: String(port) };
                const child = spawn(process.execPath, [config.script, step.mode], { env });
                let stdout = '';
                child.stdout.on('data', chunk => stdout += chunk);
                child.on('exit', code => resolve({ mode: step.mode, exitCode: code, stdout, elapsedMs: Date.now() - started }));
                child.stdin.end(JSON.stringify(step.input));
            });
        }

        server.listen(0, '127.0.0.1', async () => {
            const port = server.address().port;
            if (config.down) await new Promise(resolve => server.close(resolve));
            const steps = [];
            for (const step of config.steps) steps.push(await runStep(step, port));
            if (!config.down) server.close();
            process.stdout.write(JSON.stringify({ steps, calls }));
            process.exit(0);
        });
        """;
}
