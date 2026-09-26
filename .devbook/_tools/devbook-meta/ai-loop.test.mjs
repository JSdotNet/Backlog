// Asserts what the `.ai` loop picture reads from, per devbook-ai.md "The loop
// picture": a chapter is placed on the loop by its own `stage`, drawn from the
// fixed DevOps vocabulary and never from the file it sits in; a usage names its
// technology in `depends-on` rather than `related`; and `stage` reaches the
// graph as a node attribute.
//
// Run: `node ai-loop.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { validateDocument, AI_STAGES } from "./metadata.mjs";
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

const usageFile = (fileMeta, chapters) =>
    `# 1. Author\n\n${fence(fileMeta)}\nProse.\n\n` +
    chapters.map(([name, meta]) => `## ${name}\n\n${fence(meta)}\nProse.\n`).join("\n");

// --- the vocabulary is the loop's, in loop order ---------------------------

check(
    AI_STAGES.join(" ") === "plan code build test release deploy operate monitor",
    "the eight stages are exported in loop order",
    AI_STAGES.join(" ")
);

// --- stage: on every chapter, from the fixed vocabulary -------------------

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [
            ["Coding Agent", "status: trial\ntype: agent\nstage: [code, test]\n"],
            ["Release Notes", "status: candidate\ntype: skill\nstage: release\n"],
        ])
    );
    check(!find(issues, "error", "`stage`") && !find(issues, "warning", "`stage`"), "chapters placed with loop words are silent, list or scalar", dump(issues));
}

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [["Coding Agent", "status: trial\ntype: agent\nstage: [specify, code]\n"]])
    );
    check(
        Boolean(find(issues, "error", '`stage` entry "specify", expected one of: plan, code')),
        "a word outside the eight is an error",
        dump(issues)
    );
}

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [["Coding Agent", "status: trial\ntype: agent\n"]])
    );
    check(
        Boolean(find(issues, "warning", "has no `stage` — a usage says which stages")),
        "a usage without `stage` is off the loop and warns",
        dump(issues)
    );
}

{
    const issues = validateDocument(
        ".devbook/ai/concepts.md",
        `# Concepts\n\n${fence("status: adopted\ntype: concepts\n")}\nProse.\n\n## Context Engineering\n\n${fence("status: adopted\ntype: concept\n")}\nProse.\n\n## Eval Harness\n\n${fence("status: trial\ntype: concept\nstage: [test]\n")}\nProse.\n`
    );
    check(!find(issues, "warning", "has no `stage`"), "a concept without `stage` is the middle of the loop and silent", dump(issues));
}

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\nstage: [code]\n", [["Coding Agent", "status: trial\ntype: agent\nstage: [code]\n"]])
    );
    check(
        Boolean(find(issues, "error", "`stage` on the file-level block — a file groups chapters and places none")),
        "`stage` on a file-level block is an error — a file places nothing",
        dump(issues)
    );
}

// --- the tool edge is depends-on, never related ---------------------------

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [
            [
                "Coding Agent",
                "status: trial\ntype: agent\nstage: [code]\nrelated: [.devbook/tech/tooling.md#claude-code, .devbook/arc42/adr/hosts.md]\n",
            ],
        ])
    );
    check(
        Boolean(find(issues, "warning", "reaching into .tech")),
        "a `related` entry into .tech on an ai chapter is a warning",
        dump(issues)
    );
    check(
        !find(issues, "warning", ".devbook/arc42/adr/hosts.md"),
        "a `related` entry into .arc42 stays silent",
        dump(issues)
    );
}

{
    const issues = validateDocument(
        ".devbook/ai/01-author.md",
        usageFile("status: trial\ntype: stage\n", [
            ["Coding Agent", "status: trial\ntype: agent\nstage: [code]\ndepends-on: [.devbook/tech/tooling.md#claude-code]\n"],
        ])
    );
    check(!find(issues, "warning", "reaching into .tech"), "the same reference in `depends-on` is silent", dump(issues));
}

// --- stage reaches the graph as a list, beside date -----------------------

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
                ["Coding Agent", "status: trial\ntype: agent\nstage: code\ndate: 2026-09-01\n"],
                ["Eval Gate", "status: candidate\ntype: guardrail\nstage: [test, release]\n"],
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
        check(JSON.stringify(gate?.stage) === '["test","release"]', "a stage list is carried in authored order", JSON.stringify(gate));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

if (failed) {
    console.error(`\n${failed} check(s) failed`);
    process.exit(1);
}
console.log("\nall checks passed");
