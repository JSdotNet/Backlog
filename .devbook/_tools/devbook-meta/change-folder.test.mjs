// Exercises the change folder: `openspec/changes/<name>/` as a folder kind the
// graph build indexes, `proposal.md` as a `type: change` file, and the delta
// merge in delta.mjs — `--check` resolving every delta to a target file and
// heading, `--apply` merging heading by heading one level up, stamping
// `change` on every chapter it touched, and moving the folder to `archive/`.
//
// One fixture change is written to a temporary repository and run through
// both, then broken one way at a time.
//
// Run: `node change-folder.test.mjs`
import { mkdtemp, mkdir, writeFile, readFile, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { changeHash, changePathParts, folderKindForPath, validateDocument, CHANGES_ROOT } from "./metadata.mjs";
import { buildGraph, discoverLayout } from "./graph.mjs";
import { applyChange, changeFingerprint, checkChange, mergeDelta, parseDelta } from "./delta.mjs";
import { main as hashMain } from "./chapter-hash.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const errorsOf = (issues) => issues.filter((i) => i.severity === "error");
const fence = (body) => "```meta\n" + body + "```\n";
const exists = async (p) => stat(p).then(() => true, () => false);

// -- Paths ------------------------------------------------------------------

check(folderKindForPath(`${CHANGES_ROOT}/add-cache/proposal.md`) === "changes", "a proposal is in the change folder");
check(
    changePathParts(`${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`)?.target === ".devbook/arc42/09-decisions.md",
    "a delta's target is its path under devbook-delta/, re-rooted at .devbook/"
);
check(folderKindForPath(`${CHANGES_ROOT}/archive/2026-09-01-old/proposal.md`) === null, "archive/ is outside every folder kind");
check(folderKindForPath(".devbook/.changes/x/proposal.md") === null, "a dotted folder under .devbook/ is not the change folder");

// -- The fixture ------------------------------------------------------------

const DECISIONS = ".devbook/arc42/09-decisions.md";
const decisions =
    `# Decisions\n\n${fence("")}\n` +
    `## Caching\n\n${fence("status: proposed\n")}\nUse a cache.\n\n` +
    `### Consequences\n\n${fence("")}\nReads get faster.\n\n` +
    `### Open questions\n\n${fence("")}\nWhich cache?\n\n` +
    `## Logging\n\n${fence("")}\nLog everything.\n`;

const proposal =
    `# Add a cache\n\n${fence("type: change\nstatus: proposed\ncategory: feature\n")}\n` +
    `## Why\n\nReads are slow.\n\n## Scope\n\nThe read path.\n\n## Chapters touched\n\n- ${DECISIONS}#caching\n`;

const modified =
    fence("change: add-cache\ndelta: modified\n") +
    `\n## Caching\n\n### MODIFIED\n\n${fence(`related: ["${DECISIONS}#logging"]\n`)}\n` +
    `#### Consequences\n\n${fence("")}\nReads get faster; writes invalidate.\n\n` +
    "```text\n# not a heading\n```\n\n" +
    `### ADDED\n\n#### Invalidation\n\n${fence("")}\nWrites evict the key.\n\n` +
    `### REMOVED\n\n#### Open questions\n`;

const added =
    fence("change: add-cache\ndelta: added\n") +
    `\n# Quality\n\n${fence("")}\nHow good it has to be.\n\n## ADDED\n\n### Latency\n\n${fence("")}\nA read answers in 50 ms.\n`;

async function fixture(extra = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-change-"));
    const files = {
        [DECISIONS]: decisions,
        [`${CHANGES_ROOT}/add-cache/proposal.md`]: proposal,
        [`${CHANGES_ROOT}/add-cache/solution.md`]: "# Solution\n\nLoad the ADR on storage.\n",
        [`${CHANGES_ROOT}/add-cache/tasks.md`]: "# Tasks\n\n## Step 1 — Cache reads\n\ndelivers: devbook-delta/arc42/09-decisions.md#caching\n",
        [`${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`]: modified,
        [`${CHANGES_ROOT}/add-cache/devbook-delta/arc42/10-quality.md`]: added,
        [`${CHANGES_ROOT}/archive/2026-09-01-old/proposal.md`]: "# broken, and never read\n",
        ...extra,
    };
    for (const [rel, text] of Object.entries(files)) {
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text, "utf8");
    }
    return root;
}

