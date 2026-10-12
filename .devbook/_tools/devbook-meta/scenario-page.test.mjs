// scenario-page.test.mjs — the scenario page: one end-to-end journey written
// as a `.domain` page of its own, typed `scenario` on its file-level block.
//
// The per-document rules are validateDocument's: where the type may sit, the
// parts that owe no block, the fixed status set, the field set with `tests`
// refused, the setup fields only there, the step keywords, and the shape of a
// `Proved by:` case. The corpus-wide rules are the graph build's: the stem
// register, the setup fields resolved against chapters and the profile file,
// `scenario:` image references, `Proved by:` resolved to a part, the parts no
// case claims, and the `e2e` entry a pointer derives from its spec.
//
// Run: node plugins/devbook/tools/devbook-meta/scenario-page.test.mjs
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { documentDigest, testIssues, validateDocument } from "./metadata.mjs";
import { buildGraph, CONTRACT_VERSION } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const fence = (body) => "```meta\n" + body + "```\n";
const dump = (list) => JSON.stringify(list, null, 2);
const errors = (issues) => issues.filter((i) => i.severity === "error");
const warnings = (issues) => issues.filter((i) => i.severity === "warning");
const has = (issues, needle) => issues.some((i) => i.message.includes(needle));

const PAGE = ".devbook/domain/work/set-up-and-fill-the-backlog.md";
const page = (meta, body = "") =>
    `# Set up and fill the backlog\n\n${fence(meta)}\nA product owner sets up the backlog and moves the first item.\n\n` +
    `## Statuses are set up\n\n- **Given** a product with no statuses\n- **When** I create "New", "Doing" and "Done"\n- **Then** they appear as columns in that order\n\n` +
    `![The board](shot:statuses-set-up)\n\n## An item is moved\n\n- **Given** an item in "New"\n- **When** I drag it to "Doing"\n- **And** I reload the board\n- **Then** it stays in "Doing"\n` +
    body;

// ── DB1: the type, by folder and by level ──────────────────────────────────

