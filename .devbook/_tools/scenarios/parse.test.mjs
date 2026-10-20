// The scenarios file parser: journeys with their setup fields, parts, steps
// with their continuation lines, `shot:` labels and where they sit, the spec
// header and the calls a spec makes, and the files found in a repository.
//
// Run: `node --test parse.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import {
    dataFolder,
    isScenariosPath,
    labelsOf,
    listJourneys,
    listLegacyPages,
    listScenarioFiles,
    listSpecs,
    OUTSIDE_ROOT,
    parseScenariosFile,
    parseSpecHeader,
    readProfiles,
    resolveInside,
    runFolderOf,
    specLabels,
    specParts,
    splitAddress,
} from "./parse.mjs";

const F = "```";
const FILE = ".devbook/domain/work/scenarios.md";
const SCENARIOS = `# Scenarios

${F}meta
type: scenarios
${F}

The journeys of the work context.

## Set up and fill the backlog

${F}meta
type: scenario
start: admin:/products/webshop/backlog
actor: [admin:actors.md#product-owner, customer:actors.md#shopper]
data: [webshop-without-statuses]
profile: tenant-acme
flags: [context.md#new-board, -context.md#legacy-filters]
settings: [context.md#backlog-max-columns=5]
${F}

A product owner sets up the backlog. \`![not an image](shot:quoted)\`

### Statuses are set up
- **Given** a product "Webshop"
  with no backlog statuses
- **When** I create the statuses
  - "New", "Doing" and "Done"
- **Then** they appear as board columns

![The board with three empty columns](shot:statuses-set-up)

${F}text
### Not a part
- **Given** inside a fence
${F}

### An item is moved
1. **Given** an item in "New"
2. **When** I drag it to "Doing"
![Mid-drag](shot:mid-drag)

## Close the sprint

${F}meta
type: scenario
status: draft
${F}

### The sprint is closed
- **When** I close the sprint
- **Then** the board is empty
`;

test("each ## is a journey with its address, title, and setup fields, lists as lists", () => {
    const file = parseScenariosFile(SCENARIOS, FILE);
    assert.equal(file.isScenarios, true);
    assert.equal(file.legacyPage, false);
    assert.deepEqual(file.journeys.map((j) => [j.slug, j.id, j.title, j.isScenario]), [
        ["set-up-and-fill-the-backlog", `${FILE}#set-up-and-fill-the-backlog`, "Set up and fill the backlog", true],
        ["close-the-sprint", `${FILE}#close-the-sprint`, "Close the sprint", true],
    ]);
    assert.deepEqual(file.journeys[0].setup, {
        start: "admin:/products/webshop/backlog",
        actor: ["admin:actors.md#product-owner", "customer:actors.md#shopper"],
        data: ["webshop-without-statuses"],
        profile: "tenant-acme",
        flags: ["context.md#new-board", "-context.md#legacy-filters"],
        settings: ["context.md#backlog-max-columns=5"],
    });
    assert.deepEqual(file.journeys[1].setup, { start: null, actor: [], data: [], profile: null, flags: [], settings: [] });
    assert.equal(file.journeys[1].meta.status, "draft");
});

test("reads every ### as a part of its journey, its steps with continuation lines folded in, and skips fences", () => {
    const [journey, closing] = parseScenariosFile(SCENARIOS, FILE).journeys;
    assert.deepEqual(journey.parts.map((part) => [part.title, part.anchor]), [
        ["Statuses are set up", "statuses-are-set-up"],
        ["An item is moved", "an-item-is-moved"],
    ]);
    assert.deepEqual(journey.parts[0].steps.map((step) => [step.keyword, step.text]), [
        ["Given", 'a product "Webshop" with no backlog statuses'],
        ["When", 'I create the statuses "New", "Doing" and "Done"'],
        ["Then", "they appear as board columns"],
    ]);
    assert.deepEqual(journey.parts[1].steps.map((step) => step.keyword), ["Given", "When"]);
    assert.equal(journey.parts[1].steps[1].text, 'I drag it to "Doing"');
    assert.deepEqual(closing.parts.map((part) => part.anchor), ["the-sprint-is-closed"]);
});

test("reads shot: labels with their journey and part, and ignores one in a code span", () => {
    const file = parseScenariosFile(SCENARIOS, FILE);
    assert.deepEqual(labelsOf(file.journeys[0]), ["statuses-set-up", "mid-drag"]);
    assert.deepEqual(labelsOf(file.journeys[1]), []);
    assert.deepEqual(file.shots.map((shot) => [shot.label, shot.caption, shot.journey, shot.part, shot.inStepList]), [
        ["statuses-set-up", "The board with three empty columns", "set-up-and-fill-the-backlog", "statuses-are-set-up", false],
        ["mid-drag", "Mid-drag", "set-up-and-fill-the-backlog", "an-item-is-moved", true],
    ]);
});

