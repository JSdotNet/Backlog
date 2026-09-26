// How a bounded context ships is written twice: `deployment` on its
// `bounded-context` chapter in `context-map.md`, and on the file-level block of
// its own `context.md`. The chapter's `related` names that file, and the graph
// build holds the two equal — a difference, or the field on one side only, is
// an error. A chapter that names no `context.md`, and a `context.md` no chapter
// names, are left alone: a repository that lists its contexts only in tables
// writes the field on `context.md` alone.
//
// Run: `node deployment-pairing.test.mjs`
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
const MAP = ".devbook/domain/context-map.md";
const CONTEXT = ".devbook/domain/ordering/context.md";

const mapWith = (meta) =>
    `# Order Platform\n\n${fence("index: root\ntype: context-map\n")}\n## Ordering\n\n${fence(meta)}\nOrdering prose.\n`;
const contextWith = (deployment) =>
    `# Ordering\n\n${fence(`index: root\ntype: context\n${deployment ? `deployment: ${deployment}\n` : ""}`)}\nProse.\n`;

async function graphOf(files) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-deployment-"));
    try {
        for (const [rel, body] of Object.entries(files)) {
            await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
            await writeFile(path.join(root, rel), body, "utf8");
        }
        return await buildGraph(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

const mismatch = (graph) =>
    graph.problems.find((p) => p.severity === "error" && p.message.includes("how a context ships"));
const dump = (graph) => JSON.stringify(graph.problems, null, 2);
const paired = (deployment) => `type: bounded-context\n${deployment ? `deployment: ${deployment}\n` : ""}related: [${CONTEXT}]\n`;

{
    const graph = await graphOf({ [MAP]: mapWith(paired("module")), [CONTEXT]: contextWith("module") });
    check(!mismatch(graph), "the same `deployment` on the chapter and its `context.md` is silent", dump(graph));
    const node = graph.nodes.find((n) => n.id === CONTEXT);
    check(node?.deployment === "module", "the `context.md` node carries `deployment`", JSON.stringify(node));
}
{
    const graph = await graphOf({ [MAP]: mapWith(paired(null)), [CONTEXT]: contextWith(null) });
    check(!mismatch(graph), "no `deployment` on either side is silent", dump(graph));
}
{
    const graph = await graphOf({ [MAP]: mapWith(paired("module")), [CONTEXT]: contextWith("service") });
    check(Boolean(mismatch(graph)), "`module` on the chapter and `service` on `context.md` is an error", dump(graph));
}
{
    const graph = await graphOf({ [MAP]: mapWith(paired("service")), [CONTEXT]: contextWith(null) });
    check(Boolean(mismatch(graph)), "`deployment` on the chapter only is an error", dump(graph));
}
{
    const graph = await graphOf({ [MAP]: mapWith(paired(null)), [CONTEXT]: contextWith("service") });
    check(Boolean(mismatch(graph)), "`deployment` on `context.md` only, with a chapter naming it, is an error", dump(graph));
}
{
    const graph = await graphOf({
        [MAP]: mapWith("type: bounded-context\ndeployment: service\n"),
        [CONTEXT]: contextWith("module"),
    });
    check(!mismatch(graph), "a chapter whose `related` names no `context.md` is not paired", dump(graph));
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
