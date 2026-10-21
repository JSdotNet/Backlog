// scenario-page.test.mjs — the journey: one end-to-end walk written as a `##`
// chapter typed `scenario` in a bounded context's scenarios file, its `###`
// headings the parts.
//
// The per-document rules are validateDocument's: where the type may sit, the
// parts that owe no block, the anchors unique in a file, the fixed status set,
// the field set with `tests` refused, the setup fields only on a journey, the
// step keywords, and the shape of a `Proved by:` case. The corpus-wide rules
// are the graph build's: the setup fields resolved against chapters and the
// profile file, `scenario:` image references, `Proved by:` resolved to a part,
// the parts no case claims, the journey in the wrong context, and the `e2e`
// entry a pointer derives from its spec.
//
// Run: node plugins/devbook/tools/devbook-meta/scenario-page.test.mjs
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { documentDigest, testIssues, validateDocument } from "./metadata.mjs";
import { buildGraph, CONTRACT_VERSION } from "./graph.mjs";
import { parseScenariosFile } from "../scenarios/parse.mjs";
import { signatureOf } from "../scenarios/signature.mjs";

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

const FILE = ".devbook/domain/work/scenarios.md";
const JOURNEY = `${FILE}#set-up-and-fill-the-backlog`;
const scenarios = (meta, body = "") =>
    `# Scenarios\n\n${fence("type: scenarios\n")}\nThe journeys of the work context.\n\n` +
    `## Set up and fill the backlog\n\n${fence(meta)}\nA product owner sets up the backlog and moves the first item.\n\n` +
    `### Statuses are set up\n\n- **Given** a product with no statuses\n- **When** I create "New", "Doing" and "Done"\n- **Then** they appear as columns in that order\n\n` +
    `![The board](shot:statuses-set-up)\n\n### An item is moved\n\n- **Given** an item in "New"\n- **When** I drag it to "Doing"\n- **And** I reload the board\n- **Then** it stays in "Doing"\n` +
    body;

// ── DB1: the type, by file, level, and folder ──────────────────────────────

