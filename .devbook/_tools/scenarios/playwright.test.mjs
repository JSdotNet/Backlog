// Loading the tools from a suite that is its own npm package: `@playwright/test`
// resolved from where Playwright runs rather than from this folder, and the
// folder's package.json, which makes Playwright load setup.ts and shot.ts as
// ES modules.
//
// Run: `node --test playwright.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { PACKAGE, playwrightBases, resolvePlaywright } from "./playwright.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));

test("the bases are the running script, then the working folder, then the tools folder", () => {
    const bases = playwrightBases({ script: "/repo/.test/node_modules/playwright/lib/common/process.js", cwd: "/repo/.test", here: "/repo/.devbook/_tools/scenarios/playwright.mjs" });
    assert.deepEqual(bases, [
        path.resolve("/repo/.test/node_modules/playwright/lib/common/process.js"),
        path.join(path.resolve("/repo/.test"), "noop.js"),
        "/repo/.devbook/_tools/scenarios/playwright.mjs",
    ]);
});

test("the first base that has the package wins; none is an error naming every base", () => {
    const resolve = (base) => {
        if (base.startsWith("/suite")) return `/suite/node_modules/${PACKAGE}/index.js`;
        throw new Error("not found");
    };
    assert.equal(resolvePlaywright(["/elsewhere/a.js", "/suite/b.js", "/root/c.js"], resolve), `/suite/node_modules/${PACKAGE}/index.js`);
    assert.throws(() => resolvePlaywright(["/elsewhere/a.js"], resolve), /cannot find @playwright\/test from \/elsewhere\/a\.js/);
});

test("a suite with its own node_modules resolves its copy, which the tools folder cannot see", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-playwright-"));
    try {
        const installed = path.join(root, ".test/node_modules", PACKAGE);
        await mkdir(installed, { recursive: true });
        await writeFile(path.join(installed, "package.json"), JSON.stringify({ name: PACKAGE, main: "index.js" }));
        await writeFile(path.join(installed, "index.js"), "module.exports = { test: 'suite copy' };\n");
        const tools = path.join(root, ".devbook/_tools/scenarios/playwright.mjs");
        assert.throws(() => resolvePlaywright([tools]));
        const bases = playwrightBases({ script: path.join(root, ".test/node_modules/.bin/playwright"), cwd: path.join(root, ".test"), here: tools });
        assert.equal(resolvePlaywright(bases), path.join(installed, "index.js"));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("the folder is an ES module package, so Playwright loads setup.ts and its .mjs imports as ESM", async () => {
    const manifest = JSON.parse(await readFile(path.join(HERE, "package.json"), "utf8"));
    assert.equal(manifest.type, "module");
    for (const file of ["setup.ts", "shot.ts"]) {
        const source = await readFile(path.join(HERE, file), "utf8");
        assert.doesNotMatch(source, /^import (?!type )[^;]*from '@playwright\/test'/m, `${file} imports @playwright/test only as a type`);
        assert.match(source, /playwrightTest\(\)/, `${file} takes Playwright from playwright.mjs`);
    }
});
