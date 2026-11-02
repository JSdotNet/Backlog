// The devbook sweep's pickup: units.mjs's groups joined with the directions Backlog keeps.
//   node --test plugins/backlog-tools/tests/devbook-sweep-select.test.mjs

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { rollUp, selectGroups, unitIds } from "../skills/devbook-sweep/assets/select-groups.mjs";

const SCRIPT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills", "devbook-sweep", "assets", "select-groups.mjs");

const unit = (id, extra = {}) => ({ id, kind: "aggregate", label: id, context: null, chapters: [id], files: [id.split("#")[0]], ...extra });
const group = (id, units, extra = {}) => ({ id, slug: id.split("#")[1] ?? id, direction: null, setAside: null, chapters: units.length, ties: [], units, ...extra });
const row = (id, sync, syncFrom = null, syncLevel = syncFrom ? "context" : "default") => ({ unit: id, sync, syncFrom, syncLevel });

const listing = {
    version: 1,
    groups: [
        group(".devbook/domain/inbox/domain.md#inbox-item", [unit(".devbook/domain/inbox/domain.md#inbox-item")]),
        group(".devbook/domain/sessions/domain.md#cost", [unit(".devbook/domain/sessions/domain.md#cost")]),
        group(".devbook/domain/tasks/domain.md#task", [unit(".devbook/domain/tasks/domain.md#task", { sync: "pull", syncFrom: ".devbook/domain/tasks/context.md" })]),
        group(".devbook/domain/both/domain.md#a", [unit(".devbook/domain/both/domain.md#a"), unit(".devbook/domain/both/domain.md#b")]),
    ],
    setAside: [
        group(".devbook/domain/big/domain.md#huge", [unit(".devbook/domain/big/domain.md#huge")], { setAside: "oversized", chapters: 99 }),
        group(".devbook/domain/was-mixed/domain.md#x", [unit(".devbook/domain/was-mixed/domain.md#x"), unit(".devbook/domain/was-mixed/domain.md#y")], { setAside: "mixed" }),
    ],
    orphans: [{ chapter: ".devbook/domain/inbox/domain.md#loose", reason: "no related" }],
};

const answer = {
    repository: "JSdotNet/Backlog",
    units: [
        row(".devbook/domain/inbox/domain.md#inbox-item", "pull", ".devbook/domain/inbox/context.md"),
        row(".devbook/domain/sessions/domain.md#cost", "push", ".devbook/domain/sessions/context.md"),
        // tasks: the map says nothing, so the Markdown's pull above must not survive.
        row(".devbook/domain/both/domain.md#a", "sync", ".devbook/domain/both/domain.md#a", "unit"),
        row(".devbook/domain/both/domain.md#b", "push", ".devbook/domain/both/context.md"),
        row(".devbook/domain/big/domain.md#huge", "pull", ".devbook/domain/context-map.md", "folder"),
        row(".devbook/domain/was-mixed/domain.md#x", "pull", ".devbook/domain/was-mixed/context.md"),
        row(".devbook/domain/was-mixed/domain.md#y", "pull", ".devbook/domain/was-mixed/context.md"),
    ],
    stated: [],
};

test("rollUp keeps units.mjs's rule", () => {
    assert.equal(rollUp(["pull", "pull"]), "pull");
    assert.equal(rollUp(["sync", "push"]), "push");
    assert.equal(rollUp(["sync", "sync"]), "sync");
    assert.equal(rollUp(["pull", "push"]), null);
    assert.equal(rollUp([]), null);
});

test("pull picks up the groups the map sends pull or sync, with the map's source", () => {
    const pull = selectGroups(listing, answer, "pull");
    assert.deepEqual(pull.groups.map((g) => g.id), [
        ".devbook/domain/inbox/domain.md#inbox-item",
        ".devbook/domain/was-mixed/domain.md#x",
    ]);
    const inbox = pull.groups[0].units[0];
    assert.equal(inbox.sync, "pull");
    assert.equal(inbox.syncFrom, ".devbook/domain/inbox/context.md");
    assert.equal(inbox.syncLevel, "context");
    assert.deepEqual(pull.setAside.map((g) => [g.id, g.setAside]), [[".devbook/domain/big/domain.md#huge", "oversized"]]);
    assert.equal(pull.orphans.length, 1);
});

test("push picks up push and a sync unit grouped with push, and never the Markdown's direction", () => {
    const push = selectGroups(listing, answer, "push");
    assert.deepEqual(push.groups.map((g) => [g.id, g.direction]), [
        [".devbook/domain/both/domain.md#a", "push"],
        [".devbook/domain/sessions/domain.md#cost", "push"],
    ]);
    assert.ok(!push.groups.some((g) => g.id.includes("/tasks/")));
});

test("a unit the map did not answer for is report, and a group whose units meet two ways is set aside", () => {
    const tasks = selectGroups(listing, { units: [] }, "pull");
    assert.equal(tasks.groups.length, 0);

    const mixed = selectGroups(listing, { units: [row(".devbook/domain/both/domain.md#a", "pull"), row(".devbook/domain/both/domain.md#b", "push")] }, "pull");
    assert.deepEqual(mixed.setAside.map((g) => [g.id, g.setAside]), [[".devbook/domain/both/domain.md#a", "mixed"]]);
});

test("a group units.mjs set aside as mixed keeps its size when the map agrees on one direction", () => {
    const big = {
        maxGroupChapters: 40,
        groups: [],
        setAside: [group(".devbook/domain/tied/domain.md#a", [unit(".devbook/domain/tied/domain.md#a"), unit(".devbook/domain/tied/domain.md#b")], { setAside: "mixed", chapters: 45 })],
    };
    const pull = selectGroups(big, { units: [row(".devbook/domain/tied/domain.md#a", "pull"), row(".devbook/domain/tied/domain.md#b", "pull")] }, "pull");
    assert.equal(pull.groups.length, 0);
    assert.deepEqual(pull.setAside.map((g) => [g.id, g.setAside, g.direction]), [[".devbook/domain/tied/domain.md#a", "oversized", "pull"]]);
});

test("unitIds lists every unit once, set-asides included", () => {
    assert.equal(unitIds(listing).length, 8);
});

test("the command line prints the selection and the unit ids", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "devbook-sweep-"));
    const groups = path.join(dir, "groups.json");
    const directions = path.join(dir, "directions.json");
    fs.writeFileSync(groups, JSON.stringify(listing));
    fs.writeFileSync(directions, JSON.stringify(answer));

    const ids = spawnSync(process.execPath, [SCRIPT, "--unit-ids", groups], { encoding: "utf8" });
    assert.equal(ids.status, 0, ids.stderr);
    assert.equal(JSON.parse(ids.stdout).length, 8);

    const run = spawnSync(process.execPath, [SCRIPT, "--direction", "push", "--groups", groups, "--directions", directions], { encoding: "utf8" });
    assert.equal(run.status, 0, run.stderr);
    assert.equal(JSON.parse(run.stdout).groups.length, 2);

    const bad = spawnSync(process.execPath, [SCRIPT, "--direction", "report", "--groups", groups, "--directions", directions], { encoding: "utf8" });
    assert.notEqual(bad.status, 0);
});
