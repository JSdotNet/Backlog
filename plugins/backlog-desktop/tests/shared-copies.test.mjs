// An installed plugin reaches no sibling's files, so backlog-desktop ships its own copy of what it
// shares with backlog-tools. The copies must not drift: each is the backlog-tools file, byte for byte.
//   node --test plugins/backlog-desktop/tests/shared-copies.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const SHARED = path.join(ROOT, "..", "backlog-tools");

// The dispatcher's host-neutral assets live in backlog-tools/resources/dispatch/assets; the
// dispatch-sessions skill, their Claude Desktop adapter, runs its copies from its own assets folder.
const DISPATCH = "resources/dispatch/assets";
const ADAPTER = "skills/dispatch-sessions/assets";
const dispatchAssets = fs.readdirSync(path.join(SHARED, DISPATCH)).filter((file) => file !== "fake-session-host.mjs");

const copies = [
  ["hooks/backlog-grant.mjs", "hooks/backlog-grant.mjs"],
  ["tests/fake-backlog-app.mjs", "tests/fake-backlog-app.mjs"],
  ...dispatchAssets.map((file) => [`${ADAPTER}/${file}`, `${DISPATCH}/${file}`]),
];

for (const [own, shared] of copies) {
  test(`${own} is backlog-tools' ${shared}, unchanged`, () => {
    const mine = fs.readFileSync(path.join(ROOT, own), "utf8").replace(/\r\n/g, "\n");
    const theirs = fs.readFileSync(path.join(SHARED, shared), "utf8").replace(/\r\n/g, "\n");
    assert.equal(mine, theirs, `edit plugins/backlog-tools/${shared} and copy it to plugins/backlog-desktop/${own}`);
  });
}

test("the adapter's assets folder holds every shared dispatch asset and the adapter alone besides", () => {
  const held = fs.readdirSync(path.join(ROOT, ADAPTER)).sort();
  assert.deepEqual(held, [...dispatchAssets, "claude-desktop-host.mjs"].sort());
});
