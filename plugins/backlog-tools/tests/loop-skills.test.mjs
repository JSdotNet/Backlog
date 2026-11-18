// A skill a person starts as `/loop /backlog-tools:<name>` must stay model-invocable: /loop runs
// its inner command through the Skill tool, which refuses a skill marked
// `disable-model-invocation: true`, so that start would fail on its first tick.
//   node --test plugins/backlog-tools/tests/loop-skills.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SKILLS = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills");
const standing = fs.readFileSync(path.join(SKILLS, "..", "resources", "standing-session.md"), "utf8");

const loopStarted = fs
  .readdirSync(SKILLS)
  .map((name) => ({ name, file: path.join(SKILLS, name, "SKILL.md") }))
  .filter(({ file }) => fs.existsSync(file))
  .map(({ name, file }) => ({ name, text: fs.readFileSync(file, "utf8") }))
  .filter(({ name, text }) => text.includes(`/loop /backlog-tools:${name}`));

// standing-session.md's table: one row per loop skill, `| skill | title | kind |`, the kind `—`
// for a loop `check_in` accepts no kind for yet.
const rowOf = (name) =>
  standing.match(new RegExp("^\\| `" + name + "` \\| `([^`]+)` \\| (?:`([^`]+)`|—) \\|$", "m"));

const section = (text, heading) =>
  text.split(/^## /m).find((part) => part.startsWith(heading)) ?? "";

test("at least one skill is started under /loop", () => {
  assert.ok(loopStarted.length > 0);
});

// The check-in rule is stated once, in standing-session.md, for every loop skill (local ADR 0051 §3).
test("standing-session.md says what each check_in command makes a loop do", () => {
  const checkIn = section(standing, "Check in");
  assert.ok(checkIn, "standing-session.md has no ## Check in section");
  assert.match(checkIn, /`check_in`/);
  assert.match(checkIn, /every tick/i);
  for (const argument of ["kind", "sessionId", "state", "heartbeatMinutes"]) {
    assert.match(checkIn, new RegExp("`" + argument + "`"), `the Check in section never sets ${argument}`);
  }
  // Each command's bullet says what the loop does with it.
  const bullet = (command) => checkIn.match(new RegExp("^- \\*\\*`" + command + "`\\*\\*.*(?:\\r?\\n  .*)*", "m"))?.[0] ?? "";
  assert.match(bullet("stop"), /do no work[\s\S]*End the loop[\s\S]*say so/);
  assert.match(bullet("pause"), /do no work[\s\S]*heartbeat/);
  assert.match(bullet("resume"), /carry on/);
});

test("standing-session.md stays within the 60-line contract body budget", () => {
  const lines = standing.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, "").trim().split(/\r?\n/).length;
  assert.ok(lines <= 60, `standing-session.md body is ${lines} lines`);
});

