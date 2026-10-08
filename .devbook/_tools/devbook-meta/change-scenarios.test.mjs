// change-scenarios.test.mjs — `delta.mjs --check` runs the corpus-wide
// scenario rules over the corpus as the change would leave it: every delta of
// the change merged in memory, scenario.mjs's pass over the result, and each
// problem the change introduces reported against the delta that causes it.
//
// Run: node plugins/devbook/tools/devbook-meta/change-scenarios.test.mjs
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { CHANGES_ROOT } from "./metadata.mjs";
import { buildGraph } from "./graph.mjs";
import { checkChange } from "./delta.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const fence = (body) => "```meta\n" + body + "```\n";
const dump = (list) => JSON.stringify(list, null, 2);
const errorsOf = (issues) => issues.filter((i) => i.severity === "error");

const CTX = ".devbook/domain/work";
const REQUIREMENTS = `${CTX}/requirements.md`;
const PAGE = `${CTX}/set-up-and-fill-the-backlog.md`;
const BASE = `${CHANGES_ROOT}/add-journey`;
const DELTA = `${BASE}/devbook-delta/domain/work`;
const header = (delta) => fence(`change: add-journey\ndelta: ${delta}\n`);

const ACTORS = `# Actors\n\n${fence("type: actors\n")}\nWho works here.\n\n## Product Owner\n\n${fence("type: user\n")}\nOwns the backlog.\n`;
const FEATURES = `# Features\n\n${fence("type: features\n")}\nWhat it does.\n\n## Backlog Board\n\n${fence("type: feature\n")}\nThe board.\n`;
const requirements =
    `# Requirements\n\n${fence("type: requirements\n")}\n## Backlog board\n\n${fence(`type: requirements\nrelated: [${CTX}/features.md#backlog-board]\n`)}\n` +
    `### Requirement: Columns follow the status order\n\n${fence("type: requirement\n")}\nThe board SHALL show one column per status.\n\n` +
    `#### Scenario: Statuses are set up\n\n- **WHEN** a status is created\n- **THEN** a column appears\n`;
const PARTS =
    `### Statuses are set up\n\n- **Given** a product with no statuses\n- **When** I create "New", "Doing" and "Done"\n- **Then** they appear as columns in that order\n\n` +
    `### An item is moved\n\n- **Given** an item in "New"\n- **When** I drag it to "Doing"\n- **Then** it stays in "Doing"\n`;
const pageDelta = (meta = "type: scenario\n") =>
    `${header("added")}\n# Set up and fill the backlog\n\n${fence(meta)}\nA product owner sets up the backlog and moves the first item.\n\n## ADDED\n\n${PARTS}`;
const provedBy = (target) =>
    `${header("modified")}\n### Requirement: Columns follow the status order\n\n#### MODIFIED\n\n##### Scenario: Statuses are set up\n\nProved by: ${target}\n`;
const proposal =
    `# Add journey\n\n${fence("type: change\nstatus: proposed\ncategory: feature\n")}\n` +
    `## Why\n\nThe board's setup is one journey.\n\n## Scope\n\nThe journey and its requirement.\n\n## Chapters touched\n\n- ${REQUIREMENTS}\n- ${PAGE}\n`;

async function fixture(extra = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-change-scenarios-"));
    const files = {
        [`${CTX}/actors.md`]: ACTORS,
        [`${CTX}/features.md`]: FEATURES,
        [REQUIREMENTS]: requirements,
        [`${BASE}/proposal.md`]: proposal,
        [`${DELTA}/set-up-and-fill-the-backlog.md`]: pageDelta(),
        [`${DELTA}/requirements.md`]: provedBy("set-up-and-fill-the-backlog.md#statuses-are-set-up"),
        ...extra,
    };
    for (const [rel, text] of Object.entries(files)) {
        if (text === null) continue;
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text, "utf8");
    }
    return root;
}