test("reports an image inside a step list, a bad label, a label used twice in the file, a shot outside a journey, and a repeated anchor", () => {
    const extra = "\n![Again](shot:mid-drag)\n\n![Bad](shot:Bad_Label)\n\n### Statuses are set up\n- **Given** again\n";
    const file = parseScenariosFile(SCENARIOS.replace("The journeys of the work context.", "![Early](shot:early)") + extra, FILE);
    assert.deepEqual(file.issues.map((issue) => [issue.kind, issue.label ?? null]), [
        ["duplicate-anchor", null],
        ["shot-outside-journey", "early"],
        ["image-in-steps", "mid-drag"],
        ["bad-label", "mid-drag"],
        ["bad-label", "Bad_Label"],
    ]);
});

test("a heading keeps a trailing #, a deeply nested item loses its marker, and a shot in a step leaves no markup", () => {
    const file = parseScenariosFile(
        `# Scenarios\n\n${F}meta\ntype: scenarios\n${F}\n\n## Pay\n\n${F}meta\ntype: scenario\n${F}\n\n### Pay in C#\n10. **Given** a basket\n    - with  two lines\n- **Then** it is paid ![A  caption](shot:paid)\n`,
        ".devbook/domain/shop/scenarios.md"
    );
    const [journey] = file.journeys;
    assert.equal(journey.parts[0].title, "Pay in C#");
    assert.deepEqual(journey.parts[0].steps.map((step) => step.text), ["a basket with two lines", "it is paid"]);
});

test("a ## without type: scenario is no journey, and a whole file typed scenario is the old page", () => {
    const untyped = parseScenariosFile(`# Scenarios\n\n${F}meta\ntype: scenarios\n${F}\n\n## Notes\n\nProse.\n`, FILE);
    assert.equal(untyped.journeys[0].isScenario, false);
    const legacy = parseScenariosFile(`# Old\n\n${F}meta\ntype: scenario\n${F}\n\n## Part\n- **Given** a\n`, ".devbook/domain/work/old.md");
    assert.equal(legacy.legacyPage, true);
    assert.equal(legacy.isScenarios, false);
});

test("a scenarios file is scenarios.md or a split of it, directly in a bounded context", () => {
    assert.equal(isScenariosPath(".devbook/domain/work/scenarios.md"), true);
    assert.equal(isScenariosPath(".devbook/domain/work/scenarios.returns.md"), true);
    assert.equal(isScenariosPath(".devbook/domain/work/deeper/scenarios.md"), false);
    assert.equal(isScenariosPath(".devbook/domain/scenarios.md"), false);
    assert.equal(isScenariosPath(".devbook/domain/work/requirements.md"), false);
    assert.equal(isScenariosPath(".devbook/arc42/scenarios.md"), false);
});

test("an address splits into its file and anchor, and a journey runs beside its file", () => {
    assert.deepEqual(splitAddress(`${FILE}#close-the-sprint`), { path: FILE, anchor: "close-the-sprint" });
    assert.deepEqual(splitAddress(FILE), { path: FILE, anchor: null });
    assert.equal(runFolderOf({ path: FILE, slug: "close-the-sprint" }), ".devbook/domain/work/scenarios/close-the-sprint");
    assert.equal(runFolderOf({ path: ".devbook/domain/work/scenarios.returns.md", slug: "return" }), ".devbook/domain/work/scenarios.returns/return");
});

test("reads a spec's header, its shot() labels, and its part titles", () => {
    const spec = [
        `// scenario: ${FILE}#set-up-and-fill-the-backlog`,
        "// signature: 9C41E2A0",
        "import { scenario, shot } from '../../../.devbook/_tools/scenarios/setup';",
        "scenario('set-up-and-fill-the-backlog', ({ part }) => {",
        "  part('Statuses are set up', async ({ page, step }) => {",
        "    await shot(page, 'statuses-set-up');",
        "    // await shot(page, 'commented-out');",
        "  });",
        "  part(\"It\\'s moved\", async ({ page }) => { await shot(page, \"mid-drag\"); });",
        "});",
    ].join("\n");
    assert.deepEqual(parseSpecHeader(spec), { page: `${FILE}#set-up-and-fill-the-backlog`, signature: "9c41e2a0" });
    assert.deepEqual(specLabels(spec), ["statuses-set-up", "mid-drag"]);
    assert.deepEqual(specParts(spec), ["Statuses are set up", "It's moved"]);
    assert.deepEqual(parseSpecHeader("import x from 'y';\n"), { page: null, signature: null });
});