for (const { name, text } of loopStarted) {
  const body = text.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, "").replace(/^\s*\r?\n/, "");

  test(`${name} is started under /loop, so the Skill tool may invoke it`, () => {
    const frontmatter = text.match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? "";
    assert.doesNotMatch(frontmatter, /^disable-model-invocation:\s*true\s*$/m);
  });

  // A long-running agent marks its session (icon, ALL CAPS title, pinned, Agents section).
  test(`${name} marks its session per standing-session.md, which names its title`, () => {
    assert.match(text, /\.\.\/\.\.\/resources\/standing-session\.md/);
    const row = rowOf(name);
    assert.ok(row, `standing-session.md has no row for ${name}`);
    const [icon, ...words] = row[1].split(" ");
    assert.match(icon, /\p{Extended_Pictographic}/u);
    assert.equal(words.join(" "), words.join(" ").toUpperCase());
  });

  // Every tick opens with a check-in, so the person's pause, resume or stop reaches the loop.
  test(`${name} checks in first on every tick, as the kind check_in accepts`, (t) => {
    if (rowOf(name) && rowOf(name)[2] === undefined) {
      // No kind yet: standing-session.md says such a loop does not check in, so it must not try.
      assert.doesNotMatch(body, /`check_in`|#check-in/);
      return t.skip(`${name} has no check_in kind yet`);
    }
    const kind = rowOf(name)?.[2];
    assert.ok(["dispatcher", "brain-tender"].includes(kind), `${name}'s kind is ${kind}`);
    const steps = body.split(/^## /m).find((part) => /^Each (tick|turn)\r?\n/.test(part)) ?? "";
    const first = steps.match(/^(\d+)\. \*\*([^*]+)\*\*(.*)$/m);
    assert.ok(first, `${name} has no numbered step`);
    assert.equal(first[2].trim().replace(/\.$/, ""), "Check in");
    assert.match(steps, /\.\.\/\.\.\/resources\/standing-session\.md#check-in/);
    // The host substitutes the session's own id, the one check_in keys a loop by.
    assert.match(steps, /\$\{CLAUDE_SESSION_ID\}/);
  });

  // A Manager's wait reaches Backlog far more often than a tick, so it is a check-in too.
  if (name === "dispatch-sessions") {
    test("dispatch-sessions arms its wait with its own session id", () => {
      assert.match(body, /node assets\/wait-requests\.mjs --session \$\{CLAUDE_SESSION_ID\}/);
    });

    // The app answers room against the Manager's own limits (local ADR 0051 §4), so the check-in
    // carries them, and the skill measures a figure itself only when the answer cannot.
    const step = (n) => body.match(new RegExp("^" + n + "\\. \\*\\*[\\s\\S]*?(?=^\\d+\\. \\*\\*|^## )", "m"))?.[0] ?? "";

    test("dispatch-sessions sends its arguments as the check_in limits", () => {
      const checkIn = step(1);
      for (const [limit, argument] of [
        ["maxSessions", "sessions"],
        ["maxUsagePercent", "usage"],
        ["maxCpuPercent", "cpu"],
        ["minFreeMemoryGb", "memory"],
      ]) {
        assert.match(checkIn, new RegExp("`" + limit + "` `" + argument + "`"), `step 1 never sends ${limit} as ${argument}`);
      }
      assert.match(checkIn, /`holding` if a limit held the last queue/);
    });

    test("dispatch-sessions reads room from the check-in answer's capacity", () => {
      const capacity = step(6);
      assert.match(capacity, /`capacity`/);
      assert.match(capacity, /`hasRoom: false`/);
      assert.match(capacity, /step 1/);
      // A start made since that answer counts against `sessions`.
      assert.match(capacity, /start[\s\S]*since[\s\S]*`sessions`/);
    });

    test("dispatch-sessions measures a figure itself only as the fallback", () => {
      const capacity = step(6);
      const fallback = capacity.search(/no `capacity`/);
      assert.ok(fallback >= 0, "step 6 never names the no-capacity fallback");
      // The MCP serializer omits a null property, so an unmeasured figure arrives absent, not `null`.
      assert.match(capacity, /absent or `null`/);
      for (const [probe, figure] of [
        ["list_sessions", "workingSessions"],
        ["get_usage", "planUsagePercent"],
        ["capacity.ps1", "cpuPercent"],
        ["capacity.ps1", "freeMemoryGb"],
      ]) {
        const at = capacity.indexOf(probe);
        assert.ok(at > fallback, `${probe} is not behind the no-capacity condition`);
        assert.match(capacity, new RegExp("`" + figure + "`"), `step 6 never tags the figure ${figure}`);
      }
      assert.ok(body.indexOf("get_usage") > body.indexOf("no `capacity`"));
      assert.ok(body.indexOf("capacity.ps1") > body.indexOf("no `capacity`"));
    });
  }

  test(`${name} stays within the 40-line SKILL.md body budget`, () => {
    const lines = body.replace(/\s+$/, "").split(/\r?\n/).length;
    assert.ok(lines <= 40, `${name} body is ${lines} lines`);
  });
}
