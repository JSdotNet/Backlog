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

for (const file of ["hooks/backlog-grant.mjs", "tests/fake-backlog-app.mjs"]) {
  test(`${file} is backlog-tools' own, unchanged`, () => {
    const own = fs.readFileSync(path.join(ROOT, file), "utf8").replace(/\r\n/g, "\n");
    const shared = fs.readFileSync(path.join(SHARED, file), "utf8").replace(/\r\n/g, "\n");
    assert.equal(own, shared, `edit plugins/backlog-tools/${file} and copy it here`);
  });
}
