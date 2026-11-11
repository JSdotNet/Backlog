// A SKILL.md whose frontmatter does not parse as YAML loads with every field dropped, silently.
// The usual cause is an unquoted value holding ": " or " #", so this flags any plain scalar that does.
//   node --test plugins/backlog-tools/tests/skill-frontmatter.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SKILLS = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills");

const skills = fs
  .readdirSync(SKILLS)
  .map((name) => ({ name, file: path.join(SKILLS, name, "SKILL.md") }))
  .filter(({ file }) => fs.existsSync(file));

for (const { name, file } of skills) {
  test(`${name} frontmatter has no plain scalar YAML would misread`, () => {
    const frontmatter = fs.readFileSync(file, "utf8").match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1];
    assert.ok(frontmatter, "no frontmatter block");
    for (const line of frontmatter.split(/\r?\n/)) {
      const value = line.match(/^[\w-]+:\s+(.*)$/)?.[1];
      if (!value || /^['"|>]/.test(value)) continue;
      assert.doesNotMatch(value, /: | #/, `quote this value: ${line.slice(0, 60)}…`);
    }
  });
}
