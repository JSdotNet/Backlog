// The coverage check: one clean repository, then each check on its own — a
// page without a spec, a spec without a page, a signature mismatch, label
// drift both ways, an image inside a step list, a bad label, a duplicate stem,
// a dangling scenario: reference, and an unknown profile — plus the CLI's exit
// codes.
//
// Run: `node --test check.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { coverage } from "./check.mjs";
import { parseScenarioPage } from "./parse.mjs";
import { signatureOf } from "./signature.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const F = "```";
const PAGE_PATH = ".devbook/domain/work/move-an-item.md";
const PAGE = `# Move an item

${F}meta
type: scenario
start: /board
${F}

## The item is moved
- **When** I drag it to "Doing"
- **Then** it shows in "Doing"

![After the drag](shot:after-drag)
`;
const FEATURES = `# Features

${F}meta
type: features
${F}

![After](scenario:move-an-item#after-drag)
`;
const signature = signatureOf(parseScenarioPage(PAGE, PAGE_PATH));
const spec = (sig = signature, labels = ["after-drag"], page = PAGE_PATH) =>
    [`// scenario: ${page}`, `// signature: ${sig}`, "scenario('move-an-item', ({ part }) => {", "  part('The item is moved', async ({ page }) => {", ...labels.map((label) => `    await shot(page, '${label}');`), "  });", "});", ""].join("\n");

async function repo(files) {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-check-"));
    const all = {
        [PAGE_PATH]: PAGE,
        ".devbook/domain/work/features.md": FEATURES,
        ".devbook/scenarios/profiles.json": JSON.stringify({ default: { portals: { app: "env:APP_URL" } } }),
        "e2e/tests/move-an-item.spec.ts": spec(),
        ...files,
    };
    for (const [rel, content] of Object.entries(all)) {
        if (content === null) continue;
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), content);
    }
    return root;
}

async function findings(files, options = {}) {
    const root = await repo(files);
    try {
        return (await coverage({ repoRoot: root, ...options })).map((finding) => [finding.check, finding.where]);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

test("a repository whose specs match its pages has no findings", async () => {
    assert.deepEqual(await findings({}), []);
});

test("page-without-spec", async () => {
    assert.deepEqual(await findings({ "e2e/tests/move-an-item.spec.ts": null }), [["page-without-spec", PAGE_PATH]]);
});

test("spec-without-page", async () => {
    const gone = ".devbook/domain/work/gone.md";
    assert.deepEqual(await findings({ "e2e/tests/gone.spec.ts": spec(signature, [], gone) }), [["spec-without-page", "e2e/tests/gone.spec.ts"]]);
});

test("signature-mismatch, for a changed page and for a missing signature line", async () => {
    assert.deepEqual(await findings({ "e2e/tests/move-an-item.spec.ts": spec("00000000") }), [["signature-mismatch", "e2e/tests/move-an-item.spec.ts"]]);
    assert.deepEqual(await findings({ [PAGE_PATH]: PAGE.replace("I drag it", "I move it") }), [["signature-mismatch", "e2e/tests/move-an-item.spec.ts"]]);
    assert.deepEqual(await findings({ "e2e/tests/move-an-item.spec.ts": spec().replace(/^\/\/ signature:.*\n/m, "") }), [["signature-mismatch", "e2e/tests/move-an-item.spec.ts"]]);
});

test("label-drift, a label the spec never captures and one the page does not have", async () => {
    assert.deepEqual(await findings({ "e2e/tests/move-an-item.spec.ts": spec(signature, ["before-drag"]) }), [
        ["label-drift", "e2e/tests/move-an-item.spec.ts"],
        ["label-drift", "e2e/tests/move-an-item.spec.ts"],
    ]);
});

test("image-in-steps", async () => {
    const page = PAGE.replace('"Doing"\n\n![After', '"Doing"\n![After');
    const sig = signatureOf(parseScenarioPage(page, PAGE_PATH));
    assert.deepEqual(await findings({ [PAGE_PATH]: page, "e2e/tests/move-an-item.spec.ts": spec(sig) }), [["image-in-steps", `${PAGE_PATH}:11`]]);
});

test("bad-label", async () => {
    const page = PAGE.replace("shot:after-drag", "shot:After_Drag");
    const sig = signatureOf(parseScenarioPage(page, PAGE_PATH));
    const result = await findings({ [PAGE_PATH]: page, "e2e/tests/move-an-item.spec.ts": spec(sig, ["After_Drag"]), ".devbook/domain/work/features.md": null });
    assert.deepEqual(result, [["bad-label", `${PAGE_PATH}:12`]]);
});

test("duplicate-stem", async () => {
    const result = await findings({ ".devbook/domain/shop/move-an-item.md": PAGE });
    assert.deepEqual(result, [
        ["duplicate-stem", PAGE_PATH],
        ["page-without-spec", ".devbook/domain/shop/move-an-item.md"],
    ]);
});

test("dangling-reference, to a stem and to a label", async () => {
    const result = await findings({
        ".devbook/domain/work/features.md": FEATURES + "\n![x](scenario:no-such-page#a)\n\n![y](scenario:move-an-item#no-such-label)\n\n`![z](scenario:quoted#x)`\n",
    });
    assert.deepEqual(result, [
        ["dangling-reference", ".devbook/domain/work/features.md:9"],
        ["dangling-reference", ".devbook/domain/work/features.md:11"],
    ]);
});

test("unknown-profile, named on the page or with no profile file", async () => {
    const page = PAGE.replace("start: /board", "start: /board\nprofile: tenant-acme");
    const sig = signatureOf(parseScenarioPage(page, PAGE_PATH));
    assert.deepEqual(await findings({ [PAGE_PATH]: page, "e2e/tests/move-an-item.spec.ts": spec(sig) }), [["unknown-profile", PAGE_PATH]]);
    assert.deepEqual(await findings({ ".devbook/scenarios/profiles.json": null }), [["unknown-profile", PAGE_PATH]]);
});

test("--specs limits where specs are looked for", async () => {
    assert.deepEqual(await findings({}, { specRoots: ["elsewhere"] }), [["page-without-spec", PAGE_PATH]]);
});

test("the CLI exits 0 when clean and 1 with findings", async () => {
    const root = await repo({});
    try {
        const clean = spawnSync(process.execPath, [path.join(here, "check.mjs"), "--root", root], { encoding: "utf8" });
        assert.equal(clean.status, 0, clean.stderr);
        await writeFile(path.join(root, "e2e/tests/move-an-item.spec.ts"), spec("00000000"));
        const drift = spawnSync(process.execPath, [path.join(here, "check.mjs"), "--root", root, "--json"], { encoding: "utf8" });
        assert.equal(drift.status, 1);
        assert.equal(JSON.parse(drift.stdout).findings[0].check, "signature-mismatch");
        assert.equal(spawnSync(process.execPath, [path.join(here, "check.mjs"), "--bogus"], { encoding: "utf8" }).status, 2);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
