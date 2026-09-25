// Two headings in one file that slugify identically are an error, not
// last-writer-wins: nothing is assigned, so the address `<path>#<slug>` cannot
// tell them apart and a reference to either is ambiguous. The graph reports the
// collision and keeps the *first* heading.
//
// The load-bearing case is the mixed pair. A structural heading claims an
// anchor just as an addressable one does, so keep-the-first and the collision
// check both have to run before the "no `meta` fence, not a chapter" guard —
// otherwise a structural heading shadowing a chapter is never reported at all.
//
// Two *structural* headings sharing an anchor is not an error: it is the
// ordinary shape of a chapter file, where two rules in one `domain.invariants.md`
// carry a `#### Scenario:` of the same name and every event carries its own
// `### Payload`. Those are only ever materialized on demand, and the first
// is the one GitHub leaves unsuffixed, so resolving to it is correct.
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildGraph } from "./graph.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const REL = ".devbook/domain/context-map.md";
const ANCHOR = `${REL}#order-management`;

// "Order Management" and "Order Management!" both slugify to "order-management":
// GitHub's algorithm strips the punctuation.
const HEAD = `# Context Map\n\n${fence("index: root\ntype: context-map\n")}\n`;
const chapter = (text, body) => `## ${text}\n\n${fence(body)}\n${text} prose.\n\n`;
const structural = (text) => `## ${text}\n\nNo fence, so not a chapter.\n\n`;

async function graphOf(body) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-anchor-"));
    try {
        await mkdir(path.join(root, path.dirname(REL)), { recursive: true });
        await writeFile(path.join(root, REL), HEAD + body, "utf8");
        return await buildGraph(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const collisions = (graph) =>
    graph.problems.filter(
        (p) => p.severity === "error" && p.message.includes(`Duplicate chapter anchor "${ANCHOR}"`)
    );
const nodeAt = (graph, id) => graph.nodes.find((n) => n.id === id);

// ── Two addressable headings ────────────────────────────────────────────────

const twoChapters = await graphOf(
    chapter("Order Management", "type: bounded-context\nstatus: draft\n") +
        chapter("Order Management!", "type: bounded-context\nstatus: approved\n")
);
check(
    collisions(twoChapters).length === 1,
    "two chapters on one anchor report the collision exactly once",
    JSON.stringify(twoChapters.problems.map((p) => p.message))
);
const kept = nodeAt(twoChapters, ANCHOR);
check(
    kept?.label === "Order Management" && kept?.status === "draft",
    "the first chapter keeps the anchor — the second does not overwrite it",
    JSON.stringify(kept)
);
check(
    twoChapters.nodes.filter((n) => n.id === ANCHOR).length === 1,
    "the collision leaves one node, not two under one id"
);

// ── A structural heading shadowed by a later chapter ────────────────────────

const structuralFirst = await graphOf(
    structural("Order Management") + chapter("Order Management!", "type: bounded-context\n")
);
check(
    collisions(structuralFirst).length === 1,
    "a chapter shadowed by an earlier structural heading is reported, not swallowed by the fence guard",
    JSON.stringify(structuralFirst.problems.map((p) => p.message))
);
check(
    nodeAt(structuralFirst, ANCHOR) === undefined,
    "the first heading still wins, so the shadowed chapter contributes no node",
    JSON.stringify(nodeAt(structuralFirst, ANCHOR))
);

// ── A chapter shadowed by a later structural heading ────────────────────────

const chapterFirst = await graphOf(
    chapter("Order Management", "type: bounded-context\n") + structural("Order Management!")
);
check(
    collisions(chapterFirst).length === 1,
    "a structural heading landing on a chapter's anchor is reported too",
    JSON.stringify(chapterFirst.problems.map((p) => p.message))
);
check(
    nodeAt(chapterFirst, ANCHOR)?.type === "chapter",
    "the chapter that got there first survives",
    JSON.stringify(nodeAt(chapterFirst, ANCHOR))
);

// ── Two structural headings: kept first, and silent ───────────────────────

const twoStructural = await graphOf(
    chapter("Order Management", "type: bounded-context\n") +
        structural("Scenario: The order is already confirmed") +
        chapter("Order Fulfilment", "type: bounded-context\n") +
        structural("Scenario: The order is already confirmed!")
);
check(
    twoStructural.problems.filter((p) => /Duplicate chapter anchor/.test(p.message)).length === 0,
    "the repeated `#### Scenario:` pattern is not an error",
    JSON.stringify(twoStructural.problems.map((p) => p.message))
);
check(
    twoStructural.nodes.filter((n) => n.id === `${REL}#scenario-the-order-is-already-confirmed`).length === 0,
    "and neither structural heading becomes a node on its own"
);

// ── The negative: distinct slugs are not a collision ────────────────────────

const distinct = await graphOf(
    chapter("Order Management", "type: bounded-context\n") +
        chapter("Order Fulfilment", "type: bounded-context\n")
);
check(
    distinct.problems.filter((p) => /Duplicate chapter anchor/.test(p.message)).length === 0,
    "two differently-slugged headings are silent",
    JSON.stringify(distinct.problems.map((p) => p.message))
);
check(
    nodeAt(distinct, ANCHOR) !== undefined &&
        nodeAt(distinct, `${REL}#order-fulfilment`) !== undefined,
    "and both still become nodes"
);

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