async function report(extra) {
    const root = await fixture(extra);
    try {
        return await checkChange(root, "add-journey");
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
const issuesOf = (r, suffix) => r.deltas.find((d) => d.path.endsWith(suffix))?.issues ?? [];
const all = (r) => [...r.problems, ...r.deltas.flatMap((d) => d.issues)];

// ── A Proved by naming a page the same change adds ─────────────────────────
{
    const r = await report();
    check(errorsOf(all(r)).length === 0, "a Proved by pointing at a page added by the same change resolves", dump(all(r)));
    check(
        issuesOf(r, "/set-up-and-fill-the-backlog.md").some((i) => i.severity === "warning" && i.message.includes('part "#an-item-is-moved" is an unclaimed journey')),
        "a part the change adds and nothing claims is a warning on the page's delta",
        dump(all(r))
    );
}

// ── A Proved by typo ───────────────────────────────────────────────────────
{
    const r = await report({ [`${DELTA}/requirements.md`]: provedBy("set-up-and-fill-the-backlog.md#statuses-are-setup") });
    const issues = errorsOf(issuesOf(r, "/requirements.md"));
    check(
        issues.length === 1 && issues[0].message.includes('has no part "#statuses-are-setup"'),
        "a Proved by naming a part that does not exist is an error on the requirement's delta",
        dump(all(r))
    );
    check(errorsOf(issuesOf(r, "/set-up-and-fill-the-backlog.md")).length === 0, "the typo is not blamed on the page's delta", dump(all(r)));
}
{
    const r = await report({ [`${DELTA}/requirements.md`]: provedBy("set-up-the-backlog.md#statuses-are-set-up") });
    check(
        errorsOf(issuesOf(r, "/requirements.md")).some((i) => i.message.includes('no scenario page has the stem "set-up-the-backlog"')),
        "a Proved by naming a stem that does not exist is an error on the requirement's delta",
        dump(all(r))
    );
}

// ── A stem that collides with an existing page ─────────────────────────────
for (const other of [".devbook/domain/shop/set-up-and-fill-the-backlog.md", ".devbook/domain/accounts/set-up-and-fill-the-backlog.md"]) {
    const page = `# Set up elsewhere\n\n${fence("type: scenario\n")}\nLead.\n\n## Only part\n\n- **Given** a thing\n- **Then** it holds\n`;
    const r = await report({ [other]: page });
    check(
        errorsOf(issuesOf(r, "/set-up-and-fill-the-backlog.md")).some((i) => i.message.includes('has the stem "set-up-and-fill-the-backlog"')),
        `a stem colliding with ${other.split("/")[2]}'s page is an error on the page's delta, whichever sorts first`,
        dump(all(r))
    );
}

// ── Setup fields that do not resolve ───────────────────────────────────────
{
    const r = await report({
        [`${DELTA}/set-up-and-fill-the-backlog.md`]: pageDelta("type: scenario\nactor: actors.md#nobody\nflags: [features.md#backlog-board]\nsettings: [context.md#max=5]\n"),
    });
    const issues = errorsOf(issuesOf(r, "/set-up-and-fill-the-backlog.md"));
    check(issues.some((i) => i.message.includes('`actor` entry "actors.md#nobody" that does not resolve')), "an actor that does not resolve is an error on the page's delta", dump(all(r)));
    check(issues.some((i) => i.message.includes('`flags` entry "features.md#backlog-board" that resolves to a `feature` chapter')), "a flag of the wrong kind is an error on the page's delta", dump(all(r)));
    check(issues.some((i) => i.message.includes('`settings` entry "context.md#max"')), "a setting that does not resolve is an error on the page's delta", dump(all(r)));
}
{
    const r = await report({ [`${DELTA}/set-up-and-fill-the-backlog.md`]: pageDelta("type: scenario\nactor: actors.md#product-owner\n") });
    check(errorsOf(all(r)).length === 0, "an actor in the corpus resolves", dump(all(r)));
}

// ── A part the change removes, under a pointer the change leaves alone ─────
{
    const existing = `# Set up and fill the backlog\n\n${fence("type: scenario\n")}\nLead.\n\n${PARTS.replace(/^### /gm, "## ")}`;
    const removal = `${header("modified")}\n# Set up and fill the backlog\n\n## REMOVED\n\n### Statuses are set up\n`;
    const r = await report({
        [PAGE]: existing,
        [REQUIREMENTS]: requirements.replace(/#### Scenario: Statuses are set up\n[\s\S]*$/, "#### Scenario: Statuses are set up\n\nProved by: set-up-and-fill-the-backlog.md#statuses-are-set-up\n"),
        [`${DELTA}/set-up-and-fill-the-backlog.md`]: removal,
        [`${DELTA}/requirements.md`]: header("modified"),
    });
    check(
        errorsOf(issuesOf(r, "/set-up-and-fill-the-backlog.md")).some((i) => i.message.includes('has no part "#statuses-are-set-up"')),
        "removing a part a requirement the change leaves alone points at is an error on the page's delta",
        dump(all(r))
    );
}

// ── What the corpus already has is not the change's ───────────────────────
{
    const stray = `# Elsewhere\n\n${fence("type: features\n")}\nLead.\n\n![Gone](scenario:no-such-page#x)\n`;
    const r = await report({ [".devbook/domain/shop/features.md"]: stray });
    check(!all(r).some((i) => i.message.includes("no-such-page")), "a scenario problem the corpus already has is not reported against the change", dump(all(r)));
}

// ── A delta that does not merge skips the corpus pass ──────────────────────
{
    const r = await report({ [`${DELTA}/requirements.md`]: `${header("modified")}\n### Requirement: Nowhere\n\n#### MODIFIED\n\n##### Scenario: X\n\nProved by: set-up-and-fill-the-backlog.md#typo\n` });
    check(!all(r).some((i) => i.message.includes("with the change merged")), "a change whose deltas do not merge is not checked across the corpus", dump(all(r)));
}

// ── The graph build, with the change open ──────────────────────────────────
{
    const root = await fixture();
    try {
        const graph = await buildGraph(root);
        const scenario = errorsOf(graph.problems).filter((p) => /Proved by|scenario page|stem/.test(p.message));
        check(scenario.length === 0, "an open change adding a page and its pointer indexes with no scenario error", dump(scenario));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
