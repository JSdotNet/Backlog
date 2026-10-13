// The run reporter: run.json version 2 with its English fields, one PNG per
// label, a filtered run writing nothing, an unreached label keeping its file,
// a dropped label pruned, the failure screenshot and the failing step of a
// failed part, each shot's signature, and a listing writing nothing.
//
// Run: `node --test report.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readdir, readFile, writeFile, rm } from "node:fs/promises";
import { existsSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { parseScenarioPage } from "./parse.mjs";
import ScenarioReporter, { failureOf, outcomeOf, plainMessage, specFileOf, writeRun } from "./report.mjs";

const F = "```";
const PAGE_PATH = ".devbook/domain/work/set-up-and-fill-the-backlog.md";
const PAGE = `# Set up and fill the backlog

${F}meta
type: scenario
start: /products/webshop/backlog
profile: tenant-acme
flags: [context.md#new-board]
settings: [context.md#backlog-max-columns=5]
data: [webshop-without-statuses]
${F}

## Statuses are set up
- **Given** a product "Webshop"
- **Then** the board has no columns

![Empty board](shot:statuses-set-up)

## An item is moved
- **When** I drag it
- **Then** it moves

![Moved](shot:item-moved)
`;
const SPEC = `// scenario: ${PAGE_PATH}
// signature: 1234abcd
scenario('set-up-and-fill-the-backlog', ({ part }) => {
  part('Statuses are set up', async ({ page }) => { await shot(page, 'statuses-set-up'); });
  part('An item is moved', async ({ page }) => { await shot(page, 'item-moved'); });
});
`;
const PROFILES = { "tenant-acme": { portals: { app: "env:APP_URL" }, tenant: "acme", flags: { "new-board": false, "legacy-filters": true }, settings: { "backlog-max-columns": 8 } } };

async function repo() {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-report-"));
    await mkdir(path.join(root, ".devbook/domain/work"), { recursive: true });
    await mkdir(path.join(root, ".devbook/scenarios"), { recursive: true });
    await mkdir(path.join(root, "e2e/tests"), { recursive: true });
    await mkdir(path.join(root, "out"), { recursive: true });
    await writeFile(path.join(root, PAGE_PATH), PAGE);
    await writeFile(path.join(root, ".devbook/scenarios/profiles.json"), JSON.stringify(PROFILES));
    await writeFile(path.join(root, "e2e/tests/set-up-and-fill-the-backlog.spec.ts"), SPEC);
    for (const name of ["a.png", "b.png", "fail.png"]) await writeFile(path.join(root, "out", name), name);
    return root;
}

const runFolder = (root) => path.join(root, ".devbook/scenarios/set-up-and-fill-the-backlog");
const readRun = async (root) => JSON.parse(await readFile(path.join(runFolder(root), "run.json"), "utf8"));

test("outcomeOf: a clean pass, a retry, a skip, and nothing at all", () => {
    assert.equal(outcomeOf(["passed"]), "passed");
    assert.equal(outcomeOf(["failed", "passed"]), "failed");
    assert.equal(outcomeOf(["timedOut"]), "failed");
    assert.equal(outcomeOf(["skipped"]), "not-run");
    assert.equal(outcomeOf([]), "not-run");
});

test("writeRun writes run.json version 2 and one PNG per label", async () => {
    const root = await repo();
    try {
        const page = parseScenarioPage(PAGE, PAGE_PATH);
        const config = { tenant: "acme", flags: { "new-board": true }, settings: { "backlog-max-columns": 5 }, data: ["webshop-without-statuses"] };
        await writeRun({
            repoRoot: root,
            page,
            signature: "1234abcd",
            ranAt: "2026-10-08T08:30:00.000Z",
            profile: "tenant-acme",
            effectiveConfig: config,
            parts: { "statuses-are-set-up": { outcome: "passed", durationMs: 3100 }, "an-item-is-moved": { outcome: "passed", durationMs: 900 } },
            captured: { "statuses-set-up": { path: path.join(root, "out/a.png") }, "item-moved": { path: path.join(root, "out/b.png") } },
        });
        const run = await readRun(root);
        assert.deepEqual(Object.keys(run), ["version", "page", "signature", "ranAt", "profile", "effectiveConfig", "parts", "shots"]);
        assert.equal(run.version, 2);
        assert.equal(run.page, PAGE_PATH);
        assert.equal(run.signature, "1234abcd");
        assert.equal(run.profile, "tenant-acme");
        assert.deepEqual(run.effectiveConfig, config);
        assert.deepEqual(run.parts, [
            { title: "Statuses are set up", anchor: "statuses-are-set-up", outcome: "passed", durationMs: 3100 },
            { title: "An item is moved", anchor: "an-item-is-moved", outcome: "passed", durationMs: 900 },
        ]);
        assert.deepEqual(run.shots, {
            "statuses-set-up": { file: "statuses-set-up.png", part: "statuses-are-set-up", ranAt: "2026-10-08T08:30:00.000Z", signature: "1234abcd" },
            "item-moved": { file: "item-moved.png", part: "an-item-is-moved", ranAt: "2026-10-08T08:30:00.000Z", signature: "1234abcd" },
        });
        assert.deepEqual((await readdir(runFolder(root))).sort(), ["item-moved.png", "run.json", "statuses-set-up.png"]);
        assert.equal(await readFile(path.join(runFolder(root), "statuses-set-up.png"), "utf8"), "a.png");
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("an unreached label keeps its file and earlier entry; a dropped label and a stale failure shot are pruned", async () => {
    const root = await repo();
    try {
        const page = parseScenarioPage(PAGE, PAGE_PATH);
        const first = "2026-10-01T00:00:00.000Z";
        await writeRun({
            repoRoot: root, page, signature: "1234abcd", ranAt: first, profile: "tenant-acme", effectiveConfig: null,
            parts: { "statuses-are-set-up": { outcome: "passed" }, "an-item-is-moved": { outcome: "failed" } },
            captured: { "statuses-set-up": { path: path.join(root, "out/a.png") }, "item-moved": { path: path.join(root, "out/b.png") } },
            failures: { "an-item-is-moved": { path: path.join(root, "out/fail.png") } },
        });
        assert.ok(existsSync(path.join(runFolder(root), "fail.an-item-is-moved.png")));
        assert.equal((await readRun(root)).parts[1].failureShot, "fail.an-item-is-moved.png");
        await writeFile(path.join(runFolder(root), "old-label.png"), "x");

        const second = "2026-10-08T00:00:00.000Z";
        const { pruned } = await writeRun({
            repoRoot: root, page, signature: "5678ef00", ranAt: second, profile: "tenant-acme", effectiveConfig: null,
            parts: { "statuses-are-set-up": { outcome: "failed" } },
            captured: { "statuses-set-up": { path: path.join(root, "out/b.png") } },
        });
        const run = await readRun(root);
        assert.equal(run.parts[1].outcome, "not-run");
        assert.deepEqual(run.shots["statuses-set-up"], { file: "statuses-set-up.png", part: "statuses-are-set-up", ranAt: second, signature: "5678ef00" });
        // Kept from the first run, with the signature of the run that took it.
        assert.deepEqual(run.shots["item-moved"], { file: "item-moved.png", part: "an-item-is-moved", ranAt: first, signature: "1234abcd" });
        assert.deepEqual(pruned.sort(), ["fail.an-item-is-moved.png", "old-label.png"]);
        assert.deepEqual((await readdir(runFolder(root))).sort(), ["item-moved.png", "run.json", "statuses-set-up.png"]);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

// The reporter receives Playwright's objects; these are the fields it reads.
// `results` maps a title to its attempts, or `<project>|<title>` with several projects.
// As in Playwright, a part's own location is setup.ts, where `part()` calls `test()`;
// only its file suite names the spec.
function fakeRun(root, { titles = ["Statuses are set up", "An item is moved"], projects = ["chromium"], results }) {
    const file = path.join(root, "e2e/tests/set-up-and-fill-the-backlog.spec.ts");
    const setup = path.join(root, ".devbook/_tools/scenarios/setup.ts");
    const tests = projects.flatMap((name) => {
        const project = () => ({ name });
        const fileSuite = { type: "file", location: { file }, project, parent: { type: "project", project } };
        const group = { type: "describe", title: "set-up-and-fill-the-backlog", location: { file: setup }, project, parent: fileSuite };
        return titles.map((title) => ({ title, location: { file: setup }, parent: group }));
    });
    return { config: { grep: /.*/, projects: projects.map((name) => ({ name, grep: /.*/ })) }, suite: { allTests: () => tests }, tests, results };
}

async function report(root, run, env = { APP_URL: "http://localhost:5000" }, options = {}) {
    const saved = { ...process.env };
    Object.assign(process.env, env);
    try {
        const reporter = new ScenarioReporter({ repoRoot: root, ...options });
        reporter.onBegin(run.config, run.suite);
        for (const [key, attempts] of Object.entries(run.results)) {
            const [project, title] = key.includes("|") ? key.split("|") : [run.config.projects[0].name, key];
            const t = run.tests.find((candidate) => candidate.title === title && candidate.parent.project().name === project);
            for (const attempt of attempts) reporter.onTestEnd(t, attempt);
        }
        await reporter.onEnd({ status: "passed" });
    } finally {
        process.env = saved;
    }
}

test("the reporter writes a full run, with the spec's signature and the effective configuration", async () => {
    const root = await repo();
    try {
        await report(root, fakeRun(root, {
            results: {
                "Statuses are set up": [{ status: "passed", duration: 10, attachments: [{ name: "scenario-shot:statuses-set-up", path: path.join(root, "out/a.png") }] }],
                "An item is moved": [
                    { status: "failed", duration: 5, attachments: [{ name: "scenario-failure", path: path.join(root, "out/fail.png") }] },
                    { status: "failed", duration: 6, attachments: [{ name: "scenario-shot:item-moved", path: path.join(root, "out/b.png") }, { name: "scenario-failure", path: path.join(root, "out/fail.png") }] },
                ],
            },
        }));
        const run = await readRun(root);
        assert.equal(run.signature, "1234abcd");
        assert.equal(run.profile, "tenant-acme");
        assert.deepEqual(run.effectiveConfig, { tenant: "acme", flags: { "new-board": true, "legacy-filters": true }, settings: { "backlog-max-columns": 5 }, data: ["webshop-without-statuses"] });
        assert.deepEqual(run.parts.map((part) => [part.outcome, part.durationMs, part.failureShot]), [["passed", 10, undefined], ["failed", 11, "fail.an-item-is-moved.png"]]);
        assert.deepEqual(Object.keys(run.shots), ["statuses-set-up", "item-moved"]);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("a filtered run — a part the spec declares left unscheduled — writes nothing", async () => {
    const root = await repo();
    try {
        const passed = { "Statuses are set up": [{ status: "passed", duration: 1, attachments: [{ name: "scenario-shot:statuses-set-up", path: path.join(root, "out/a.png") }] }] };
        await report(root, fakeRun(root, { titles: ["Statuses are set up"], results: passed }));
        assert.equal(existsSync(runFolder(root)), false);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("screenshots attached as bytes are written, and with two projects only the first one's results count", async () => {
    const root = await repo();
    try {
        const shotA = { name: "scenario-shot:statuses-set-up", body: Buffer.from("chromium-bytes") };
        const shotB = { name: "scenario-shot:statuses-set-up", body: Buffer.from("webkit-bytes") };
        await report(root, fakeRun(root, {
            projects: ["chromium", "webkit"],
            results: {
                "chromium|Statuses are set up": [{ status: "passed", duration: 1, attachments: [shotA] }],
                "chromium|An item is moved": [{ status: "passed", duration: 1, attachments: [] }],
                "webkit|Statuses are set up": [{ status: "failed", duration: 1, attachments: [shotB] }],
                "webkit|An item is moved": [{ status: "skipped", duration: 0, attachments: [] }],
            },
        }));
        const run = await readRun(root);
        assert.deepEqual(run.parts.map((part) => part.outcome), ["passed", "passed"]);
        assert.equal(await readFile(path.join(runFolder(root), "statuses-set-up.png"), "utf8"), "chromium-bytes");
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("a page that cannot be written is reported and does not stop the others", async () => {
    const root = await repo();
    const errors = [];
    const original = console.error;
    console.error = (message) => errors.push(message);
    try {
        await report(root, fakeRun(root, {
            results: {
                "Statuses are set up": [{ status: "passed", duration: 1, attachments: [{ name: "scenario-shot:statuses-set-up", path: path.join(root, "out/gone.png") }] }],
                "An item is moved": [{ status: "passed", duration: 1, attachments: [] }],
            },
        }));
        assert.equal(errors.length, 1);
        assert.match(errors[0], /no run written/);
    } finally {
        console.error = original;
        await rm(root, { recursive: true, force: true });
    }
});

test("specFileOf: the file suite's location, else the test's own", () => {
    const fileSuite = { type: "file", location: { file: "/repo/e2e/tests/journey.spec.ts" } };
    const declared = { location: { file: "/repo/.devbook/_tools/scenarios/setup.ts" }, parent: { type: "describe", parent: fileSuite } };
    assert.equal(specFileOf(declared), "/repo/e2e/tests/journey.spec.ts");
    assert.equal(specFileOf({ location: { file: "/repo/e2e/plain.spec.ts" } }), "/repo/e2e/plain.spec.ts");
});

test("a listing — every part scheduled, none run — writes nothing", async () => {
    const root = await repo();
    try {
        await report(root, fakeRun(root, { results: {} }));
        assert.equal(existsSync(runFolder(root)), false);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("failureOf: the page step whose step() threw, numbered across the part, and the message without colours", () => {
    const part = parseScenarioPage(PAGE, PAGE_PATH).parts[1];
    const error = { message: "\u001b[31mExpected: 3 columns\u001b[39m\nReceived: 2" };
    const steps = [
        { title: "Before Hooks", category: "hook", steps: [] },
        { title: "When I drag it", category: "test.step", steps: [] },
        { title: "Then  it moves", category: "test.step", error, steps: [{ title: "expect.toHaveCount", category: "expect", error, steps: [] }] },
    ];
    assert.deepEqual(failureOf(part, steps, error), { step: 2, message: "Expected: 3 columns\nReceived: 2" });
    // Thrown outside every page step — in the setup, say — the message stands alone.
    assert.deepEqual(failureOf(part, [{ title: "beforeAll hook", category: "hook", error, steps: [] }], error), { step: null, message: "Expected: 3 columns\nReceived: 2" });
    assert.equal(plainMessage("x".repeat(3000)).length, 2001);
});

test("failureOf: a step text the part repeats is numbered by the occurrence that threw", () => {
    const part = { steps: [{ keyword: "When", text: "I press Save" }, { keyword: "Then", text: "it saves" }, { keyword: "When", text: "I press Save" }] };
    const error = { message: "no Save button" };
    const steps = [
        { title: "When I press Save", category: "test.step", steps: [] },
        { title: "Then it saves", category: "test.step", steps: [] },
        { title: "When I press Save", category: "test.step", error, steps: [] },
    ];
    assert.equal(failureOf(part, steps, error).step, 3);
});

test("the reporter records a failed part's step and message in run.json, and a passed part none", async () => {
    const root = await repo();
    try {
        const error = { message: "Expected: 3 columns" };
        await report(root, fakeRun(root, {
            results: {
                "Statuses are set up": [{ status: "passed", duration: 1, attachments: [], steps: [] }],
                "An item is moved": [{ status: "failed", duration: 1, attachments: [], error, steps: [{ title: "Then it moves", category: "test.step", error, steps: [] }] }],
            },
        }));
        const run = await readRun(root);
        assert.equal(run.parts[0].failure, undefined);
        assert.deepEqual(run.parts[1].failure, { step: 2, message: "Expected: 3 columns" });
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
