// Asserts what the `.ai` views read from, per devbook-ai.md "The stages" and
// "The picture": a chapter is placed by its own `stage` or `layer`, drawn from
// the fixed vocabularies and never from the file it sits in; each `substage`
// splits a stage the chapter lists; each `feeds` entry is a labelled reference
// the graph resolves like `related`; `evidence` is one word; a chapter below
// `adopted` says what moves it up; and a usage names its technology in
// `depends-on` rather than `related`.
//
// Run: `node ai-loop.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import {
    validateDocument,
    parseFeed,
    AI_STAGES,
    AI_STAGE_NAMES,
    AI_STAGES_BEFORE_PULL_REQUEST,
    AI_LAYERS,
    AI_EVIDENCE,
} from "./metadata.mjs";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const find = (issues, severity, needle) =>
    issues.find((i) => i.severity === severity && i.message.includes(needle));
const dump = (issues) => JSON.stringify(issues, null, 2);
const MOVES = "\n- **Moves up when** — a second person works this way.\n";

const usageFile = (fileMeta, chapters) =>
    `# 1. Author\n\n${fence(fileMeta)}\nProse.\n\n` +
    chapters.map(([name, meta, body = MOVES]) => `## ${name}\n\n${fence(meta)}\nProse.\n${body}`).join("\n");
const one = (meta, body) => validateDocument(".devbook/ai/01-author.md", usageFile("status: trial\ntype: stage\n", [["Coding Agent", meta, body]]));

// --- the vocabularies -------------------------------------------------------

check(
    AI_STAGES.join(" ") === "decide specify code verify integrate release operate",
    "the seven stages are exported in path order",
    AI_STAGES.join(" ")
);
check(
    AI_STAGES.map((s) => AI_STAGE_NAMES[s]).join(" | ") ===
        "Decide | Specify & plan | Code | Verify | Integrate | Release & deploy | Operate & feedback",
    "every stage has its display name",
    JSON.stringify(AI_STAGE_NAMES)
);
check(AI_STAGES[AI_STAGES_BEFORE_PULL_REQUEST - 1] === "verify", "the pull request falls after verify");
check(AI_LAYERS.join(" ") === "thread upkeep harness", "the three layers are exported", AI_LAYERS.join(" "));
check(AI_EVIDENCE.join(" ") === "recorded partial fired none", "the four evidence words are exported", AI_EVIDENCE.join(" "));

// --- stage ------------------------------------------------------------------

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [
            ["Coding Agent", "status: trial\ntype: agent\nstage: [code, verify]\n"],
            ["Release Notes", "status: candidate\ntype: skill\nstage: release\n"],
        ])
    );
    check(issues.every((i) => i.severity === "info"), "chapters placed with path words are silent, list or scalar", dump(issues));
}

{
    const issues = one("status: trial\ntype: agent\nstage: [plan, code]\n");
    check(
        Boolean(find(issues, "error", '`stage` entry "plan", expected one of: decide, specify, code')),
        "a word outside the seven — an old loop word included — is an error",
        dump(issues)
    );
}

{
    const issues = one("status: trial\ntype: agent\nstage: [monitor, custom]\n");
    check(
        Boolean(find(issues, "error", "migration `035-delivery-path-stages` maps it onto the path")) &&
            !issues.some((i) => i.message.includes('"custom"') && i.message.includes("035-delivery-path-stages")),
        "an old loop word names the migration that moves it; any other word does not",
        dump(issues)
    );
}

{
    const issues = one("status: trial\ntype: agent\n");
    check(
        Boolean(find(issues, "warning", "has no `stage` and no `layer`")),
        "a usage with neither `stage` nor `layer` is off the picture and warns",
        dump(issues)
    );
}

{
    const issues = validateDocument(
        ".devbook/ai/concepts.md",
        `# Concepts\n\n${fence("status: adopted\ntype: concepts\n")}\nProse.\n\n## Context Engineering\n\n${fence("status: adopted\ntype: concept\n")}\nProse.\n\n## Eval Harness\n\n${fence("status: trial\ntype: concept\nstage: [verify]\n")}\nProse.\n${MOVES}`
    );
    check(!find(issues, "warning", "no `stage`"), "a concept with neither is applied throughout and silent", dump(issues));
}