test("finds the scenarios files under .devbook/domain, their journeys, the old pages, and the specs that name one", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-parse-"));
    try {
        await mkdir(path.join(root, ".devbook/domain/work/deeper"), { recursive: true });
        await mkdir(path.join(root, ".devbook/domain/_meta"), { recursive: true });
        await mkdir(path.join(root, ".test/tests"), { recursive: true });
        await mkdir(path.join(root, "node_modules/x"), { recursive: true });
        await writeFile(path.join(root, FILE), SCENARIOS);
        await writeFile(path.join(root, ".devbook/domain/work/deeper/scenarios.md"), SCENARIOS);
        await writeFile(path.join(root, ".devbook/domain/work/deeper/old.md"), `# Old\n\n${F}meta\ntype: scenario\n${F}\n`);
        await writeFile(path.join(root, ".devbook/domain/work/features.md"), `# Features\n\n${F}meta\ntype: features\n${F}\n`);
        await writeFile(path.join(root, ".devbook/domain/_meta/scenarios.md"), SCENARIOS);
        const address = `${FILE}#close-the-sprint`;
        await writeFile(path.join(root, ".test/tests/journey.spec.ts"), `// scenario: ${address}\n`);
        await writeFile(path.join(root, ".test/tests/other.spec.ts"), "test('x', () => {});\n");
        await writeFile(path.join(root, "node_modules/x/journey.spec.ts"), `// scenario: ${address}\n`);
        await mkdir(path.join(root, ".claude/worktrees/copy"), { recursive: true });
        await writeFile(path.join(root, ".claude/worktrees/copy/journey.spec.ts"), `// scenario: ./${address}\n`);

        assert.deepEqual((await listScenarioFiles(root)).map((file) => file.path), [FILE]);
        assert.deepEqual((await listJourneys(root)).map((journey) => journey.id), [`${FILE}#set-up-and-fill-the-backlog`, address]);
        assert.deepEqual(await listLegacyPages(root), [".devbook/domain/work/deeper/old.md"]);
        // A dot-folder is skipped below the root, and walked when it is a root itself.
        assert.deepEqual((await listSpecs(root)).length, 0);
        const specs = await listSpecs(root, [".test", ".claude/worktrees/copy"]);
        assert.deepEqual(specs.map((spec) => [spec.path, spec.page]), [
            [".claude/worktrees/copy/journey.spec.ts", address],
            [".test/tests/journey.spec.ts", address],
        ]);
        assert.deepEqual((await listSpecs(root, ["e2e"])).length, 0);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("reads profiles.json, and says when it is missing or broken", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-profiles-"));
    try {
        assert.deepEqual(await readProfiles(root), { missing: true });
        await mkdir(path.join(root, ".devbook/scenarios"), { recursive: true });
        await writeFile(path.join(root, ".devbook/scenarios/profiles.json"), "{ nope");
        assert.match((await readProfiles(root)).error, /does not parse/);
        await writeFile(path.join(root, ".devbook/scenarios/profiles.json"), '{ "default": { "tenant": "demo" } }');
        assert.deepEqual(await readProfiles(root), { map: { default: { tenant: "demo" } } });
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("resolveInside keeps a path inside the root and refuses one that leaves it", () => {
    const root = path.resolve(tmpdir(), "repo");
    assert.equal(resolveInside(root, ".devbook/domain/x.md"), path.join(root, ".devbook/domain/x.md"));
    assert.equal(resolveInside(root, ""), root);
    assert.equal(resolveInside(root, "a/../b.md"), path.join(root, "b.md"));
    assert.equal(resolveInside(root, "..not-a-climb.md"), path.join(root, "..not-a-climb.md"));
    assert.equal(resolveInside(root, path.join(root, "e2e/x.spec.ts")), path.join(root, "e2e/x.spec.ts"));
    for (const given of ["..", "../secret", "a/../../secret", path.resolve(root, "../repo-sibling/x.md"), path.parse(root).root]) {
        assert.throws(() => resolveInside(root, given), (error) => error.code === OUTSIDE_ROOT && /resolves outside the repository/.test(error.message), given);
    }
});

test("a data set or a profile file outside the repository is never read", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-outside-"));
    try {
        assert.equal(dataFolder(root, ".devbook/scenarios", "webshop"), path.join(root, ".devbook/scenarios/data/webshop"));
        assert.equal(dataFolder(root, ".devbook/scenarios", "../profiles"), null);
        assert.equal(dataFolder(root, "../elsewhere", "webshop"), null);
        assert.deepEqual(await readProfiles(root, "../elsewhere"), { error: "resolves outside the repository" });
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