{
    const issues = validateDocument(FILE, scenarios("type: scenario\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a journey in a scenarios file is clean", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/work/scenarios.returns.md", scenarios("type: scenario\n"));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "a journey in a split scenarios file is clean", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/work/set-up.md", `# Set up\n\n${fence("type: scenario\n")}\n## A part\n\n- **Given** a\n`);
    check(has(errors(issues), "032-scenarios-md"), "a whole file typed scenario is an error naming the migration", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/work/journeys/scenarios.md", scenarios("type: scenario\n"));
    check(has(errors(issues), "outside a scenarios file"), "a scenarios file below the context folder is none, and its journey is an error", dump(issues));
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
{
    const issues = validateDocument(".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\n")}\n## Set up\n\n${fence("type: scenario\n")}\nProse.\n`);
    check(has(errors(issues), "outside a scenarios file"), "a journey chapter in another domain file is an error", dump(issues));
}
{
    const issues = validateDocument(FILE, scenarios("type: scenario\n", `\n## Notes\n\nProse.\n`));
    check(has(errors(issues), "without `type: scenario`"), "a ## of a scenarios file that is no journey is an error", dump(issues));
}
{
    const issues = validateDocument(FILE, scenarios("type: scenario\n").replace("### An item is moved\n", `### An item is moved\n\n${fence("type: sub-feature\n")}\n`));
    check(has(errors(issues), "carries a `meta` block"), "a part carrying a block is an error", dump(issues));
}
{
    const issues = validateDocument(FILE, scenarios("type: scenario\n", "\n### Statuses are set up\n\n- **Given** again\n"));
    check(has(errors(issues), 'the anchor "#statuses-are-set-up"'), "a part anchor repeated in the file is an error", dump(issues));
}

// ── DB2: parts owe no block ────────────────────────────────────────────────

{
    const issues = validateDocument(FILE, scenarios("type: scenario\n", "\n#### A note under the part\n\nProse.\n"));
    check(!has(issues, "has no `meta` block"), "a part, and a heading under it, without a block is not reported", dump(issues));
}

// ── DB3: status ────────────────────────────────────────────────────────────

for (const status of ["draft", "proposed"]) {
    const issues = validateDocument(FILE, scenarios(`type: scenario\nstatus: ${status}\n`));
    check(errors(issues).length === 0 && warnings(issues).length === 0, `status: ${status} is accepted`, dump(issues));
}
for (const status of ["active", "approved", "deprecated", "nonsense"]) {
    const issues = validateDocument(FILE, scenarios(`type: scenario\nstatus: ${status}\n`));
    check(has(errors(issues), `has status "${status}" on a journey`), `status: ${status} is an error`, dump(issues));
}

// ── DB4: the field set ─────────────────────────────────────────────────────

{
    const meta =
        "type: scenario\nrelated: [.devbook/domain/work/features.md#backlog-board]\nroadmap: [scenario-pages]\n" +
        "issue: \"#12\"\neffort: 3\ndate: 2026-10-07\nchange: add-scenarios\naliases: [Backlog setup]\next.spec-manager.pinned: yes\n";
    const issues = validateDocument(FILE, scenarios(meta));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "the common fields, aliases, and ext.* are accepted on a journey", dump(issues));
}
{
    const issues = validateDocument(FILE, scenarios("type: scenario\ntests: [e2e:playwright:tests/x.spec.ts]\n"));
    check(errors(issues).length === 1 && has(errors(issues), "`tests` on a journey"), "tests on a journey is one error", dump(issues));
}
{
    const issues = validateDocument(".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\naliases: [x]\n")}\nProse.\n`);
    check(has(errors(issues), "`aliases` on the file-level block"), "aliases on a file-level block is an error", dump(issues));
}

// ── DB11: the setup fields, on a journey alone ─────────────────────────────

{
    const meta =
        "type: scenario\nstart: admin:/products/webshop/backlog\nactor: [admin:actors.md#product-owner, customer:actors.md#shopper]\n" +
        "data: [webshop-without-statuses]\nprofile: tenant-acme\nflags: [context.md#new-board, -context.md#legacy-filters]\n" +
        "settings: [context.md#backlog-max-columns=5]\n";
    const issues = validateDocument(FILE, scenarios(meta));
    check(errors(issues).length === 0 && warnings(issues).length === 0, "the six setup fields are accepted on a journey", dump(issues));
}
for (const [meta, needle, name] of [
    ["start: [/a, /b]\n", "`start` as a list", "start is a single value"],
    ["profile: [a, b]\n", "`profile` as a list", "profile is a single value: one profile per journey"],
    ["start: products/backlog\n", "`start` \"products/backlog\"", "start is a route"],
    ["actor: product-owner\n", "`actor` entry \"product-owner\"", "actor is a chapter reference"],
    ["flags: [new-board]\n", "`flags` entry \"new-board\"", "a flag is a chapter reference"],
    ["settings: [context.md#max]\n", "`settings` entry \"context.md#max\"", "a setting carries a value"],
    ["data: [Webshop Data]\n", "`data` entry \"Webshop Data\"", "a data set is a kebab-case name"],
]) {
    const issues = validateDocument(FILE, scenarios(`type: scenario\n${meta}`));
    check(has(errors(issues), needle), name, dump(issues));
}
for (const [relPath, markdown, where] of [
    [".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\n")}\n## Board\n\n${fence("type: feature\nactor: actors.md#po\n")}\nProse.\n`, "a domain chapter"],
    [".devbook/domain/work/features.md", `# Features\n\n${fence("type: features\nstart: /x\n")}\nProse.\n`, "another domain file"],
    [FILE, scenarios("type: scenario\n").replace("type: scenarios\n", "type: scenarios\nstart: /x\n"), "a scenarios file's own block"],
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
    const issues = validateDocument(FILE, scenarios("type: scenario\n", `${line}\n`));
    const stepErrors = errors(issues).filter((i) => i.message.includes("step"));
    check(ok ? stepErrors.length === 0 : stepErrors.length === 1, name, dump(issues));
}
{
    const md = scenarios("type: scenario\n").replace("A product owner", "- a lead list item\n\nA product owner").replace("The journeys of", "- a file lead item\n\nThe journeys of") + "\n```text\n- inside a fence\n```\n";
    const issues = validateDocument(FILE, md);
    check(errors(issues).length === 0, "list items in the leads and inside fences are not steps", dump(issues));
}

