// Asserts a repository's own status ladder in `.devbook/statuses.json`: rules
// tried in order by glob and block level, the built-in ladder where nothing
// matches, the resting value and the decision rungs kept devbook's, a rating
// still required, and a configuration error reported once rather than per block.
//
// Run: `node statuses.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { validateDocument } from "./metadata.mjs";
import { compileStatusLadder, globToRegExp, loadStatusLadder, STATUSES_FILE } from "./statuses.mjs";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const dump = (value) => JSON.stringify(value, null, 2);
const errors = (issues) => issues.filter((i) => i.severity === "error");
const statusErrors = (issues) => errors(issues).filter((i) => i.message.includes("status"));

// Budgetbeheer's intended domain/ ladder, as the handoff that asked for this states it.
const config = {
    $schema: "https://example.test/spec-manager.schema.json",
    statuses: { review: { meaning: "Ready to be read.", tone: "attention" } },
    folders: {
        inbox: { path: ".inbox", rules: [{ files: ["**"], statuses: ["pending"] }] },
        domain: {
            path: ".devbook/domain",
            rules: [
                { id: "actors", files: ["**/actors.md"], scope: "file", statuses: ["draft", "review", "ready", "changed"], default: "draft" },
                { id: "actor-chapters", files: ["**/actors.md"], scope: "chapter", statuses: [] },
                { id: "model", files: ["**/domain.md", "**/domain.*.md"], scope: "any", statuses: ["draft", "review", "ready", "changed", "deprecated"] },
                { id: "capabilities", files: ["**/features*.md", "**/flow*.md"], statuses: ["planned", "draft", "review", "ready", "changed", "deprecated"] },
            ],
        },
    },
};
const { ladder, issues: configIssues } = compileStatusLadder(config);
check(configIssues.length === 0, "a well-formed file reports nothing, and keys devbook does not read are left alone", dump(configIssues));

const domainPage = (fileMeta, chapterMeta) =>
    `# Domain\n\n${fence("type: domain\n" + fileMeta)}\n## Order\n\n${fence("type: aggregate\n" + chapterMeta)}\nProse.\n`;
const at = ".devbook/domain/ordering/domain.md";

// --- A configured rung validates, and one the rule does not list does not ---

{
    const issues = validateDocument(at, domainPage("", "status: review\n"), { ladder });
    check(statusErrors(issues).length === 0, "`review` on a domain chapter validates where the rule lists it", dump(issues));
}
{
    const issues = validateDocument(at, domainPage("", "status: review\n"));
    check(statusErrors(issues).length === 1, "without the file, `review` is still rejected by the built-in ladder", dump(issues));
}
{
    const issues = validateDocument(at, domainPage("", "status: proposed\n"), { ladder });
    const e = statusErrors(issues)[0];
    check(
        e?.message.includes('"proposed"') && e.message.includes(STATUSES_FILE) && e.message.includes('("model")'),
        "a rung the matching rule does not list is an error that names the rule",
        dump(issues)
    );
}

// --- The resting value and the decision rungs stay devbook's --------------

{
    const issues = validateDocument(at, domainPage("", ""), { ladder });
    check(errors(issues).length === 0, "an omitted status still means the resting value", dump(issues));
}
{
    const issues = validateDocument(at, domainPage("", "status: active\n"), { ladder });
    check(
        errors(issues).length === 0 && issues.some((i) => i.severity === "warning" && i.message.includes("resting value")),
        "`status: active` written out is the usual warning, never a ladder error",
        dump(issues)
    );
}
{
    const approved = "status: approved\napproved-by: Ada\napproved-at: 2026-09-28\n";
    const issues = validateDocument(at, domainPage("", approved), { ladder });
    check(statusErrors(issues).length === 0, "`approved` is valid on a domain/ rule that does not list it", dump(issues));
}

// --- Order, glob, and block level decide which rule answers ----------------

{
    const actors = `# Actors\n\n${fence("type: actors\nstatus: review\n")}\n## Case Worker\n\n${fence("type: user\nstatus: draft\n")}\nProse.\n`;
    const issues = validateDocument(".devbook/domain/ordering/actors.md", actors, { ladder });
    const e = statusErrors(issues);
    check(
        e.length === 1 && e[0].message.includes("Case Worker") && e[0].message.includes("expected one of: approved, accepted"),
        "a `file` rule answers the file block and an empty `chapter` rule leaves a chapter only the decision rungs",
        dump(issues)
    );
}
{
    const split = validateDocument(".devbook/domain/ordering/features.checkout.md", `# Checkout\n\n${fence("type: features\nstatus: planned\n")}\n`, { ladder });
    check(statusErrors(split).length === 0, "`**/features*.md` matches a split file", dump(split));
}
{
    const unmatched = validateDocument(".devbook/domain/ordering/requirements.md", `# Requirements\n\n${fence("type: requirements\nstatus: review\n")}\n`, { ladder });
    check(statusErrors(unmatched).length === 1, "a file no rule matches takes the built-in ladder", dump(unmatched));
    const arc42 = validateDocument(".devbook/arc42/01-introduction.md", `# 01. Introduction\n\n${fence("status: review\n")}\n`, { ladder });
    check(statusErrors(arc42).length === 1, "a folder the file does not name takes the built-in ladder", dump(arc42));
}
{
    check(globToRegExp("**/actors.md").test("actors.md"), "`**/` also matches no directory");
    check(globToRegExp("*.md").test("a.md") && !globToRegExp("*.md").test("x/a.md"), "`*` stays inside one segment");
}
{
    const { ladder: arcLadder } = compileStatusLadder({ folders: { arc42: { rules: [{ files: ["adr/**"], statuses: [] }] } } });
    const issues = validateDocument(".devbook/arc42/adr/hosts.md", `# Hosts\n\n${fence("status: draft\n")}\n`, { ladder: arcLadder });
    check(
        statusErrors(issues).some((i) => i.message.includes("no status is written on this block")),
        "an empty list outside domain/ means the block carries no status",
        dump(issues)
    );
}

