// The scenario page parser: setup fields, parts, steps with their continuation
// lines, `shot:` labels and where they sit, the spec header and the calls a
// spec makes, and the pages found in a repository.
//
// Run: `node --test parse.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { labelsOf, listScenarioPages, listSpecs, parseScenarioPage, parseSpecHeader, readProfiles, specLabels, specParts } from "./parse.mjs";

const F = "```";
const PAGE = `# Set up and fill the backlog

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

## Statuses are set up
- **Given** a product "Webshop"
  with no backlog statuses
- **When** I create the statuses
  - "New", "Doing" and "Done"
- **Then** they appear as board columns

![The board with three empty columns](shot:statuses-set-up)

${F}text
## Not a part
- **Given** inside a fence
${F}

## An item is moved
1. **Given** an item in "New"
2. **When** I drag it to "Doing"
![Mid-drag](shot:mid-drag)
`;

test("reads the setup fields, lists as lists", () => {
    const page = parseScenarioPage(PAGE, ".devbook/domain/work/set-up-and-fill-the-backlog.md");
    assert.equal(page.isScenario, true);
    assert.equal(page.stem, "set-up-and-fill-the-backlog");
    assert.equal(page.title, "Set up and fill the backlog");
    assert.deepEqual(page.setup, {
        start: "admin:/products/webshop/backlog",
        actor: ["admin:actors.md#product-owner", "customer:actors.md#shopper"],
        data: ["webshop-without-statuses"],
        profile: "tenant-acme",
        flags: ["context.md#new-board", "-context.md#legacy-filters"],
        settings: ["context.md#backlog-max-columns=5"],
    });
});

test("reads every ## as a part, its steps with continuation lines folded in, and skips fences", () => {
    const page = parseScenarioPage(PAGE, "x.md");
    assert.deepEqual(page.parts.map((part) => [part.title, part.anchor]), [
        ["Statuses are set up", "statuses-are-set-up"],
        ["An item is moved", "an-item-is-moved"],
    ]);
    assert.deepEqual(page.parts[0].steps.map((step) => [step.keyword, step.text]), [
        ["Given", 'a product "Webshop" with no backlog statuses'],
        ["When", 'I create the statuses "New", "Doing" and "Done"'],
        ["Then", "they appear as board columns"],
    ]);
    assert.deepEqual(page.parts[1].steps.map((step) => step.keyword), ["Given", "When"]);
    assert.equal(page.parts[1].steps[1].text, 'I drag it to "Doing"');
});

test("reads shot: labels with their part, and ignores one in a code span", () => {
    const page = parseScenarioPage(PAGE, "x.md");
    assert.deepEqual(labelsOf(page), ["statuses-set-up", "mid-drag"]);
    assert.deepEqual(page.shots.map((shot) => [shot.label, shot.caption, shot.part, shot.inStepList]), [
        ["statuses-set-up", "The board with three empty columns", "statuses-are-set-up", false],
        ["mid-drag", "Mid-drag", "an-item-is-moved", true],
    ]);
});

test("reports an image inside a step list, a bad label, and a label used twice", () => {
    const page = parseScenarioPage(PAGE + "\n![Again](shot:mid-drag)\n\n![Bad](shot:Bad_Label)\n", "x.md");
    assert.deepEqual(page.issues.map((issue) => [issue.kind, issue.label]), [
        ["image-in-steps", "mid-drag"],
        ["bad-label", "mid-drag"],
        ["bad-label", "Bad_Label"],
    ]);
});

test("a heading keeps a trailing #, a deeply nested item loses its marker, and a shot in a step leaves no markup", () => {
    const page = parseScenarioPage(
        `# P\n\n${F}meta\ntype: scenario\n${F}\n\n## Pay in C#\n10. **Given** a basket\n    - with  two lines\n- **Then** it is paid ![A  caption](shot:paid)\n`,
        "p.md"
    );
    assert.equal(page.parts[0].title, "Pay in C#");
    assert.deepEqual(page.parts[0].steps.map((step) => step.text), ["a basket with two lines", "it is paid"]);
});

test("a page without type: scenario is not one", () => {
    assert.equal(parseScenarioPage(`# Features\n\n${F}meta\ntype: features\n${F}\n`, "f.md").isScenario, false);
});

test("reads a spec's header, its shot() labels, and its part titles", () => {
    const spec = [
        "// scenario: .devbook/domain/work/set-up-and-fill-the-backlog.md",
        "// signature: 9C41E2A0",
        "import { scenario, shot } from '../../.devbook/_tools/scenarios/setup';",
        "scenario('set-up-and-fill-the-backlog', ({ part }) => {",
        "  part('Statuses are set up', async ({ page, step }) => {",
        "    await shot(page, 'statuses-set-up');",
        "    // await shot(page, 'commented-out');",
        "  });",
        "  part(\"It\\'s moved\", async ({ page }) => { await shot(page, \"mid-drag\"); });",
        "});",
    ].join("\n");
    assert.deepEqual(parseSpecHeader(spec), { page: ".devbook/domain/work/set-up-and-fill-the-backlog.md", signature: "9c41e2a0" });
    assert.deepEqual(specLabels(spec), ["statuses-set-up", "mid-drag"]);
    assert.deepEqual(specParts(spec), ["Statuses are set up", "It's moved"]);
    assert.deepEqual(parseSpecHeader("import x from 'y';\n"), { page: null, signature: null });
});

test("finds the scenario pages under .devbook/domain and the specs that name one", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-parse-"));
    try {
        await mkdir(path.join(root, ".devbook/domain/work/deeper"), { recursive: true });
        await mkdir(path.join(root, ".devbook/domain/_meta"), { recursive: true });
        await mkdir(path.join(root, ".test/tests"), { recursive: true });
        await mkdir(path.join(root, "node_modules/x"), { recursive: true });
        await writeFile(path.join(root, ".devbook/domain/work/deeper/journey.md"), PAGE);
        await writeFile(path.join(root, ".devbook/domain/work/features.md"), `# Features\n\n${F}meta\ntype: features\n${F}\n`);
        await writeFile(path.join(root, ".devbook/domain/_meta/skip.md"), PAGE);
        await writeFile(path.join(root, ".test/tests/journey.spec.ts"), "// scenario: .devbook/domain/work/deeper/journey.md\n");
        await writeFile(path.join(root, ".test/tests/other.spec.ts"), "test('x', () => {});\n");
        await writeFile(path.join(root, "node_modules/x/journey.spec.ts"), "// scenario: .devbook/domain/work/deeper/journey.md\n");
        await mkdir(path.join(root, ".claude/worktrees/copy"), { recursive: true });
        await writeFile(path.join(root, ".claude/worktrees/copy/journey.spec.ts"), "// scenario: ./.devbook/domain/work/deeper/journey.md\n");

        const pages = await listScenarioPages(root);
        assert.deepEqual(pages.map((page) => page.path), [".devbook/domain/work/deeper/journey.md"]);
        // A dot-folder is skipped below the root, and walked when it is a root itself.
        assert.deepEqual((await listSpecs(root)).length, 0);
        const specs = await listSpecs(root, [".test", ".claude/worktrees/copy"]);
        assert.deepEqual(specs.map((spec) => [spec.path, spec.page]), [
            [".claude/worktrees/copy/journey.spec.ts", ".devbook/domain/work/deeper/journey.md"],
            [".test/tests/journey.spec.ts", ".devbook/domain/work/deeper/journey.md"],
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
