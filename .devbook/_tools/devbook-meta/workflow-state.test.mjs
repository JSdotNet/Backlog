// workflow-state.test.mjs — contract 38: workflow state leaves the chapter.
//
// A branch's devbook is true of its code, so a merged chapter is in force and
// carries no state. `status` outside `tech/` and `ai/`, the two decision rungs'
// six record fields, `sync`, `issue`, `effort`, `roadmap`, and `change` are
// errors naming `038-workflow-state-leaves-the-chapter`; a `tech/` or `ai/`
// rating is `rating`, always stated, on the adoption ladder. The tool that runs
// a workflow keeps the rest, and `delta.mjs --apply` merges a change only over
// the fingerprint its acceptance was recorded at.
//
// The load-bearing survivor of the old `status` rule is the empty `meta` fence:
// `.arc42` and `.design` define no `type`, so a chapter with no relations has
// nothing to write, and the fence alone marks it as a node.
//
// Run: `node workflow-state.test.mjs`
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { behaviourIssues, parseDocument, validateDocument, CHANGES_ROOT } from "./metadata.mjs";
import { buildGraph, buildGraphDocument } from "./graph.mjs";
import { buildOutlineDocument } from "./outline.mjs";
import { applyChange, changeFingerprint } from "./delta.mjs";
import { listUnits } from "./units.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const MIGRATION = "038-workflow-state-leaves-the-chapter";
const fence = (body) => "```meta\n" + body + "```\n";
const errors = (issues) => issues.filter((i) => i.severity === "error");
const warnings = (issues) => issues.filter((i) => i.severity === "warning");
const dump = (value) => JSON.stringify(value, null, 2);
const moved = (issues, field) => errors(issues).filter((i) => i.message.includes(`has \`${field}\``) && i.message.includes(MIGRATION));

const domainChapter = (meta) => `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n" + meta)}\nProse.\n`;
const journeyFile = (meta) =>
    `# Scenarios\n\n${fence("type: scenarios\n")}\nLead.\n\n## Set up\n\n${fence("type: scenario\n" + meta)}\nLead.\n\n### Done\n\n- **Then** it is done\n`;

// ── status is moved to tooling outside tech/ and ai/ ───────────────────────

for (const [where, relPath, markdown] of [
    ["an arc42/ chapter", ".devbook/arc42/03-context.md", `# 03. Context\n\n${fence("status: draft\n")}\nProse.\n`],
    ["a domain/ chapter", ".devbook/domain/ordering/domain.md", domainChapter("status: proposed\n")],
    ["a design/ chapter", ".devbook/design/color-scheme.md", `# Color Scheme\n\n${fence("status: deprecated\n")}\nProse.\n`],
    ["a debt record", ".devbook/arc42/tdr/1-no-retry.md", `# 1. No Retry\n\n${fence("severity: low\nstatus: planned\n")}\nProse.\n`],
    ["a decision record", ".devbook/arc42/adr/storage.md", `# Storage\n\n${fence("status: proposed\n")}\nProse.\n`],
    ["a journey", ".devbook/domain/work/scenarios.md", journeyFile("status: draft\n")],
    ["a change's proposal.md", `${CHANGES_ROOT}/add-cache/proposal.md`, `# Add a cache\n\n${fence("type: change\ncategory: feature\nstatus: proposed\n")}\nWhy.\n`],
]) {
    const issues = validateDocument(relPath, markdown);
    check(
        moved(issues, "status").length === 1 && errors(issues).length === 1 && moved(issues, "status")[0].message.includes("moved to tooling"),
        `\`status\` on ${where} is one error, moved to tooling, naming the migration`,
        dump(issues)
    );
}

// ── the decision rungs' six record fields ──────────────────────────────────

for (const field of ["approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash"]) {
    const issues = validateDocument(".devbook/domain/ordering/domain.md", domainChapter(`${field}: x\n`));
    check(moved(issues, field).length === 1 && errors(issues).length === 1, `\`${field}\` is moved to tooling`, dump(issues));
}

// ── sync, issue, effort, roadmap, change ───────────────────────────────────

for (const [field, value] of [["sync", "pull"], ["issue", "owner/repo#12"], ["effort", "3"], ["roadmap", "[mobile-mvp]"], ["change", "add-cache"]]) {
    for (const [relPath, markdown] of [
        [".devbook/arc42/03-context.md", `# 03. Context\n\n${fence(`${field}: ${value}\n`)}\nProse.\n`],
        [".devbook/tech/backend.md", `# Backend\n\n${fence(`rating: adopted\n${field}: ${value}\n`)}\nProse.\n`],
    ]) {
        const issues = validateDocument(relPath, markdown);
        check(moved(issues, field).length === 1 && errors(issues).length === 1, `\`${field}\` in ${relPath} is one error naming the migration`, dump(issues));
    }
}

// ── the rating ─────────────────────────────────────────────────────────────

