// Every backlog tool track-goals names must be one the server has: each backticked snake_case
// name in its SKILL.md matches a tool-name constant in the MCP project, so a renamed or misspelt
// tool fails here rather than on a person's tick. A one-word name (`ask`) reads like any other
// word in backticks, so it is not checked.
//   node --test plugins/backlog-tools/tests/track-goals-tools.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PLUGIN = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const MCP = path.join(PLUGIN, "..", "..", "src", "Infrastructure", "Backlog.Infrastructure.Mcp");

const skill = fs.readFileSync(path.join(PLUGIN, "skills", "track-goals", "SKILL.md"), "utf8");
const named = [...new Set([...skill.matchAll(/`([a-z]+(?:_[a-z]+)+)`/g)].map((match) => match[1]))];

const tools = new Set(
  fs
    .readdirSync(MCP)
    .filter((file) => file.endsWith(".cs"))
    .flatMap((file) => [...fs.readFileSync(path.join(MCP, file), "utf8").matchAll(/const string \w+ = "([a-z_]+)"/g)])
    .map((match) => match[1]),
);

test("track-goals reads the goals and iterations through read_iterations", () => {
  assert.ok(named.includes("read_iterations"), `named: ${named.join(", ")}`);
});

for (const name of named) {
  test(`${name}, named by track-goals, is a backlog tool`, () => {
    assert.ok(tools.has(name), `no const string … = "${name}" in ${MCP}`);
  });
}