// ── DB13: a Proved by case, on the requirement's page ──────────────────────

const REQUIREMENTS = ".devbook/domain/work/requirements.md";
const requirements = (caseBody) =>
    `# Requirements\n\n${fence("type: requirements\n")}\n## Backlog board\n\n${fence("type: requirements\nrelated: [.devbook/domain/work/features.md#backlog-board]\n")}\n` +
    `### Requirement: Columns follow the status order\n\n${fence("type: requirement\n")}\nThe board SHALL show one column per status.\n\n#### Scenario: Statuses are set up\n${caseBody}`;
{
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: scenarios.md#statuses-are-set-up\n"));
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
    const issues = validateDocument(REQUIREMENTS, requirements("Proved by: scenarios\n"));
    check(has(errors(issues), "names one part of a journey"), "a Proved by line without a part is an error", dump(issues));
}

// ── DB6: screenshot images are no diagrams ─────────────────────────────────

{
    const md = "# T\n\nLead.\n\n![a](shot:x) ![b](scenario:work/scenarios.md#x)\n\n![c](diagram.svg)\n\n```mermaid\ngraph TD\n```\n";
    check(documentDigest(md).diagrams === 2, "shot: and scenario: images are not counted as diagrams", JSON.stringify(documentDigest(md)));
}

// ── Side fix: a `.devbook/` path in `tests` is named as a chapter reference ─

{
    const issues = testIssues({ tests: [".devbook/domain/work/domain.md#order"] });
    check(issues.length === 1 && issues[0].message.includes("which is a chapter reference"), "a .devbook/ path in tests is reported as a chapter reference", dump(issues));
}

check(CONTRACT_VERSION >= 32, "the contract is at least 32");

// ── The corpus-wide rules, through the graph build ─────────────────────────

const CONTEXT =
    `# Work\n\n${fence("type: context\n")}\nThe work context.\n\n## New Board\n\n${fence("type: feature-flag\nkey: new-board\n")}\nA flag.\n\n` +
    `## Backlog Max Columns\n\n${fence("type: setting\nkey: backlog-max-columns\n")}\nA setting.\n`;
const ACTORS = `# Actors\n\n${fence("type: actors\n")}\nWho works here.\n\n## Product Owner\n\n${fence("type: user\n")}\nOwns the backlog.\n`;
const FEATURES = (extra = "") =>
    `# Features\n\n${fence("type: features\n")}\nWhat it does.\n\n## Backlog Board\n\n${fence("type: feature\n")}\nThe board.\n${extra}`;
const SPEC = "e2e/tests/work/scenarios/set-up-and-fill-the-backlog.spec.ts";
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
const base = (journeyMeta, extra = {}) => ({
    ".devbook/domain/work/context.md": CONTEXT,
    ".devbook/domain/work/actors.md": ACTORS,
    ".devbook/domain/work/features.md": FEATURES(),
    [FILE]: scenarios(journeyMeta),
    ...extra,
});
const problemsAbout = (graph, needle) => graph.problems.filter((p) => p.message.includes(needle));