{
    const issues = validateDocument(".devbook/tech/backend.md", `# Backend\n\n${fence("status: adopted\n")}\nProse.\n`);
    check(
        errors(issues).length === 1 && issues[0].message.includes("is the `rating` field") && issues[0].message.includes(MIGRATION),
        "`status` in tech/ is reported once, as renamed `rating`",
        dump(issues)
    );
    check(!issues.some((i) => i.message.includes("missing required `rating`")), "a renamed rating is not also reported missing", dump(issues));
}
{
    const issues = validateDocument(".devbook/ai/02-build.md", `# 02. Build\n\n${fence("rating: trial\ntype: stage\n")}\n## Agent-Driven TDD\n\n${fence("status: trial\ntype: practice\nstage: verify\n")}\nProse.\n\n- **Moves up when** — a second person works this way.\n`);
    check(errors(issues).length === 1 && issues[0].message.includes("is the `rating` field"), "`status` in ai/ is reported as renamed `rating`", dump(issues));
}
for (const [relPath, markdown] of [
    [".devbook/tech/backend.md", `# Backend\n\n${fence("rating: adopted\n")}\n## PostgreSQL\n\n${fence("type: service\n")}\nProse.\n`],
    [".devbook/ai/02-build.md", `# 02. Build\n\n${fence("rating: trial\ntype: stage\n")}\n## Agent-Driven TDD\n\n${fence("type: practice\nstage: verify\n")}\nProse.\n`],
]) {
    const issues = validateDocument(relPath, markdown);
    check(errors(issues).length === 1 && issues.some((i) => i.message.includes("missing required `rating`")), `a chapter in ${relPath} without a rating is an error`, dump(issues));
}
{
    const issues = validateDocument(".devbook/tech/backend.md", `# Backend\n\n${fence("rating: adopted\n")}\n## PostgreSQL\n\n${fence("rating: active\ntype: service\n")}\nProse.\n`);
    check(errors(issues).length === 1 && issues.some((i) => i.message.includes('has `rating` "active", expected one of: candidate, trial, adopted, hold, retired')), "a rating off the adoption ladder is an error", dump(issues));
}
{
    const issues = validateDocument(".devbook/tech/backend.md", `# Backend\n\n${fence("rating: adopted\n")}\n## PostgreSQL\n\n${fence("rating: adopted\ntype: service\n")}\nProse.\n`);
    check(issues.length === 0, "a rating stated on every tech/ block is silent", dump(issues));
}
for (const relPath of [".devbook/arc42/03-context.md", ".devbook/design/color-scheme.md"]) {
    const issues = validateDocument(relPath, `# Title\n\n${fence("rating: adopted\n")}\nProse.\n`);
    check(errors(issues).length === 0 && warnings(issues).some((i) => i.message.includes("unrecognized field `rating`")), `\`rating\` in ${relPath} is an unrecognized field`, dump(issues));
}

// ── a chapter with nothing to say ──────────────────────────────────────────

for (const [relPath, markdown] of [
    [".devbook/arc42/03-context.md", `# 03. Context\n\n${fence("")}\nProse.\n\n## Interfaces\n\n${fence("")}\nProse.\n`],
    [".devbook/design/color-scheme.md", `# Color Scheme\n\n${fence("")}\nProse.\n\n## Tokens\n\n${fence("")}\nProse.\n`],
    [".devbook/domain/ordering/domain.md", domainChapter("")],
    [".devbook/domain/work/scenarios.md", journeyFile("")],
]) {
    const issues = validateDocument(relPath, markdown);
    check(issues.length === 0, `${relPath} with no state is clean`, dump(issues));
}
{
    const parsed = parseDocument(`# Title\n\n${fence("")}\n## Empty\n\n${fence("")}\nprose\n`);
    check(
        parsed.chapters.length === 2 && parsed.chapters.every((c) => c.meta && Object.keys(c.meta).length === 0),
        "an empty meta fence parses to {}, not to null",
        dump(parsed.chapters)
    );
}

// ── a withdrawn promise is removed, not exempted ───────────────────────────

{
    const issues = behaviourIssues("requirement", { status: "deprecated" }, 0);
    check(issues.length === 1 && issues[0].severity === "warning" && issues[0].message.includes("no `#### Scenario:`"), "a requirement with no scenario warns, whatever its old status said", dump(issues));
}

// ── the derived artifacts ──────────────────────────────────────────────────

async function repo(files) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-workflow-state-"));
    for (const [rel, text] of Object.entries(files)) {
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text, "utf8");
    }
    return root;
}