// --- Configuration errors: once, in the file, never per block --------------

{
    const { ladder: bad, issues } = compileStatusLadder({
        folders: {
            domain: { rules: [{ id: "decisions", files: ["**/_decisions*.md"], scope: "chapter", statuses: ["proposed", "active", "approved", "deprecated"] }] },
            design: { rules: [{ files: ["**"], statuses: ["draft", "accepted"] }] },
            tech: { rules: [{ files: ["**"], statuses: ["adopted", "review"] }, { files: ["x.md"], statuses: [] }] },
            arc42: { rules: [{ files: ["**"], scope: "section", statuses: ["draft"] }, { statuses: ["draft"] }] },
            ai: "nope",
        },
    });
    const has = (needle) => issues.some((i) => i.message.includes(needle));
    check(has("lists `active`, the resting value"), "listing the resting value is a configuration error", dump(issues));
    check(has("lists `approved`, which devbook adds"), "listing a decision rung in domain/ is a configuration error", dump(issues));
    check(has("lists `accepted`, a decision rung, which only domain/"), "a decision rung outside domain/ is a configuration error", dump(issues));
    check(has("lists `review`, which is not on the tech/ rating ladder"), "a rating rule may only narrow its ladder", dump(issues));
    check(has("is empty, and `status` is required in tech/"), "an empty rating rule is a configuration error", dump(issues));
    check(has('has scope "section"') && has("needs `files`"), "a malformed rule is reported and skipped", dump(issues));
    check(has("`folders.ai` has no `rules` list"), "a folder entry without rules is reported", dump(issues));
    check(issues.every((i) => i.path === STATUSES_FILE && i.severity === "error"), "every configuration issue is an error on the file itself", dump(issues));

    const chapter = `# Decisions\n\n${fence("type: decisions\n")}\n## Split\n\n${fence("status: proposed\n")}\nProse.\n`;
    const kept = validateDocument(".devbook/domain/ordering/_decisions.md", chapter, { ladder: bad });
    check(statusErrors(kept).length === 0, "an offending value is dropped and the rest of its rule stands", dump(kept));
}

// --- Loading from a repository, and the graph build reporting once ---------

const root = await mkdtemp(path.join(tmpdir(), "devbook-statuses-"));
try {
    check((await loadStatusLadder(root)).ladder === null, "no file is the built-in ladder, with nothing reported");

    await mkdir(path.join(root, ".devbook", "domain", "ordering"), { recursive: true });
    await writeFile(path.join(root, STATUSES_FILE), "{ not json");
    const broken = await loadStatusLadder(root);
    check(broken.ladder === null && broken.issues[0]?.message.includes("is not valid JSON"), "unparseable JSON is one error and the built-in ladder", dump(broken.issues));

    const withActive = structuredClone(config);
    withActive.folders.domain.rules[2].statuses.push("active");
    await writeFile(path.join(root, STATUSES_FILE), "﻿" + JSON.stringify(withActive));
    await writeFile(path.join(root, ".devbook", "domain", "context-map.md"), `# Shop\n\n${fence("type: context-map\n")}\n`);
    await writeFile(path.join(root, ".devbook", "domain", "ordering", "context.md"), `# Ordering\n\n${fence("type: context\n")}\n`);
    await writeFile(path.join(root, ".devbook", "domain", "ordering", "domain.md"), domainPage("status: review\n", "status: ready\n"));
    await writeFile(path.join(root, ".devbook", "domain", "ordering", "domain.billing.md"), domainPage("status: changed\n", "status: review\n"));
    const { problems } = await buildGraph(root);
    const fromFile = problems.filter((p) => p.path === STATUSES_FILE);
    check(fromFile.length === 1 && fromFile[0].message.includes("lists `active`"), "the build reports a configuration error once", dump(problems));
    check(
        !problems.some((p) => p.severity === "error" && p.path !== STATUSES_FILE && p.message.includes("status")),
        "the build validates every block against the repository's ladder",
        dump(problems)
    );
} finally {
    await rm(root, { recursive: true, force: true });
}

if (failed) {
    console.error(`\n${failed} check(s) failed.`);
    process.exit(1);
}
console.log("\nAll checks passed.");