{
    const issues = validateDocument(PAGE, page("type: scenario\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a scenario page in a bounded context is clean", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/work/journeys/backlog/set-up.md", page("type: scenario\n"));
    check(errors(issues).length === 0, "a scenario page at any depth inside a bounded context is clean", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/set-up.md", page("type: scenario\n"));
    check(has(errors(issues), "outside a bounded context"), "a scenario page at the domain/ root is an error", dump(issues));
}
for (const [folder, relPath, meta] of [
    ["arc42", ".devbook/arc42/set-up.md", "type: scenario\n"],
    ["design", ".devbook/design/set-up.md", "type: scenario\n"],
    ["tech", ".devbook/tech/set-up.md", "type: scenario\nstatus: trial\n"],
    ["ai", ".devbook/ai/set-up.md", "type: scenario\nstatus: trial\n"],
]) {
    const issues = validateDocument(relPath, `# Set up\n\n${fence(meta)}\nProse.\n`);
    check(has(errors(issues), `has type "scenario" in .${folder}`), `type: scenario in .${folder} is an error`, dump(issues));
}
for (const relPath of [".devbook/domain/work/features.md", ".devbook/arc42/05-building-block-view.md"]) {
    const fileMeta = relPath.includes("domain") ? "type: features\n" : "";
    const issues = validateDocument(relPath, `# Features\n\n${fence(fileMeta)}\n## Set up\n\n${fence("type: scenario\n")}\nProse.\n`);
    check(has(errors(issues), `has type "scenario" on a chapter`), `type: scenario on a chapter is an error (${relPath})`, dump(issues));
}

// ── DB2: parts owe no block ────────────────────────────────────────────────

{
    const issues = validateDocument(".devbook/domain/work/journeys/set-up.md", page("type: scenario\n"));
    check(!has(issues, "has no `meta` block"), "a part without a block is not reported, at any depth", dump(issues));
}

// ── DB3: status ────────────────────────────────────────────────────────────

for (const status of ["draft", "proposed"]) {
    const issues = validateDocument(PAGE, page(`type: scenario\nstatus: ${status}\n`));
    check(errors(issues).length === 0 && warnings(issues).length === 0, `status: ${status} is accepted`, dump(issues));
}
for (const status of ["active", "approved", "deprecated", "nonsense"]) {
    const issues = validateDocument(PAGE, page(`type: scenario\nstatus: ${status}\n`));
    check(has(errors(issues), `has status "${status}" on a scenario page`), `status: ${status} is an error`, dump(issues));
}

// ── DB4: the field set ─────────────────────────────────────────────────────

{
    const meta =
        "type: scenario\nrelated: [.devbook/domain/work/features.md#backlog-board]\nroadmap: [scenario-pages]\n" +
        "issue: \"#12\"\neffort: 3\ndate: 2026-10-07\nchange: add-scenarios\naliases: [Backlog setup]\next.spec-manager.pinned: yes\n";
    const issues = validateDocument(PAGE, page(meta));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "the common fields, aliases, and ext.* are accepted", dump(issues));
}
{
    const issues = validateDocument(PAGE, page("type: scenario\ntests: [e2e:playwright:tests/x.spec.ts]\n"));
    check(errors(issues).length === 1 && has(errors(issues), "`tests` on a scenario page"), "tests on a scenario page is one error", dump(issues));
}
{
    // `aliases` stays chapter-only on every other file.
    const issues = validateDocument(".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\naliases: [x]\n")}\nProse.\n`);
    check(has(errors(issues), "`aliases` on the file-level block"), "aliases on another file-level block is still an error", dump(issues));
}

// ── DB11: the setup fields, on the page alone ──────────────────────────────

{
    const meta =
        "type: scenario\nstart: admin:/products/webshop/backlog\nactor: [admin:actors.md#product-owner, customer:actors.md#shopper]\n" +
        "data: [webshop-without-statuses]\nprofile: tenant-acme\nflags: [context.md#new-board, -context.md#legacy-filters]\n" +
        "settings: [context.md#backlog-max-columns=5]\n";
    const issues = validateDocument(PAGE, page(meta));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "the six setup fields are accepted on a scenario page", dump(issues));
}
for (const [meta, needle, name] of [
    ["start: [/a, /b]\n", "`start` as a list", "start is a single value"],
    ["profile: [a, b]\n", "`profile` as a list", "profile is a single value: one profile per page"],
    ["start: products/backlog\n", "`start` \"products/backlog\"", "start is a route"],
    ["actor: product-owner\n", "`actor` entry \"product-owner\"", "actor is a chapter reference"],
    ["flags: [new-board]\n", "`flags` entry \"new-board\"", "a flag is a chapter reference"],
    ["settings: [context.md#max]\n", "`settings` entry \"context.md#max\"", "a setting carries a value"],
    ["data: [Webshop Data]\n", "`data` entry \"Webshop Data\"", "a data set is a kebab-case name"],
]) {
    const issues = validateDocument(PAGE, page(`type: scenario\n${meta}`));
    check(has(errors(issues), needle), name, dump(issues));
}
for (const [relPath, markdown, where] of [
    [".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\n")}\n## Board\n\n${fence("type: feature\nactor: actors.md#po\n")}\nProse.\n`, "a domain chapter"],
    [".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\nstart: /x\n")}\nProse.\n`, "another domain file"],
    [".devbook/arc42/01-introduction.md", `# Introduction\n\n${fence("profile: default\n")}\nProse.\n`, "an arc42 file"],
]) {
    const issues = validateDocument(relPath, markdown);
    check(has(errors(issues), "a setup field"), `a setup field on ${where} is an error`, dump(issues));
}

// ── Steps ──────────────────────────────────────────────────────────────────

