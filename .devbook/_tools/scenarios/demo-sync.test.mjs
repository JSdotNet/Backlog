// The demo sync: a current demo is left byte for byte, a stale signature and
// drifted step text are re-copied from the journey and nothing else moves, a copy
// keeps its keyword style, a part that does not resolve is reported, `--check`
// writes nothing and exits 1 while anything is stale, and every `*.demo.html`
// under `domain/` is taken when no file is named.
//
// Run: `node --test demo-sync.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, mkdir, readFile, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseWithSpans, syncDemos } from "./demo-sync.mjs";
import { parseScenariosFile } from "./parse.mjs";
import { signatureOf } from "./signature.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const F = "```";
const PAGE_PATH = ".devbook/domain/work/scenarios.md";
const DEMO_PATH = ".devbook/domain/work/features.demo.html";
const page = (doing = "Doing") => `# Scenarios

${F}meta
type: scenarios
${F}

## Set up the board

${F}meta
type: scenario
${F}

### Statuses are set up
- **Given** a product with no statuses
- **When** I create "New", "${doing}" and "Done"
- **Then** they appear as columns in that order
`;
const journeyOf = (markdown) => parseScenariosFile(markdown, PAGE_PATH).journeys[0];
const SIGNATURE = signatureOf(journeyOf(page()));

// Hand-formatted on purpose: the sync must keep every byte it does not own.
const demo = ({ signature = SIGNATURE, part = "scenarios.md#statuses-are-set-up", texts } = {}) => {
    const [one, two, three] = texts ?? ["a product with no statuses", 'When I create "New", "Doing" and "Done"', "they appear as columns in that order"];
    return `<!doctype html>
<html><head><title>Work</title>
<!-- template:begin -->
<style>body{margin:0}</style>
<!-- template:end -->
<script type="application/json" id="demo-meta">{"question":"Does the board read?"}</script>
<script type="application/json" id="demo-model">
{
  "screens": [ {"id": "statuses", "title": "Statuses"}, {"id": "board", "title": "Board"} ],
  "walkthroughs": [
    { "id": "set-up-statuses", "title": "Set up statuses",
      "part": ${JSON.stringify(part)},   "signature": ${JSON.stringify(signature)},
      "steps": [
        {"screen": "statuses", "step": 1, "text": ${JSON.stringify(one)}},
        {"screen": "statuses", "step": 2,   "text": ${JSON.stringify(two)}},
        {"screen": "board", "step": 3, "text": ${JSON.stringify(three)}}
      ] },
    { "id": "free-one", "title": "Free", "steps": [ {"screen": "board", "text": "anything at all"} ] }
  ]
}
</script>
</head><body>
<section id="statuses" data-screen></section>
<section id="board" data-screen></section>
</body></html>
`;
};

async function withRepo(files, run) {
    const root = await mkdtemp(path.join(tmpdir(), "demo-sync-"));
    try {
        for (const [rel, content] of Object.entries(files)) {
            await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
            await writeFile(path.join(root, rel), content, "utf8");
        }
        await run(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
const read = (root, rel) => readFile(path.join(root, rel), "utf8");
const cli = (root, ...args) => spawnSync(process.execPath, [path.join(here, "demo-sync.mjs"), "--root", root, ...args], { encoding: "utf8" });

test("parseWithSpans keeps the offset of every value", () => {
    const text = ' { "a": [1, {"b": "x\\"y"}], "c": null } ';
    const root = parseWithSpans(text);
    const b = root.members.get("a").items[1].members.get("b");
    assert.equal(b.value, 'x"y');
    assert.equal(text.slice(b.start, b.end), '"x\\"y"');
    assert.throws(() => parseWithSpans("{ \"a\": }"));
});

test("a current demo is left byte for byte", async () => {
    await withRepo({ [PAGE_PATH]: page(), [DEMO_PATH]: demo() }, async (root) => {
        const [result] = await syncDemos(root);
        assert.deepEqual(result, { path: DEMO_PATH, changes: [], problems: [] });
        assert.equal(await read(root, DEMO_PATH), demo());
        assert.equal(cli(root, "--check").status, 0);
    });
});

test("a moved journey re-copies the signature and the drifted step, and nothing else", async () => {
    const moved = page("In progress");
    const signature = signatureOf(journeyOf(moved));
    await withRepo({ [PAGE_PATH]: moved, [DEMO_PATH]: demo() }, async (root) => {
        const check = cli(root, "--check");
        assert.equal(check.status, 1, check.stdout + check.stderr);
        assert.match(check.stdout, /signature stale/);
        assert.match(check.stdout, /step 2 text drifted/);
        assert.equal(await read(root, DEMO_PATH), demo(), "--check writes nothing");

        const run = cli(root);
        assert.equal(run.status, 0, run.stdout + run.stderr);
        const expected = demo({ signature, texts: ["a product with no statuses", 'When I create "New", "In progress" and "Done"', "they appear as columns in that order"] });
        assert.equal(await read(root, DEMO_PATH), expected);
        assert.equal(cli(root, "--check").status, 0, "a synced demo checks clean");
        assert.equal(cli(root).stdout.includes("is current"), true, "a second run changes nothing");
    });
});

test("a copy keeps its keyword style, and an edited text at the current signature is restored", async () => {
    const texts = ["**Given** a product with one status", "I create three", "they appear as columns in that order"];
    await withRepo({ [PAGE_PATH]: page(), [DEMO_PATH]: demo({ texts }) }, async (root) => {
        const [result] = await syncDemos(root);
        assert.deepEqual(result.changes.map((c) => [c.kind, c.step, c.to]), [
            ["text", 1, "Given a product with no statuses"],
            ["text", 2, 'I create "New", "Doing" and "Done"'],
        ]);
        assert.equal(await read(root, DEMO_PATH), demo({ texts: ["Given a product with no statuses", 'I create "New", "Doing" and "Done"', texts[2]] }));
    });
});

test("a part that does not resolve is reported and left alone", async () => {
    const html = demo({ part: "scenarios.md#no-such-part", signature: "00000000" });
    await withRepo({ [PAGE_PATH]: page(), [DEMO_PATH]: html }, async (root) => {
        const run = cli(root);
        assert.equal(run.status, 1);
        assert.match(run.stderr, /has no part "#no-such-part"/);
        assert.equal(await read(root, DEMO_PATH), html);
    });
});

test("a named file is synced alone; without one every demo under domain/ is", async () => {
    const other = ".devbook/domain/work/board.demo.html";
    await withRepo({ [PAGE_PATH]: page(), [DEMO_PATH]: demo({ signature: "00000000" }), [other]: demo({ signature: "00000000" }) }, async (root) => {
        assert.equal(cli(root, DEMO_PATH).status, 0);
        assert.equal(await read(root, DEMO_PATH), demo());
        assert.equal(await read(root, other), demo({ signature: "00000000" }));
        assert.equal(cli(root).status, 0);
        assert.equal(await read(root, other), demo());
    });
});

test("a bad argument exits 2", async () => {
    await withRepo({}, async (root) => {
        assert.equal(cli(root, "--nope").status, 2);
    });
});

test("a demo path outside the repository is reported and never read", async () => {
    await withRepo({ [PAGE_PATH]: page(), [DEMO_PATH]: demo() }, async (root) => {
        const outside = `../${path.basename(root)}-outside.demo.html`;
        const [result] = await syncDemos(root, { files: [outside] });
        assert.deepEqual(result, { path: outside, changes: [], problems: [`${outside}: resolves outside the repository`] });
        const run = cli(root, outside);
        assert.equal(run.status, 1);
        assert.match(run.stderr, /resolves outside the repository/);
    });
});
