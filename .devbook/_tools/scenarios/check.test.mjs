// The coverage check: one clean repository, then each check on its own — a
// journey without a spec, a spec without a journey, a signature mismatch, label
// drift both ways, an image inside a step list, a bad label, a repeated anchor,
// a shot outside a journey, a dangling scenario: reference, an unknown profile,
// and a page of the old shape — plus the CLI's exit codes.
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
import { parseScenariosFile } from "./parse.mjs";
import { signatureOf } from "./signature.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const F = "```";
const FILE = ".devbook/domain/work/scenarios.md";
const ADDRESS = `${FILE}#move-an-item`;
const SCENARIOS = `# Scenarios

${F}meta
type: scenarios
${F}

## Move an item

${F}meta
type: scenario
start: /board
${F}

### The item is moved
- **When** I drag it to "Doing"
- **Then** it shows in "Doing"

![After the drag](shot:after-drag)
`;
const FEATURES = `# Features

${F}meta
type: features
${F}

![After](scenario:work/scenarios.md#after-drag)
`;
const signOf = (markdown) => signatureOf(parseScenariosFile(markdown, FILE).journeys[0]);
const signature = signOf(SCENARIOS);
const spec = (sig = signature, labels = ["after-drag"], address = ADDRESS) =>
    [`// scenario: ${address}`, `// signature: ${sig}`, "scenario('move-an-item', ({ part }) => {", "  part('The item is moved', async ({ page }) => {", ...labels.map((label) => `    await shot(page, '${label}');`), "  });", "});", ""].join("\n");

async function repo(files) {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-check-"));
    const all = {
        [FILE]: SCENARIOS,
        ".devbook/domain/work/features.md": FEATURES,
        ".devbook/scenarios/profiles.json": JSON.stringify({ default: { portals: { app: "env:APP_URL" } } }),
        "e2e/tests/work/scenarios/move-an-item.spec.ts": spec(),
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

const SPEC = "e2e/tests/work/scenarios/move-an-item.spec.ts";

test("a repository whose specs match its journeys has no findings", async () => {
    assert.deepEqual(await findings({}), []);
});

test("journey-without-spec", async () => {
    assert.deepEqual(await findings({ [SPEC]: null }), [["journey-without-spec", `${FILE}:7`]]);
});

test("spec-without-journey, for an address that names no journey and for a bare file", async () => {
    assert.deepEqual(await findings({ "e2e/tests/gone.spec.ts": spec(signature, [], `${FILE}#gone`) }), [["spec-without-journey", "e2e/tests/gone.spec.ts"]]);
    assert.deepEqual(await findings({ "e2e/tests/bare.spec.ts": spec(signature, [], FILE) }), [["spec-without-journey", "e2e/tests/bare.spec.ts"]]);
});

test("signature-mismatch, for a changed journey and for a missing signature line", async () => {
    assert.deepEqual(await findings({ [SPEC]: spec("00000000") }), [["signature-mismatch", SPEC]]);
    assert.deepEqual(await findings({ [FILE]: SCENARIOS.replace("I drag it", "I move it") }), [["signature-mismatch", SPEC]]);
    assert.deepEqual(await findings({ [SPEC]: spec().replace(/^\/\/ signature:.*\n/m, "") }), [["signature-mismatch", SPEC]]);
});

test("label-drift, a label the spec never captures and one the journey does not have", async () => {
    assert.deepEqual(await findings({ [SPEC]: spec(signature, ["before-drag"]) }), [
        ["label-drift", SPEC],
        ["label-drift", SPEC],
    ]);
});

test("image-in-steps", async () => {
    const file = SCENARIOS.replace('"Doing"\n\n![After', '"Doing"\n![After');
    assert.deepEqual(await findings({ [FILE]: file, [SPEC]: spec(signOf(file)) }), [["image-in-steps", `${FILE}:17`]]);
});

test("bad-label", async () => {
    const file = SCENARIOS.replace("shot:after-drag", "shot:After_Drag");
    const result = await findings({ [FILE]: file, [SPEC]: spec(signOf(file), ["After_Drag"]), ".devbook/domain/work/features.md": null });
    assert.deepEqual(result, [["bad-label", `${FILE}:18`]]);
});

test("duplicate-anchor and shot-outside-journey", async () => {
    const file = SCENARIOS.replace("## Move an item", "![Early](shot:early)\n\n## Move an item") + "\n### Move an item\n- **When** again\n";
    const result = await findings({ [FILE]: file, [SPEC]: spec(signOf(file)) });
    assert.deepEqual(result, [
        ["duplicate-anchor", `${FILE}:22`],
        ["shot-outside-journey", `${FILE}:7`],
    ]);
});

test("dangling-reference, to a file and to a label", async () => {
    const result = await findings({
        ".devbook/domain/work/features.md": FEATURES + "\n![x](scenario:work/nowhere.md#a)\n\n![y](scenario:.devbook/domain/work/scenarios.md#no-such-label)\n\n`![z](scenario:quoted#x)`\n",
    });
    assert.deepEqual(result, [
        ["dangling-reference", ".devbook/domain/work/features.md:9"],
        ["dangling-reference", ".devbook/domain/work/features.md:11"],
    ]);
});

test("unknown-profile, named on the journey or with no profile file", async () => {
    const file = SCENARIOS.replace("start: /board", "start: /board\nprofile: tenant-acme");
    assert.deepEqual(await findings({ [FILE]: file, [SPEC]: spec(signOf(file)) }), [["unknown-profile", ADDRESS]]);
    assert.deepEqual(await findings({ ".devbook/scenarios/profiles.json": null }), [["unknown-profile", ADDRESS]]);
});

test("legacy-page, a whole file typed scenario", async () => {
    const old = `# Move an item\n\n${F}meta\ntype: scenario\n${F}\n\n## The item is moved\n- **When** I drag it\n`;
    assert.deepEqual(await findings({ ".devbook/domain/work/journeys/old.md": old }), [["legacy-page", ".devbook/domain/work/journeys/old.md"]]);
});

test("--specs limits where specs are looked for", async () => {
    assert.deepEqual(await findings({}, { specRoots: ["elsewhere"] }), [["journey-without-spec", `${FILE}:7`]]);
});

test("the CLI exits 0 when clean and 1 with findings", async () => {
    const root = await repo({});
    try {
        const clean = spawnSync(process.execPath, [path.join(here, "check.mjs"), "--root", root], { encoding: "utf8" });
        assert.equal(clean.status, 0, clean.stderr);
        await writeFile(path.join(root, SPEC), spec("00000000"));
        const drift = spawnSync(process.execPath, [path.join(here, "check.mjs"), "--root", root, "--json"], { encoding: "utf8" });
        assert.equal(drift.status, 1);
        assert.equal(JSON.parse(drift.stdout).findings[0].check, "signature-mismatch");
        assert.equal(spawnSync(process.execPath, [path.join(here, "check.mjs"), "--bogus"], { encoding: "utf8" }).status, 2);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("a spec folder or a scenario folder outside the repository is never read", async () => {
    const root = await repo({});
    try {
        const result = await coverage({ repoRoot: root, specRoots: [".."], scenarioFolder: "../elsewhere" });
        assert.deepEqual(result.map((finding) => finding.check), ["journey-without-spec", "unknown-profile"]);
        assert.match(result[1].what, /\.\.\/elsewhere\/profiles\.json resolves outside the repository/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