for (const [line, ok, name] of [
    ["- **But** nothing else moves", false, "a bold But is an error"],
    ["- **Gegeven** een product", false, "a non-English keyword is an error"],
    ["- **GIVEN** a product", false, "the keyword's case is English title case"],
    ["- I create a status", false, "a step without a keyword is an error"],
    ["1. **When** I create a status", true, "an ordered step with a keyword is accepted"],
    ["  - a note under the step above", true, "a nested item belongs to the step above"],
]) {
    const issues = validateDocument(PAGE, page("type: scenario\n", `${line}\n`));
    const stepErrors = errors(issues).filter((i) => i.message.includes("step"));
    check(ok ? stepErrors.length === 0 : stepErrors.length === 1, name, dump(issues));
}
{
    const md = page("type: scenario\n").replace("A product owner", "- a lead list item\n\nA product owner") + "\n```text\n- inside a fence\n```\n";
    const issues = validateDocument(PAGE, md);
    check(errors(issues).length === 0, "list items in the lead and inside fences are not steps", dump(issues));
}

// ── DB13: a Proved by case, on the requirement's page ──────────────────────

const REQUIREMENTS = ".devbook/domain/work/requirements.md";
const requirements = (caseBody) =>
    `# Requirements\n\n${fence("type: requirements\n")}\n## Backlog board\n\n${fence("type: requirements\nrelated: [.devbook/domain/work/features.md#backlog-board]\n")}\n` +
    `### Requirement: Columns follow the status order\n\n${fence("type: requirement\n")}\nThe board SHALL show one column per status.\n\n#### Scenario: Statuses are set up\n${caseBody}`;
{
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: set-up-and-fill-the-backlog.md#statuses-are-set-up\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a Proved by case satisfies the coverage warning", dump(issues));
}
{
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: a.md#x\nProved by: b.md#y\n"));
    check(has(errors(issues), "holds 2 `Proved by:` lines"), "two Proved by lines in one case is an error", dump(issues));
}
{
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: a.md#x\n\n- **WHEN** a status is created\n- **THEN** a column appears\n"));
    check(has(errors(issues), "and its own Given/When/Then"), "a Proved by line beside Given/When/Then is an error", dump(issues));
}
{
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: set-up-and-fill-the-backlog\n"));
    check(has(errors(issues), "names one part of a scenario page"), "a Proved by line without a part is an error", dump(issues));
}

// ── DB6: screenshot images are no diagrams ─────────────────────────────────

{
    const md = "# T\n\nLead.\n\n![a](shot:x) ![b](scenario:page#x)\n\n![c](diagram.svg)\n\n```mermaid\ngraph TD\n```\n";
    check(documentDigest(md).diagrams === 2, "shot: and scenario: images are not counted as diagrams", JSON.stringify(documentDigest(md)));
}

// ── Side fix: a `.devbook/` path in `tests` is named as a chapter reference ─

{
    const issues = testIssues({ tests: [".devbook/domain/work/domain.md#order"] });
    check(issues.length === 1 && issues[0].message.includes("which is a chapter reference"), "a .devbook/ path in tests is reported as a chapter reference", dump(issues));
}

check(CONTRACT_VERSION === 30, "the contract is 30");

// ── The corpus-wide rules, through the graph build ─────────────────────────

const CONTEXT =
    `# Work\n\n${fence("type: context\n")}\nThe work context.\n\n## New Board\n\n${fence("type: feature-flag\nkey: new-board\n")}\nA flag.\n\n` +
    `## Backlog Max Columns\n\n${fence("type: setting\nkey: backlog-max-columns\n")}\nA setting.\n`;
const ACTORS = `# Actors\n\n${fence("type: actors\n")}\nWho works here.\n\n## Product Owner\n\n${fence("type: user\n")}\nOwns the backlog.\n`;
const FEATURES = (extra = "") =>
    `# Features\n\n${fence("type: features\n")}\nWhat it does.\n\n## Backlog Board\n\n${fence("type: feature\n")}\nThe board.\n${extra}`;
const NESTED = ".devbook/domain/work/journeys/set-up-and-fill-the-backlog.md";
const SPEC = "e2e/tests/set-up-and-fill-the-backlog.spec.ts";
const PROFILES = JSON.stringify({
    default: { portals: { app: "env:APP_URL" } },
    "tenant-acme": { portals: { admin: "env:ADMIN_URL", customer: "env:SHOP_URL" } },
});

