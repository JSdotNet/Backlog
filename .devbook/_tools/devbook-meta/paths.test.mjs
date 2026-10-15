// paths.test.mjs — resolveInside keeps a path that stays inside the
// repository and refuses one that leaves it, by a climb, an absolute path, or a
// sibling folder sharing the root's name as a prefix.
//
// Run: node --test plugins/devbook/tools/devbook-meta/paths.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import path from "node:path";

import { OUTSIDE_ROOT, resolveInside } from "./paths.mjs";

const root = path.resolve(tmpdir(), "repo");

test("a path inside the root resolves against it", () => {
    assert.equal(resolveInside(root, ".devbook/scenarios/profiles.json"), path.join(root, ".devbook/scenarios/profiles.json"));
    assert.equal(resolveInside(root, ""), root);
    assert.equal(resolveInside(root, "e2e/../tests/x.spec.ts"), path.join(root, "tests/x.spec.ts"));
    assert.equal(resolveInside(root, "..not-a-climb.md"), path.join(root, "..not-a-climb.md"));
    assert.equal(resolveInside(root, path.join(root, "e2e")), path.join(root, "e2e"));
});

test("a path outside the root is refused with OUTSIDE_ROOT", () => {
    for (const given of ["..", "../secret", "e2e/../../secret", path.resolve(root, "../repo-sibling/x.md"), path.parse(root).root]) {
        assert.throws(() => resolveInside(root, given), (error) => error.code === OUTSIDE_ROOT && /resolves outside the repository/.test(error.message), given);
    }
});