{
    const root = await repo({
        ".devbook/statuses.json": JSON.stringify({ folders: { domain: { rules: [{ files: ["**"], statuses: ["draft", "review"] }] } } }),
        ".devbook/design/README.md": `# Design\n\n${fence("")}\nProse.\n\n## Principles\n\n${fence("")}\nProse.\n`,
        ".devbook/domain/ordering/domain.md": domainChapter(""),
        ".devbook/tech/technology-graph.md": `# Technology Graph\n\n${fence("rating: adopted\n")}\nProse.\n\n## Postgres\n\n${fence("rating: trial\ntype: service\n")}\nA database.\n`,
    });
    try {
        const graph = await buildGraph(root);
        const ladders = graph.problems.filter((p) => p.message.includes("statuses.json"));
        check(ladders.length === 1 && ladders[0].severity === "warning" && ladders[0].message.includes("no longer read"), "a .devbook/statuses.json left behind is one warning", dump(graph.problems));
        check(!graph.problems.some((p) => p.severity === "error"), "the corpus builds with no error", dump(graph.problems));

        const byId = new Map(graph.nodes.map((n) => [n.id, n]));
        check(byId.get(".devbook/tech/technology-graph.md")?.rating === "adopted", "a tech/ file node carries its rating", dump(byId.get(".devbook/tech/technology-graph.md")));
        check(byId.get(".devbook/tech/technology-graph.md#postgres")?.rating === "trial", "a tech/ chapter node carries its rating");
        check(graph.nodes.every((n) => !("status" in n) && !("statusDeclared" in n)), "no node carries `status` or `statusDeclared`", dump(graph.nodes.filter((n) => "status" in n || "statusDeclared" in n)));
        check(byId.has(".devbook/design/README.md#principles"), "a chapter whose meta block is empty is still a node", dump([...byId.keys()]));
        check(!("rating" in byId.get(".devbook/domain/ordering/domain.md#order")), "a domain/ node carries no rating");

        const doc = await buildGraphDocument(root, ".", graph);
        check(dump(doc.stats.nodesByRating) === dump({ adopted: 1, trial: 1 }) && !("nodesByStatus" in doc.stats), "graph.json stats count `nodesByRating`, rated nodes only", dump(doc.stats));
        check(doc.schemaVersion === 38, "graph.json stamps contract 38", String(doc.schemaVersion));

        const tech = (await buildOutlineDocument(root, ".devbook/tech")).entries.find((e) => e.path === ".devbook/tech/technology-graph.md");
        check(tech?.rating === "adopted" && !("status" in tech), "index.json lists a tech/ file's rating", dump(tech));
        const design = (await buildOutlineDocument(root, ".devbook/design")).entries.find((e) => e.path === ".devbook/design/README.md");
        check(design && !("status" in design) && !("statusDeclared" in design) && !("rating" in design), "index.json lists a design/ file with no state", dump(design));

        const units = await listUnits(root, { groups: true });
        check(units.version === 2 && !("direction" in units), "units.mjs prints version 2, with no direction", dump(Object.keys(units)));
        check(
            units.groups.length === 1 && !("direction" in units.groups[0]) && units.groups[0].units.every((u) => !("sync" in u) && !("syncFrom" in u) && !("syncLevel" in u)),
            "no group or unit carries a direction",
            dump(units.groups)
        );
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

// ── delta.mjs --apply ───────────────────────────────────────────────────────

{
    const DECISIONS = ".devbook/arc42/09-decisions.md";
    const DELTA = `${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`;
    const decisions = `# Decisions\n\n${fence("")}\n## Caching\n\n${fence("")}\nUse a cache.\n`;
    const files = {
        [DECISIONS]: decisions,
        [`${CHANGES_ROOT}/add-cache/proposal.md`]: `# Add a cache\n\n${fence("type: change\ncategory: feature\n")}\n## Why\n\nReads are slow.\n`,
        [DELTA]: fence("change: add-cache\ndelta: modified\n") + `\n## Caching\n\n### ADDED\n\n#### Invalidation\n\n${fence("")}\nWrites evict the key.\n`,
    };
    const attempt = async (acceptedHashOf, mutate = async () => {}) => {
        const root = await repo(files);
        try {
            const acceptedHash = await acceptedHashOf(root);
            await mutate(root);
            const result = await applyChange(root, "add-cache", { date: "2026-10-29", move: false, acceptedHash });
            return { result, merged: await readFile(path.join(root, DECISIONS), "utf8") };
        } finally {
            await rm(root, { recursive: true, force: true });
        }
    };
    const messages = (result) => errors(result.report.problems).map((i) => i.message);

    const none = await attempt(async () => null);
    check(!none.result.applied && messages(none.result).some((m) => m.includes("has no acceptance passed in")) && none.merged === decisions, "--apply refuses a change with no acceptance passed in", dump(messages(none.result)));

    const stale = await attempt(
        (root) => changeFingerprint(root, "add-cache"),
        (root) => writeFile(path.join(root, DELTA), files[DELTA].replace("evict", "drop"), "utf8")
    );
    check(!stale.result.applied && messages(stale.result).some((m) => m.includes("changed after the decision")) && stale.merged === decisions, "--apply refuses an acceptance recorded at an older fingerprint", dump(messages(stale.result)));

    const accepted = await attempt((root) => changeFingerprint(root, "add-cache"));
    check(accepted.result.applied && accepted.merged.includes("### Invalidation"), "--apply merges a change accepted over its current fingerprint", dump(messages(accepted.result)));
    check(!accepted.merged.includes("change:"), "the merge stamps no `change` on the chapters it touched", accepted.merged);
}
{
    const issues = validateDocument(`${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`, fence("change: Add_Cache\ndelta: modified\n") + "\n## Caching\n");
    check(errors(issues).some((i) => i.message.includes("lowercase kebab-case")), "a delta header's `change` that is not one kebab-case name is an error", dump(issues));
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