async function graphOf(files) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-scenario-"));
    try {
        for (const [relPath, content] of Object.entries(files)) {
            await mkdir(path.join(root, path.dirname(relPath)), { recursive: true });
            await writeFile(path.join(root, relPath), content, "utf8");
        }
        return await buildGraph(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
const base = (pageMeta, extra = {}) => ({
    ".devbook/domain/work/context.md": CONTEXT,
    ".devbook/domain/work/actors.md": ACTORS,
    ".devbook/domain/work/features.md": FEATURES(),
    [NESTED]: page(pageMeta),
    ...extra,
});
const problemsAbout = (graph, needle) => graph.problems.filter((p) => p.message.includes(needle));

{
    const graph = await graphOf(base("type: scenario\nactor: actors.md#product-owner\nflags: [context.md#new-board, -context.md#new-board]\nsettings: [context.md#backlog-max-columns=5]\n"));
    const setup = graph.problems.filter((p) => /`(actor|flags|settings)` entry/.test(p.message));
    check(setup.length === 0, "actor, flags, and settings resolve from the bounded context", dump(setup));
    check(errors(graph.problems).length === 0, "a corpus with a scenario page has no errors", dump(errors(graph.problems)));
    check(graph.nodes.some((n) => n.id === NESTED && n.kind === "scenario"), "the page is a file node of kind scenario");
}
{
    const graph = await graphOf(base("type: scenario\nactor: features.md#backlog-board\nflags: [context.md#backlog-max-columns]\nsettings: [context.md#nowhere=5]\n"));
    check(problemsAbout(graph, "`actor` entry \"features.md#backlog-board\" that resolves to a `feature` chapter").length === 1, "an actor resolving to a feature is an error", dump(graph.problems));
    check(problemsAbout(graph, "`flags` entry \"context.md#backlog-max-columns\" that resolves to a `setting` chapter").length === 1, "a flag resolving to a setting is an error", dump(graph.problems));
    check(problemsAbout(graph, "`settings` entry \"context.md#nowhere\" that does not resolve").length === 1, "an unresolved setting is an error", dump(graph.problems));
}

// The profile file: a named profile must exist there, and a portal must be one it defines.
{
    const graph = await graphOf(base("type: scenario\nprofile: tenant-acme\nstart: admin:/backlog\nactor: [customer:actors.md#product-owner]\n", { ".devbook/scenarios/profiles.json": PROFILES }));
    check(errors(graph.problems).length === 0, "a profile and portals it defines are accepted", dump(errors(graph.problems)));
}
{
    const graph = await graphOf(base("type: scenario\nprofile: tenant-beta\n", { ".devbook/scenarios/profiles.json": PROFILES }));
    check(problemsAbout(graph, "`profile` \"tenant-beta\"").length === 1, "a profile the file does not define is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\nprofile: tenant-beta\n"));
    check(problemsAbout(graph, "profile").length === 0, "without a profile file, a profile is not checked", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\nstart: admin:/backlog\nactor: [shop:actors.md#product-owner]\n", { ".devbook/scenarios/profiles.json": PROFILES }));
    check(problemsAbout(graph, "names portal \"admin\" in `start`, which profile \"default\" does not define").length === 1, "a start portal the default profile lacks is an error", dump(graph.problems));
    check(problemsAbout(graph, "names portal \"shop\" in `actor`").length === 1, "an actor portal the profile lacks is an error", dump(graph.problems));
}

// DB5: the stem is unique across every bounded context.
{
    const graph = await graphOf(base("type: scenario\n", { ".devbook/domain/shop/set-up-and-fill-the-backlog.md": page("type: scenario\n") }));
    check(problemsAbout(graph, "has the stem \"set-up-and-fill-the-backlog\"").length === 1, "a stem duplicated across contexts is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", { ".devbook/domain/shop/move-an-item.md": page("type: scenario\n") }));
    check(problemsAbout(graph, "has the stem").length === 0, "distinct stems are accepted", dump(graph.problems));
}

// DB2: a part is addressable as a structural anchor.
{
    const graph = await graphOf(base("type: scenario\n", {
        ".devbook/domain/work/features.md": FEATURES().replace("type: feature\n", `type: feature\nrelated: [${NESTED}#statuses-are-set-up]\n`),
    }));
    check(problemsAbout(graph, "does not resolve").length === 0, "related into a part resolves", dump(graph.problems));
    check(graph.edges.some((e) => e.type === "related" && e.target === `${NESTED}#statuses-are-set-up`), "the part is a related edge target");
}

// DB6: a `scenario:` image names a page, and a label on it.
{
    const images =
        "\n![Board](scenario:set-up-and-fill-the-backlog#statuses-set-up)\n\n![Gone](scenario:no-such-page#x)\n\n![Typo](scenario:set-up-and-fill-the-backlog#missing)\n";
    const graph = await graphOf(base("type: scenario\n", { ".devbook/domain/work/features.md": FEATURES(images) }));
    check(problemsAbout(graph, "no scenario page has the stem \"no-such-page\"").filter((p) => p.severity === "error").length === 1, "a scenario: reference to an unknown stem is an error", dump(graph.problems));
    check(problemsAbout(graph, "has no screenshot point `shot:missing`").filter((p) => p.severity === "warning").length === 1, "an unknown label is a warning", dump(graph.problems));
    check(problemsAbout(graph, "#statuses-set-up").length === 0, "a known stem and label are accepted", dump(graph.problems));
}

// DB13: Proved by resolves to a part, derives the e2e entry, and claims the part.
const proved = (target) => ({
    [REQUIREMENTS]: requirements(`Proved by: ${target}\n`),
    [SPEC]: `// scenario: ${NESTED}\n// signature: 9c41e2a0\nimport { scenario } from "x";\n`,
});
{
    const graph = await graphOf(base("type: scenario\n", proved("set-up-and-fill-the-backlog.md#statuses-are-set-up")));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(problemsAbout(graph, "has `Proved by:").length === 0, "a Proved by pointer to a part resolves by stem", dump(graph.problems));
    check(
        JSON.stringify(requirement?.tests) === JSON.stringify([`e2e:playwright:${SPEC}#Statuses are set up`]),
        "the requirement's e2e tests entry is derived from the spec header",
        JSON.stringify(requirement)
    );
    check(problemsAbout(graph, "part \"#statuses-are-set-up\" is an unclaimed journey").length === 0, "a claimed part is not reported");
    check(problemsAbout(graph, "part \"#an-item-is-moved\" is an unclaimed journey").filter((p) => p.severity === "warning").length === 1, "a part no requirement points at is an unclaimed journey", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved(`${NESTED}#statuses-are-set-up`)));
    check(problemsAbout(graph, "has `Proved by:").length === 0, "a Proved by pointer by repository path resolves", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", { [REQUIREMENTS]: requirements("Proved by: set-up-and-fill-the-backlog.md#statuses-are-set-up\n") }));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(requirement && requirement.tests === undefined, "without a spec naming the page, nothing is derived", JSON.stringify(requirement));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("set-up-and-fill-the-backlog.md#no-such-part")));
    check(problemsAbout(graph, "has no part \"#no-such-part\"").filter((p) => p.severity === "error").length === 1, "a pointer to a missing part is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("features.md#backlog-board")));
    check(problemsAbout(graph, "no scenario page has the stem \"features\"").filter((p) => p.severity === "error").length === 1, "a pointer to a page that is no scenario page is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", {
        ...proved("set-up-and-fill-the-backlog.md#statuses-are-set-up"),
        ".devbook/domain/shop/set-up-and-fill-the-backlog.md": page("type: scenario\n"),
    }));
    check(problemsAbout(graph, "the stem is ambiguous").length === 1, "a pointer by a duplicated stem is ambiguous", dump(graph.problems));
}

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