// --- layer ------------------------------------------------------------------

{
    const issues = one("status: adopted\ntype: practice\nlayer: [harness]\n", "");
    check(issues.every((i) => i.severity === "info"), "a chapter placed by `layer` alone is silent", dump(issues));
}

{
    const issues = one("status: adopted\ntype: practice\nstage: [code]\nlayer: [thread, upkeep]\n", "");
    check(issues.every((i) => i.severity === "info"), "a chapter may carry `stage` and `layer` both", dump(issues));
}

{
    const issues = one("status: adopted\ntype: practice\nlayer: [platform]\n", "");
    check(
        Boolean(find(issues, "error", '`layer` entry "platform", expected one of: thread, upkeep, harness')),
        "a layer outside the three is an error",
        dump(issues)
    );
}

// --- substage ---------------------------------------------------------------

{
    const issues = one("status: trial\ntype: agent\nstage: [verify, integrate]\nsubstage: [verify/review, integrate/review, verify/functional-qa]\n");
    check(issues.every((i) => i.severity === "info"), "sub-stages under listed stages, in the repository's own words, are silent", dump(issues));
}

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\nsubstage: [verify/review]\n");
    check(
        Boolean(find(issues, "error", '`substage` entry "verify/review" under "verify", which its `stage` does not list')),
        "a sub-stage under a stage the chapter does not list is an error",
        dump(issues)
    );
}

{
    const issues = one("status: trial\ntype: agent\nstage: [verify]\nsubstage: [review, verify/Code Review]\n");
    check(
        Boolean(find(issues, "error", '`substage` entry "review" — a sub-stage is written `<stage>/<word>`')) &&
            Boolean(find(issues, "error", '`substage` entry "verify/Code Review"')),
        "a sub-stage without its stage, or not kebab-case, is an error",
        dump(issues)
    );
}

// --- feeds ------------------------------------------------------------------

check(
    JSON.stringify(parseFeed(".devbook/ai/02-deliver.md#code-gate: the agreed plan")) ===
        '{"ref":".devbook/ai/02-deliver.md#code-gate","label":"the agreed plan"}',
    "a feed splits into the reference and its label"
);
check(parseFeed(".devbook/ai/02-deliver.md#code-gate") === null, "a feed with no label does not parse");

{
    const issues = one('status: trial\ntype: agent\nstage: [code]\nfeeds: [".devbook/ai/01-author.md#coding-agent"]\n');
    check(
        Boolean(find(issues, "error", "the arrow's label, which is required")),
        "a `feeds` entry without its label is an error",
        dump(issues)
    );
}

// --- evidence ---------------------------------------------------------------

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\nevidence: partial\n");
    check(issues.every((i) => i.severity === "info"), "an evidence word from the four is silent", dump(issues));
}

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\nevidence: some\n");
    check(
        Boolean(find(issues, "error", '`evidence` "some", expected one of: recorded, partial, fired, none')),
        "an evidence word outside the four is an error",
        dump(issues)
    );
}

// --- Moves up when ----------------------------------------------------------

{
    const issues = one("status: candidate\ntype: agent\nstage: [code]\n", "\n- **Evidence** — none yet.\n");
    check(
        Boolean(find(issues, "warning", "is `candidate` and has no `- **Moves up when** — …` bullet")),
        "a candidate without **Moves up when** warns",
        dump(issues)
    );
}

{
    const issues = one("status: adopted\ntype: agent\nstage: [code]\n", MOVES);
    check(
        Boolean(find(issues, "warning", "is `adopted` and still carries a **Moves up when** bullet")),
        "an adopted chapter carrying **Moves up when** warns",
        dump(issues)
    );
}

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\n", "\n```text\n- **Moves up when** — in a fence\n```\n");
    check(Boolean(find(issues, "warning", "has no `- **Moves up when**")), "a bullet inside a fence does not count", dump(issues));
}

// --- placement fields stay off the file -------------------------------------

