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

// standing-session.md's table: one row per loop skill, `| skill | title | kind |`.
const rowOf = (name) =>
  standing.match(new RegExp("^\\| `" + name + "` \\| `([^`]+)` \\| `([^`]+)` \\|$", "m"));

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
  test(`${name} checks in first on every tick, as the kind check_in accepts`, () => {
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

  test(`${name} stays within the 40-line SKILL.md body budget`, () => {
    const lines = body.replace(/\s+$/, "").split(/\r?\n/).length;
    assert.ok(lines <= 40, `${name} body is ${lines} lines`);
  });
}
