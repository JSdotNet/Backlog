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

const loopStarted = fs
  .readdirSync(SKILLS)
  .map((name) => ({ name, file: path.join(SKILLS, name, "SKILL.md") }))
  .filter(({ file }) => fs.existsSync(file))
  .map(({ name, file }) => ({ name, text: fs.readFileSync(file, "utf8") }))
  .filter(({ name, text }) => text.includes(`/loop /backlog-tools:${name}`));

test("at least one skill is started under /loop", () => {
  assert.ok(loopStarted.length > 0);
});

for (const { name, text } of loopStarted) {
  test(`${name} is started under /loop, so the Skill tool may invoke it`, () => {
    const frontmatter = text.match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? "";
    assert.doesNotMatch(frontmatter, /^disable-model-invocation:\s*true\s*$/m);
  });

  // A long-running agent marks its session (icon, ALL CAPS title, pinned, Agents section).
  test(`${name} marks its session per standing-session.md, which names its title`, () => {
    assert.match(text, /\.\.\/\.\.\/resources\/standing-session\.md/);
    const standing = fs.readFileSync(path.join(SKILLS, "..", "resources", "standing-session.md"), "utf8");
    const row = standing.match(new RegExp("^\\| `" + name + "` \\| `([^`]+)` \\|$", "m"));
    assert.ok(row, `standing-session.md has no title row for ${name}`);
    const [icon, ...words] = row[1].split(" ");
    assert.match(icon, /\p{Extended_Pictographic}/u);
    assert.equal(words.join(" "), words.join(" ").toUpperCase());
  });
}