// Both gates, passed: the proposal signed at `accepted` over the fingerprint
// of the change as it stands. `edit` changes the record before it is written.
const PROPOSAL = `${CHANGES_ROOT}/add-cache/proposal.md`;
async function decide(root, edit = (block) => block) {
    const hash = await changeFingerprint(root, "add-cache");
    const at = path.join(root, PROPOSAL);
    const text = await readFile(at, "utf8");
    const block =
        `status: accepted\napproved-by: @amy\napproved-at: 2026-09-20\napproved-hash: ${hash}\n` +
        `accepted-by: @sam\naccepted-at: 2026-09-27\naccepted-hash: ${hash}\n`;
    await writeFile(at, text.replace("status: proposed\n", edit(block)), "utf8");
    return hash;
}

// -- The generator indexes it ------------------------------------------------

{
    const root = await fixture();
    const layout = await discoverLayout(root);
    check(layout.changes === CHANGES_ROOT, "the change folder is discovered by existing");
    const graph = await buildGraph(root);
    const ids = new Set(graph.nodes.map((n) => n.id));
    const proposalNode = graph.nodes.find((n) => n.id === `${CHANGES_ROOT}/add-cache/proposal.md`);
    check(proposalNode?.kind === "change" && proposalNode.folder === "changes" && proposalNode.category === "feature", "the proposal is indexed as a `change`", JSON.stringify(proposalNode));
    const deltaNode = graph.nodes.find((n) => n.id === `${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`);
    check(deltaNode?.delta === "modified" && deltaNode.target === DECISIONS && deltaNode.change === "add-cache", "a delta is indexed with its kind and target", JSON.stringify(deltaNode));
    check(![...ids].some((id) => id.includes("solution.md") || id.includes("tasks.md")), "solution.md and tasks.md are not indexed");
    check(![...ids].some((id) => id.includes("/archive/")), "archive/ is never indexed");
    check(errorsOf(graph.problems).length === 0, "the fixture change indexes with no error", JSON.stringify(errorsOf(graph.problems)));
    await rm(root, { recursive: true, force: true });
}

// -- --apply --no-move merges and leaves the folder for the caller to move ---

{
    const root = await fixture();
    await decide(root);
    const result = await applyChange(root, "add-cache", { date: "2026-09-28", move: false });
    check(result.applied && (await readFile(path.join(root, DECISIONS), "utf8")).includes("writes invalidate"), "--no-move still merges every delta");
    check(await exists(path.join(root, CHANGES_ROOT, "add-cache/proposal.md")), "--no-move leaves the change folder where it is");
    check(!(await exists(path.join(root, CHANGES_ROOT, "archive/2026-09-28-add-cache"))), "--no-move writes nothing under archive/");
    await rm(root, { recursive: true, force: true });
}

// -- --check and --apply -----------------------------------------------------