{
    const graph = await graphOf(base("type: scenario\nactor: actors.md#product-owner\nflags: [context.md#new-board, -context.md#new-board]\nsettings: [context.md#backlog-max-columns=5]\n"));
    const setup = graph.problems.filter((p) => /`(actor|flags|settings)` entry/.test(p.message));
    check(setup.length === 0, "actor, flags, and settings resolve from the bounded context", dump(setup));
    check(errors(graph.problems).length === 0, "a corpus with a journey has no errors", dump(errors(graph.problems)));
    check(graph.nodes.some((n) => n.id === JOURNEY && n.kind === "scenario"), "the journey is a chapter node of kind scenario");
    check(graph.nodes.some((n) => n.id === FILE && n.kind === "scenarios"), "the file is a node of kind scenarios");
}
{
    const graph = await graphOf(base("type: scenario\nactor: features.md#backlog-board\nflags: [context.md#backlog-max-columns]\nsettings: [context.md#nowhere=5]\n"));
    check(problemsAbout(graph, "`actor` entry \"features.md#backlog-board\" that resolves to a `feature` chapter").length === 1, "an actor resolving to a feature is an error", dump(graph.problems));
    check(problemsAbout(graph, "`flags` entry \"context.md#backlog-max-columns\" that resolves to a `setting` chapter").length === 1, "a flag resolving to a setting is an error", dump(graph.problems));
    // A switch modelled as a setting is set through setSetting, so the error says where it goes.
    check(problemsAbout(graph, "goes under `settings` as `context.md#backlog-max-columns=true`").length === 1, "a flag resolving to a setting names the settings entry it should be", dump(graph.problems));
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

// A journey's anchor is its own file's: two files may hold a journey of the same name.
{
    const graph = await graphOf(base("type: scenario\n", { ".devbook/domain/shop/scenarios.md": scenarios("type: scenario\n").replace("work context", "shop context") }));
    check(errors(graph.problems).length === 0, "the same journey name in two files is no conflict", dump(errors(graph.problems)));
}

// DB2: a journey and a part are addressable, so any number of chapters point at either.
{
    const graph = await graphOf(base("type: scenario\n", {
        ".devbook/domain/work/features.md": FEATURES().replace("type: feature\n", `type: feature\nrelated: [${JOURNEY}, ${FILE}#statuses-are-set-up]\n`),
    }));
    check(problemsAbout(graph, "does not resolve").length === 0, "related into a journey and into a part resolves", dump(graph.problems));
    check(graph.edges.some((e) => e.type === "related" && e.target === JOURNEY), "the journey is a related edge target");
    check(graph.edges.some((e) => e.type === "related" && e.target === `${FILE}#statuses-are-set-up`), "the part is a related edge target");
}

// DB6: a `scenario:` image names a scenarios file, and a label in it.
{
    const images =
        "\n![Board](scenario:work/scenarios.md#statuses-set-up)\n\n![Gone](scenario:work/nowhere.md#x)\n\n![Typo](scenario:.devbook/domain/work/scenarios.md#missing)\n\n![Bare](scenario:nope)\n";
    const graph = await graphOf(base("type: scenario\n", { ".devbook/domain/work/features.md": FEATURES(images) }));
    check(problemsAbout(graph, ".devbook/domain/work/nowhere.md is no scenarios file").filter((p) => p.severity === "error").length === 1, "a scenario: reference to no scenarios file is an error", dump(graph.problems));
    check(problemsAbout(graph, "has no screenshot point `shot:missing`").filter((p) => p.severity === "warning").length === 1, "an unknown label is a warning", dump(graph.problems));
    check(problemsAbout(graph, "a screenshot elsewhere is `scenario:<scenarios file>#<label>`").length === 1, "a reference without a label is an error", dump(graph.problems));
    check(problemsAbout(graph, "#statuses-set-up").length === 0, "a known file and label are accepted", dump(graph.problems));
}

// DB13: Proved by resolves to a part, derives the e2e entry, and claims the part.
const proved = (target, requirementsPath = REQUIREMENTS) => ({
    [requirementsPath]: requirements(`Proved by: ${target}\n`),
    [SPEC]: `// scenario: ${JOURNEY}\n// signature: 9c41e2a0\nimport { scenario } from "x";\n`,
});
{
    const graph = await graphOf(base("type: scenario\n", proved("scenarios.md#statuses-are-set-up")));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(problemsAbout(graph, "has `Proved by:").length === 0, "a Proved by pointer from the requirement's folder resolves", dump(graph.problems));
    check(
        JSON.stringify(requirement?.tests) === JSON.stringify([`e2e:playwright:${SPEC}#Statuses are set up`]),
        "the requirement's e2e tests entry is derived from the spec header",
        JSON.stringify(requirement)
    );
    check(problemsAbout(graph, "part \"#statuses-are-set-up\" of journey").length === 0, "a claimed part is not reported");
    check(problemsAbout(graph, "part \"#an-item-is-moved\" of journey \"#set-up-and-fill-the-backlog\" is unclaimed").filter((p) => p.severity === "warning").length === 1, "a part no requirement points at is unclaimed", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved(`${FILE}#statuses-are-set-up`)));
    check(problemsAbout(graph, "has `Proved by:").length === 0, "a Proved by pointer by repository path resolves", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", { [REQUIREMENTS]: requirements("Proved by: scenarios.md#statuses-are-set-up\n") }));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(requirement && requirement.tests === undefined, "without a spec naming the journey, nothing is derived", JSON.stringify(requirement));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("scenarios.md#no-such-part")));
    check(problemsAbout(graph, "has no part \"#no-such-part\"").filter((p) => p.severity === "error").length === 1, "a pointer to a missing part is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("scenarios.md#set-up-and-fill-the-backlog")));
    check(problemsAbout(graph, "which names the journey \"#set-up-and-fill-the-backlog\"").filter((p) => p.severity === "error").length === 1, "a pointer to a journey rather than a part is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("features.md#backlog-board")));
    check(problemsAbout(graph, "features.md is not a scenarios file").filter((p) => p.severity === "error").length === 1, "a pointer to a file that is no scenarios file is an error", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("../work/scenarios.md#statuses-are-set-up", ".devbook/domain/shop/requirements.md")));
    check(problemsAbout(graph, "has `Proved by:").length === 0, "a pointer into another context's scenarios file resolves", dump(graph.problems));
}

// An inline case in a context with journeys is reported, as info only.
const INLINE = "\n- **Given** a product with no statuses\n- **When** I create a status\n- **Then** a column appears\n";
{
    const graph = await graphOf(base("type: scenario\n", { [REQUIREMENTS]: requirements(INLINE) }));
    const candidates = problemsAbout(graph, "is written inline");
    check(
        candidates.length === 1 && candidates[0].severity === "info" && candidates[0].path === REQUIREMENTS && candidates[0].message.includes("`#### Scenario: Statuses are set up`"),
        "an inline case in a context with journeys is an info candidate",
        dump(graph.problems)
    );
}
{
    const files = base("type: scenario\n", { [REQUIREMENTS]: requirements(INLINE) });
    delete files[FILE];
    const graph = await graphOf(files);
    check(problemsAbout(graph, "is written inline").length === 0, "an inline case in a context with no journey is not reported", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", { [REQUIREMENTS.replace("/work/", "/shop/")]: requirements(INLINE) }));
    check(problemsAbout(graph, "is written inline").length === 0, "a journey in another context does not make a case a candidate", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", proved("scenarios.md#statuses-are-set-up")));
    check(problemsAbout(graph, "is written inline").length === 0, "a Proved by case is not a candidate", dump(graph.problems));
}

// A journey lives in the context holding most of the requirements it proves.
const SHOP_REQUIREMENTS = ".devbook/domain/shop/requirements.md";
const shopRequirements = (cases) =>
    `# Requirements\n\n${fence("type: requirements\n")}\n## Checkout\n\n${fence("type: requirements\n")}\n` +
    cases.map((part, i) => `### Requirement: Rule ${i + 1}\n\n${fence("type: requirement\n")}\nThe shop SHALL keep rule ${i + 1}.\n\n#### Scenario: Case ${i + 1}\nProved by: ../work/scenarios.md#${part}\n\n`).join("");
const MISPLACED = "a journey lives in the context holding most of the requirements it proves";
{
    const graph = await graphOf(base("type: scenario\n", proved("scenarios.md#statuses-are-set-up")));
    check(problemsAbout(graph, MISPLACED).length === 0, "a journey in the context of the requirements it proves is not reported", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", { [SHOP_REQUIREMENTS]: shopRequirements(["statuses-are-set-up"]) }));
    const found = problemsAbout(graph, MISPLACED);
    check(
        found.length === 1 && found[0].severity === "warning" && found[0].path === FILE && found[0].message.includes("1 of the 1 requirements it proves are in .devbook/domain/shop"),
        "a journey proving only another context's requirements is a warning naming that context",
        dump(graph.problems)
    );
}
{
    const graph = await graphOf(base("type: scenario\n", {
        ...proved("scenarios.md#statuses-are-set-up"),
        [SHOP_REQUIREMENTS]: shopRequirements(["statuses-are-set-up", "an-item-is-moved"]),
    }));
    check(problemsAbout(graph, "2 of the 3 requirements it proves are in .devbook/domain/shop").length === 1, "a journey across contexts belongs where most of its requirements are", dump(graph.problems));
}
{
    const graph = await graphOf(base("type: scenario\n", {
        ...proved("scenarios.md#statuses-are-set-up"),
        [SHOP_REQUIREMENTS]: shopRequirements(["an-item-is-moved"]),
    }));
    check(problemsAbout(graph, MISPLACED).length === 0, "a tie keeps the journey where it is", dump(graph.problems));
}

// ── Linked walkthroughs: a demo walkthrough sharing a part's journey ───────

const DEMO = ".devbook/domain/work/features.demo.html";
const SIGNATURE = signatureOf(parseScenariosFile(scenarios("type: scenario\n"), FILE).journeys[0]);
const demoHtml = (walkthroughs) =>
    `<!doctype html>\n<html><head><title>Work</title>\n<!-- template:begin -->\n<style>body{margin:0}</style>\n<!-- template:end -->\n` +
    `<script type="application/json" id="demo-meta">{"question":"Does the board read?"}</script>\n` +
    `<script type="application/json" id="demo-model">${JSON.stringify({
        screens: [{ id: "statuses", title: "Statuses", anchors: [] }, { id: "board", title: "Board", anchors: [] }],
        walkthroughs,
    })}</script>\n</head><body>\n<section id="statuses" data-screen></section>\n<section id="board" data-screen></section>\n</body></html>\n`;
const linkedWalkthrough = (over = {}) => ({
    id: "set-up-statuses",
    title: "Set up statuses",
    part: "scenarios.md#statuses-are-set-up",
    signature: SIGNATURE,
    steps: [
        { screen: "statuses", step: 1, text: "a product with no statuses" },
        { screen: "statuses", step: 2, text: 'When I create "New", "Doing" and "Done"' },
        { screen: "board", step: 3, text: "they appear as columns in that order" },
    ],
    ...over,
});
const linkedCorpus = (walkthroughs, extra = {}) =>
    base("type: scenario\n", { ...proved("scenarios.md#statuses-are-set-up"), [DEMO]: demoHtml(walkthroughs), ...extra });
const walkthroughProblems = (graph) => graph.problems.filter((p) => p.path === DEMO || /walkthrough/.test(p.message));
{
    const graph = await graphOf(linkedCorpus([linkedWalkthrough()]));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(walkthroughProblems(graph).length === 0, "a linked walkthrough at the journey's signature, its text the part's, is clean", dump(walkthroughProblems(graph)));
    check(
        JSON.stringify(requirement?.demo) === JSON.stringify([`${DEMO}#walkthrough/set-up-statuses`]),
        "the Proved by requirement's demo entry is derived from the walkthrough linked to its part",
        JSON.stringify(requirement)
    );
    check(requirement?.tests?.length === 1, "the derived demo entry sits beside the derived tests entry", JSON.stringify(requirement));
}
{
    const graph = await graphOf(linkedCorpus([linkedWalkthrough({ part: `${FILE}#an-item-is-moved` })]));
    const requirement = graph.nodes.find((n) => n.id === `${REQUIREMENTS}#requirement-columns-follow-the-status-order`);
    check(requirement && requirement.demo === undefined, "a walkthrough linked to another part derives nothing onto the requirement", JSON.stringify(requirement));
    const unresolved = walkthroughProblems(graph).filter((p) => /names no journey part/.test(p.message));
    check(unresolved.length === 0 && problemsAbout(graph, "drifted from part step").length === 3, "a part by repository path resolves and is checked step by step", dump(walkthroughProblems(graph)));
}
{
    const graph = await graphOf(linkedCorpus([linkedWalkthrough({ part: "scenarios.md#no-such-part" })]));
    check(problemsAbout(graph, 'has no part "#no-such-part"').some((p) => p.severity === "error" && p.path === DEMO), "a part the file does not have is an error", dump(graph.problems));
}
{
    const graph = await graphOf(linkedCorpus([linkedWalkthrough({ part: "nowhere.md#statuses-are-set-up" })]));
    check(problemsAbout(graph, ".devbook/domain/work/nowhere.md is not a scenarios file").some((p) => p.severity === "error" && p.path === DEMO), "a part in no scenarios file is an error", dump(graph.problems));
}
{
    const steps = [{ screen: "statuses", step: 1, text: "a product with no statuses" }, { screen: "board", step: 4, text: "they appear" }];
    const graph = await graphOf(linkedCorpus([linkedWalkthrough({ steps })]));
    check(problemsAbout(graph, "step 2 on part step 4, but").some((p) => p.severity === "error"), "a step past the part's last step is an error", dump(graph.problems));
}
{
    const graph = await graphOf(linkedCorpus([linkedWalkthrough({ signature: "00000000" })]));
    check(problemsAbout(graph, `at signature 00000000, but ${JOURNEY} is at ${SIGNATURE}`).some((p) => p.severity === "warning"), "a walkthrough at an older signature is stale", dump(graph.problems));
}
{
    const steps = [{ screen: "statuses", step: 1, text: "a product with one status" }];
    const fresh = await graphOf(linkedCorpus([linkedWalkthrough({ steps })]));
    check(problemsAbout(fresh, "drifted from part step 1").some((p) => p.severity === "error"), "embedded text unlike the part step, at the current signature, is an error", dump(fresh.problems));
    const stale = await graphOf(linkedCorpus([linkedWalkthrough({ steps, signature: "00000000" })]));
    check(problemsAbout(stale, "drifted from part step 1").some((p) => p.severity === "warning"), "drifted text under a stale signature is a warning", dump(stale.problems));
}
{
    // A hand-written walkthrough on a Proved by requirement is derived, so redundant.
    const free = { id: "statuses-are-set-up", title: "Free", steps: [{ screen: "statuses", text: "Statuses" }] };
    const graph = await graphOf(
        linkedCorpus([linkedWalkthrough(), free], {
            [REQUIREMENTS]: requirements("Proved by: scenarios.md#statuses-are-set-up\n").replace(
                "type: requirement\n",
                `type: requirement\ndemo: [${DEMO}#walkthrough/statuses-are-set-up]\n`
            ),
        })
    );
    const entry = problemsAbout(graph, "#walkthrough/statuses-are-set-up");
    check(entry.length === 1 && entry[0].severity === "warning" && entry[0].message.includes("only an inline case's is written by hand"), "a hand-written walkthrough on a Proved by requirement warns", dump(graph.problems));
}

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