for (const field of ["stage: [code]", "substage: [code/start]", "layer: [harness]", "evidence: none", 'feeds: [".devbook/ai/01-author.md#coding-agent: x"]']) {
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile(`status: trial\ntype: stage\n${field}\n`, [["Coding Agent", "status: trial\ntype: agent\nstage: [code]\n"]])
    );
    const name = field.split(":")[0];
    check(
        Boolean(find(issues, "error", `\`${name}\` on the file-level block — a file groups chapters and places none`)),
        `\`${name}\` on a file-level block is an error — a file places nothing`,
        dump(issues)
    );
}

// --- the tool edge is depends-on, never related -----------------------------

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\nrelated: [.devbook/tech/tooling.md#claude-code, .devbook/arc42/adr/hosts.md]\n");
    check(Boolean(find(issues, "warning", "reaching into .tech")), "a `related` entry into .tech on an ai chapter is a warning", dump(issues));
    check(!find(issues, "warning", ".devbook/arc42/adr/hosts.md"), "a `related` entry into .arc42 stays silent", dump(issues));
}

{
    const issues = one("status: trial\ntype: agent\nstage: [code]\ndepends-on: [.devbook/tech/tooling.md#claude-code]\n");
    check(!find(issues, "warning", "reaching into .tech"), "the same reference in `depends-on` is silent", dump(issues));
}

// --- the fields reach the graph ---------------------------------------------

{
    const root = await mkdtemp(path.join(tmpdir(), "devbook-ai-loop-"));
    try {
        await mkdir(path.join(root, ".devbook", "ai"), { recursive: true });
        await writeFile(
            path.join(root, ".devbook", "ai", "adoption-map.md"),
            `# Map\n\n${fence("status: trial\nindex: root\ntype: adoption-map\n")}\nProse.\n`
        );
        await writeFile(
            path.join(root, ".devbook", "ai", "01-author.md"),
            usageFile("status: trial\ntype: stage\n", [
                [
                    "Coding Agent",
                    'status: trial\ntype: agent\nstage: code\nsubstage: code/implement\nevidence: fired\ndate: 2026-09-01\nfeeds: [".devbook/ai/01-author.md#eval-gate: the change to check"]\n',
                ],
                ["Eval Gate", "status: candidate\ntype: guardrail\nstage: [verify, integrate]\nlayer: [thread]\n"],
                ["Broken Feed", 'status: candidate\ntype: guardrail\nstage: [verify]\nfeeds: [".devbook/ai/01-author.md#nowhere: lost"]\n'],
            ])
        );
        const graph = await buildGraph(root);
        const agent = graph.nodes.find((n) => n.id === ".devbook/ai/01-author.md#coding-agent");
        const gate = graph.nodes.find((n) => n.id === ".devbook/ai/01-author.md#eval-gate");
        check(
            JSON.stringify(agent?.stage) === '["code"]' && agent?.date === "2026-09-01",
            "a scalar stage is emitted as a one-item list beside date",
            JSON.stringify(agent)
        );
        check(JSON.stringify(gate?.stage) === '["verify","integrate"]', "a stage list is carried in authored order", JSON.stringify(gate));
        check(
            JSON.stringify(agent?.substage) === '["code/implement"]' && agent?.evidence === "fired" && JSON.stringify(gate?.layer) === '["thread"]',
            "substage and layer reach the node as lists, evidence as written",
            JSON.stringify({ agent, gate })
        );
        const feed = graph.edges.find((e) => e.type === "feeds" && e.source === agent?.id);
        check(
            feed?.target === gate?.id && feed?.label === "the change to check",
            "a feed is an edge to the chapter before the colon, labelled with the text after it",
            JSON.stringify(graph.edges.filter((e) => e.type === "feeds"))
        );
        const problems = graph.problems ?? [];
        check(
            problems.some((p) => p.severity === "error" && p.message.includes('`feeds` reference ".devbook/ai/01-author.md#nowhere"')),
            "a feed that does not resolve is an error, the way `related` is",
            JSON.stringify(problems)
        );
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

if (failed) {
    console.error(`\n${failed} check(s) failed`);
    process.exit(1);
}
console.log("\nall checks passed");