{
    const root = await fixture();
    const report = await checkChange(root, "add-cache");
    const all = [...report.problems, ...report.deltas.flatMap((d) => d.issues)];
    check(report.deltas.length === 2 && errorsOf(all).length === 0, "--check resolves every delta of the fixture", JSON.stringify(errorsOf(all)));

    await decide(root);
    const result = await applyChange(root, "add-cache", { date: "2026-09-28" });
    check(result.applied, "--apply merges the fixture", JSON.stringify(errorsOf([...result.report.problems, ...result.report.deltas.flatMap((d) => d.issues)])));
    const merged = await readFile(path.join(root, DECISIONS), "utf8");
    check(merged.includes("### Consequences") && merged.includes("writes invalidate") && !merged.includes("Reads get faster.\n"), "a MODIFIED entry replaces its section whole, one level up");
    check(merged.includes("```text\n# not a heading\n```"), "a `#` line inside fenced code is carried, not raised");
    check(/### Invalidation\n\n```meta\nchange: add-cache\n```/.test(merged), "an ADDED entry lands one level up, stamped with its change", merged);
    check(!merged.includes("Open questions"), "a REMOVED entry is gone, by heading");
    check(/## Caching\n\n```meta\nstatus: proposed\nrelated: \[".devbook\/arc42\/09-decisions.md#logging"\]\nchange: add-cache\n```/.test(merged), "MODIFIED fields are set, others kept, and the chapter carries `change`", merged);
    check(merged.indexOf("### Invalidation") < merged.indexOf("## Logging"), "an ADDED entry lands inside its chapter, before the next one");
    check(/## Logging\n\n```meta\n```/.test(merged), "an untouched chapter is not stamped");
    check(errorsOf(validateDocument(DECISIONS, merged)).length === 0, "the merged chapter validates");

    const quality = await readFile(path.join(root, ".devbook/arc42/10-quality.md"), "utf8");
    check(quality.startsWith("# Quality\n\n```meta\nchange: add-cache\n```") && quality.includes("## Latency"), "an added file is created, its entries one level up", quality);

    check(await exists(path.join(root, CHANGES_ROOT, "archive/2026-09-28-add-cache/proposal.md")), "the change folder moves to archive/<date>-<name>/");
    check(!(await exists(path.join(root, CHANGES_ROOT, "add-cache"))), "nothing of the change is left open");
    const after = await buildGraph(root);
    check(errorsOf(after.problems).length === 0, "the repository indexes clean after the merge", JSON.stringify(errorsOf(after.problems)));
    await rm(root, { recursive: true, force: true });
}

// -- The gates -----------------------------------------------------------------

const QUALITY_DELTA = `${CHANGES_ROOT}/add-cache/devbook-delta/arc42/10-quality.md`;
const editFile = async (root, rel, change) => {
    const at = path.join(root, rel);
    await writeFile(at, change(await readFile(at, "utf8")), "utf8");
};
const note = (kind) => `\n\`\`\`annotation\nkind: ${kind}\nauthor: amy\ndate: 2026-09-21\nbody: Why 50?\n\`\`\`\n`;

const refusedBy = async (edit, why, pattern, mutate = async () => {}) => {
    const root = await fixture();
    if (edit) await decide(root, edit);
    await mutate(root);
    const result = await applyChange(root, "add-cache", { date: "2026-09-28" });
    const messages = errorsOf(result.report.problems).map((i) => i.message);
    check(
        !result.applied && messages.some((m) => pattern.test(m)) && (await readFile(path.join(root, DECISIONS), "utf8")) === decisions,
        `--apply refuses ${why}`,
        JSON.stringify(messages)
    );
    await rm(root, { recursive: true, force: true });
};
await refusedBy(null, "a change still at `proposed`", /is at `status: proposed`/);
await refusedBy((b) => b.replace("status: accepted", "status: approved").replace(/accepted-.*\n/g, ""), "a change approved and not accepted", /is at `status: approved`/);
await refusedBy((b) => b.replace(/accepted-by.*\n/, ""), "an unsigned acceptance", /without `accepted-by`/);
await refusedBy((b) => b.replace(/approved-hash.*\n/, ""), "a decision with no fingerprint", /carries no `approved-hash`/);
await refusedBy((b) => b, "a change edited after its decision", /changed after the decision/, (root) =>
    editFile(root, QUALITY_DELTA, (t) => t.replace("50 ms", "20 ms"))
);
await refusedBy((b) => b, "an open question in any delta", /open `kind: question`/, (root) =>
    editFile(root, QUALITY_DELTA, (t) => t + note("question"))
);

{
    const root = await fixture();
    const hash = await decide(root);
    const graph = await buildGraph(root);
    check(errorsOf(graph.problems).length === 0, "an accepted change over its current fingerprint indexes clean", JSON.stringify(errorsOf(graph.problems)));
    await editFile(root, QUALITY_DELTA, (t) => t + note("comment"));
    check((await changeFingerprint(root, "add-cache")) === hash, "a note in a delta does not change the change's fingerprint");
    await editFile(root, QUALITY_DELTA, (t) => t.replace("50 ms", "20 ms"));
    const stale = await buildGraph(root);
    check(
        errorsOf(stale.problems).some((i) => /content that has changed since `approved-at`/.test(i.message)),
        "the check reports a decided change edited since",
        JSON.stringify(errorsOf(stale.problems))
    );

    const printed = [];
    const log = console.log;
    console.log = (line) => printed.push(line);
    const cwd = process.cwd();
    process.chdir(root);
    await hashMain([`${CHANGES_ROOT}/add-cache`]);
    await hashMain([PROPOSAL]);
    process.chdir(cwd);
    console.log = log;
    const now = await changeFingerprint(root, "add-cache");
    check(printed[0] === now && printed[1] === now, "chapter-hash.mjs prints the change's fingerprint for the folder and for its proposal", JSON.stringify(printed));
    await rm(root, { recursive: true, force: true });
}

// -- A merge lifts a chapter rung it makes stale --------------------------------

{
    const rung = "type: aggregate\nstatus: approved\napproved-by: @amy\napproved-at: 2026-09-01\n";
    const model =
        `# Billing domain\n\n${fence("")}\n` +
        `## Invoice\n\n${fence(rung)}\nAn invoice.\n\n` +
        `## Payment\n\n${fence(rung)}\nA payment.\n`;
    const delta = fence("change: add-cache\ndelta: modified\n") + `\n## Invoice\n\n### ADDED\n\n#### Totals\n\n${fence("")}\nSummed per line.\n`;
    const merged = mergeDelta(parseDelta(delta), model, "add-cache");
    const invoice = merged.merged.split("## Invoice")[1].split("## Payment")[0];
    check(!/approved|accepted/.test(invoice), "a merge lifts the rung off a chapter whose content it changed, and writes none of its own", merged.merged);
    check(merged.merged.includes(`## Payment\n\n${fence(rung)}`), "a chapter the merge left alone keeps its rung", merged.merged);
    check(merged.issues.some((i) => i.severity === "info" && /lifts `approved` from "Invoice"/.test(i.message)), "the lift is reported", JSON.stringify(merged.issues));
}

// -- What --check refuses ----------------------------------------------------

const refusals = [
    {
        name: "a delta naming a chapter the target lacks",
        delta: fence("change: add-cache\ndelta: modified\n") + "\n## Storage\n\n### ADDED\n\n#### Tier\n\nHot and cold.\n",
        expect: /no level-2 heading/,
    },
    {
        name: "a section that is not ADDED, MODIFIED, or REMOVED",
        delta: fence("change: add-cache\ndelta: modified\n") + "\n## Caching\n\n### CHANGED\n\n#### Consequences\n\nNone.\n",
        expect: /is not one of ADDED, MODIFIED, REMOVED/,
    },
    {
        name: "a header naming another change",
        delta: fence("change: other\ndelta: modified\n") + "\n## Caching\n\n### REMOVED\n\n#### Open questions\n",
        expect: /sits in the change folder `add-cache`/,
    },
    {
        name: "a header carrying a status",
        delta: fence("change: add-cache\ndelta: modified\nstatus: proposed\n") + "\n## Caching\n\n### REMOVED\n\n#### Open questions\n",
        expect: /holds only `change` and `delta`/,
    },
    {
        name: "adding a chapter the target already has",
        delta: fence("change: add-cache\ndelta: added\n") + "\n## Caching\n\nAgain.\n",
        expect: /already has/,
    },
    {
        name: "replacing a section the chapter lacks",
        delta: fence("change: add-cache\ndelta: modified\n") + "\n## Caching\n\n### MODIFIED\n\n#### Risks\n\nNone.\n",
        expect: /no such section — use ADDED/,
    },
    {
        name: "a merge that would leave the target invalid",
        delta: fence("change: add-cache\ndelta: modified\n") + "\n## Caching\n\n### MODIFIED\n\n" + fence("status: approved\n"),
        expect: /would leave .* invalid/,
    },
];
for (const c of refusals) {
    const root = await fixture({ [`${CHANGES_ROOT}/add-cache/devbook-delta/arc42/09-decisions.md`]: c.delta });
    const report = await checkChange(root, "add-cache");
    const messages = errorsOf(report.deltas.flatMap((d) => d.issues)).map((i) => i.message);
    check(messages.some((m) => c.expect.test(m)), `--check refuses ${c.name}`, JSON.stringify(messages));
    const result = await applyChange(root, "add-cache", { date: "2026-09-28" });
    check(!result.applied && (await readFile(path.join(root, DECISIONS), "utf8")) === decisions, `--apply writes nothing for ${c.name}`);
    await rm(root, { recursive: true, force: true });
}

{
    const root = await fixture();
    await rm(path.join(root, CHANGES_ROOT, "add-cache/devbook-delta"), { recursive: true });
    const report = await checkChange(root, "add-cache");
    check(errorsOf(report.problems).some((i) => /no delta/.test(i.message)), "--check refuses a change with no delta at all");
    await rm(root, { recursive: true, force: true });
}

// -- Merge shapes, in memory ---------------------------------------------------

{
    const run = (delta, original) => mergeDelta(parseDelta(delta), original, "add-cache");
    const addedChapter = run(fence("change: add-cache\ndelta: added\n") + `\n## Metrics\n\n${fence("")}\nCount hits.\n`, decisions);
    check(addedChapter.issues.length === 0 && addedChapter.merged.trimEnd().endsWith("## Metrics\n\n```meta\nchange: add-cache\n```\n\nCount hits."), "an added `##` chapter lands at the end of its file, stamped", addedChapter.merged);
    const removed = run(fence("change: add-cache\ndelta: removed\n") + "\n## Logging\n", decisions);
    check(removed.issues.length === 0 && !removed.merged.includes("## Logging") && removed.merged.includes("## Caching"), "a removed chapter goes whole, its siblings stay");
    const removedFile = run(fence("change: add-cache\ndelta: removed\n") + "\n# Decisions\n", decisions);
    check(removedFile.issues.length === 0 && removedFile.merged === null, "removing the `#` chapter removes the file");
    const deep = run(fence("change: add-cache\ndelta: added\n") + "\n### Orphan\n\nNo parent.\n", decisions);
    check(errorsOf(deep.issues).some((i) => /no parent/.test(i.message)), "an added chapter below `##` is refused: it is an ADDED entry of its parent");
    const placeholder = run(fence("change: add-cache\ndelta: modified\n"), decisions);
    check(placeholder.placeholder && placeholder.merged === decisions, "a delta naming no chapter is a placeholder and merges nothing");
}

// -- The proposal ------------------------------------------------------------

{
    const at = `${CHANGES_ROOT}/add-cache/proposal.md`;
    check(errorsOf(validateDocument(at, proposal)).length === 0, "the fixture proposal validates");
    const noCategory = proposal.replace("category: feature\n", "");
    check(errorsOf(validateDocument(at, noCategory)).some((i) => /missing required `category`/.test(i.message)), "a proposal names its category");
    const badCategory = proposal.replace("category: feature", "category: chore");
    check(errorsOf(validateDocument(at, badCategory)).some((i) => /`category` "chore"/.test(i.message)), "the category is one of three");
    const approved = proposal.replace("status: proposed", "status: approved\napproved-by: @amy\napproved-at: 2026-09-20");
    check(errorsOf(validateDocument(at, approved)).length === 0, "a proposal holds the approval rung and its record");
    const active = proposal.replace("status: proposed", "status: active");
    check(errorsOf(validateDocument(at, active)).length === 1, "a proposal's ladder is proposed, approved, accepted");
    const hashed = approved.replace("approved-at: 2026-09-20", "approved-at: 2026-09-20\napproved-hash: sha256:00000000");
    check(
        errorsOf(validateDocument(at, hashed, { changeHash: "sha256:11111111" })).some((i) => /changed since `approved-at`/.test(i.message)),
        "a proposal's hash is checked against the whole change's"
    );
    check(changeHash(proposal, [{ target: "a", markdown: "x" }]) !== changeHash(proposal, [{ target: "a", markdown: "y" }]), "the change's fingerprint covers its deltas");
    const wrongType = proposal.replace("type: change", "type: feature");
    check(errorsOf(validateDocument(at, wrongType)).length === 1, "a proposal is `type: change`");
    check(errorsOf(validateDocument(DECISIONS, decisions.replace("status: proposed", "status: proposed\nchange: Add Cache"))).length === 1, "`change` on a chapter is one change name");
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
