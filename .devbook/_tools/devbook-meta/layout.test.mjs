// Exercises the one folder layout: five folders under a single `.devbook/`
// parent whose subfolders drop the dot. The root-level dot-folder spelling
// (`.arc42/`) is no longer a layout (record 80): a stray one is reported as an
// error and never indexed, so a repository that has not moved learns it from
// the check rather than from a quiet half-corpus.
//
// Run: `node layout.test.mjs`
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { folderKindForPath, validateDocument, DEVBOOK_FOLDER_NAMES, DEVBOOK_ROOT } from "./metadata.mjs";
import {
    buildGraph,
    buildGraphDocument,
    discoverLayout,
    discoverScopes,
    outputPathFor,
    resolveScope,
    SCOPES,
    DEVBOOK_FOLDERS,
    REPO_SCOPE,
} from "./graph.mjs";
import { buildOutlineDocument, outlinePathFor } from "./outline.mjs";
import { annotationsPathFor } from "./annotations-index.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

// -- Path resolution --------------------------------------------------------

for (const name of DEVBOOK_FOLDER_NAMES) {
    check(folderKindForPath(`${DEVBOOK_ROOT}/${name}/x.md`) === name, `${DEVBOOK_ROOT}/${name}/ resolves to ${name}`);
    check(
        folderKindForPath(`${DEVBOOK_ROOT}/${name}/deep/nested/x.md`) === name,
        `${DEVBOOK_ROOT}/${name}/ resolves at any depth`
    );
    check(folderKindForPath(`.${name}/x.md`) === null, `a root-level .${name}/ resolves to nothing`);
}
check(folderKindForPath("docs/x.md") === null, "a path outside the convention resolves to null");
check(folderKindForPath(`${DEVBOOK_ROOT}/notafolder/x.md`) === null, "an unknown subfolder of the parent resolves to null");
check(folderKindForPath(`${DEVBOOK_ROOT}/.domain/x.md`) === null, "the subfolders drop the dot, so .devbook/.domain is not a folder");
check(folderKindForPath(`${DEVBOOK_ROOT}/domainish/x.md`) === null, "a folder that merely starts with a known name is not that folder");
check(folderKindForPath(String.raw`.devbook\tech\x.md`) === "tech", "a Windows path separator still resolves");

// -- Scope names ------------------------------------------------------------

check(DEVBOOK_FOLDERS.every((f) => f.startsWith(`${DEVBOOK_ROOT}/`)), "every folder path lives under the parent", JSON.stringify(DEVBOOK_FOLDERS));
check(SCOPES.length === DEVBOOK_FOLDER_NAMES.length + 1, "SCOPES is the rollup plus one per folder, one spelling each");
check(resolveScope("tech") === `${DEVBOOK_ROOT}/tech`, "a bare folder name resolves to its scope");
check(resolveScope(".tech") === `${DEVBOOK_ROOT}/tech`, "the dotted short name resolves to its scope");
check(resolveScope(`${DEVBOOK_ROOT}/tech/`) === `${DEVBOOK_ROOT}/tech`, "the full path resolves, trailing slash tolerated");
check(resolveScope(".") === REPO_SCOPE, "the rollup resolves to itself");
check(resolveScope("backlog") === null && resolveScope("") === null, "an unknown or empty scope resolves to null");
check(outputPathFor(REPO_SCOPE) === `${DEVBOOK_ROOT}/_meta/graph.json`, "the rollup graph lives under the parent", outputPathFor(REPO_SCOPE));
check(outlinePathFor(REPO_SCOPE) === `${DEVBOOK_ROOT}/_meta/index.json`, "the rollup outline lives under the parent");
check(annotationsPathFor(REPO_SCOPE) === `${DEVBOOK_ROOT}/_meta/annotations.json`, "the rollup annotations live under the parent");
check(outputPathFor(`${DEVBOOK_ROOT}/tech`) === `${DEVBOOK_ROOT}/tech/_meta/graph.json`, "a folder scope writes its _meta beside its own chapters");

// -- A corpus, and a stray folder beside it ----------------------------------

const fence = (body) => "```meta\n" + body + "```\n";
const at = (name) => `${DEVBOOK_ROOT}/${name}`;
const corpus = [
    [
        `${at("domain")}/context-map.md`,
        `# Context Map\n\n${fence("index: root\ntype: context-map\n")}\n` +
            `## Order Management\n\n${fence(`type: bounded-context\nrelated: ["${at("tech")}/technology-graph.md#postgres"]\n`)}\nA context.\n`,
    ],
    [
        `${at("tech")}/technology-graph.md`,
        `# Technology Graph\n\n${fence("status: adopted\nindex: root\n")}\n` +
            `## Postgres\n\n${fence("status: adopted\ntype: service\n")}\nA database.\n`,
    ],
    [`${at("tech")}/tooling.md`, `# Tooling\n\n${fence("status: adopted\n")}\nLast by convention.\n`],
    [`${at("tech")}/shared.md`, `# Shared\n\n${fence("status: adopted\n")}\nFirst by convention.\n`],
    [`${at("tech")}/backend.md`, `# Backend\n\n${fence("status: adopted\n")}\nIn between.\n`],
    // A bounded context with split files: beside their base (`domain`, `flow`),
    // in a dropped base's slot (`features`, `model`), two for one base, an
    // invariants subpage after the page it belongs to (`domain.md`, a split
    // `domain.order.md`) and none for a page without one (`domain.invoice.md`),
    // and an unprescribed extra that must stay filename-sorted among the rest.
    ...[
        ["domain.md", "domain"],
        ["domain.invariants.md", "invariants"],
        ["domain.order.md", "domain"],
        ["domain.order.invariants.md", "invariants"],
        ["domain.invoice.md", "domain"],
        ["actors.md", "actors"],
        ["features.checkout.md", "features"],
        ["model.order.md", "model"],
        ["flow.md", "flow"],
        ["flow.fulfilment.md", "flow"],
        ["dependencies.md", "dependencies"],
        ["notes.md", "flow"],
    ].map(([name, type]) => [
        `${at("domain")}/order-management/${name}`,
        `# Order Management\n\n${fence(`type: ${type}\n`)}\nA file.\n`,
    ]),
];

const root = await mkdtemp(path.join(tmpdir(), "devbook-layout-"));
try {
    for (const [rel, body] of corpus) {
        await mkdir(path.join(root, path.dirname(rel)), { recursive: true });
        await writeFile(path.join(root, rel), body, "utf8");
    }

    const layout = await discoverLayout(root);
    check(
        layout.folders.length === 2 && layout.folders.every((f) => f.startsWith(`${DEVBOOK_ROOT}/`)),
        "discovery returns the real .devbook paths of the adopted folders",
        JSON.stringify(layout)
    );
    check(layout.stray.length === 0, "a clean corpus has no stray folder");
    const scopes = await discoverScopes(root);
    check(scopes.includes(`${DEVBOOK_ROOT}/domain`) && scopes[0] === REPO_SCOPE, "scopes are the rollup plus the adopted folders", JSON.stringify(scopes));

    const graph = await buildGraph(root);
    check(graph.nodes.length > 0 && graph.edges.length > 0, "the corpus builds nodes and the cross-folder reference resolves", `${graph.nodes.length} nodes, ${graph.edges.length} edges`);
    check(graph.problems.filter((p) => p.severity === "error").length === 0, "a clean corpus builds without errors", JSON.stringify(graph.problems));

    const doc = await buildGraphDocument(root, `${DEVBOOK_ROOT}/domain`);
    check(doc.elements.nodes.length > 0, "a folder scope projects a non-empty graph", JSON.stringify(doc.stats));

    const outline = await buildOutlineDocument(root);
    check(outline.entries.length === layout.folders.length, "the repo-wide outline lists every adopted folder", JSON.stringify(outline.entries.map((e) => e.path)));
    const tech = await buildOutlineDocument(root, `${DEVBOOK_ROOT}/tech`);
    check(
        JSON.stringify(tech.entries.map((e) => e.name)) === JSON.stringify(["technology-graph.md", "shared.md", "backend.md", "tooling.md"]),
        "the folder convention orders a .devbook folder: root, pinned first, the rest, pinned last",
        JSON.stringify(tech.entries.map((e) => e.name))
    );
    const domain = await buildOutlineDocument(root, `${DEVBOOK_ROOT}/domain`);
    const context = domain.entries.find((e) => e.name === "order-management");
    const expected = [
        "domain.md",
        "domain.invariants.md",
        "domain.invoice.md",
        "domain.order.md",
        "domain.order.invariants.md",
        "actors.md",
        "features.checkout.md",
        "model.order.md",
        "flow.md",
        "flow.fulfilment.md",
        "dependencies.md",
        "notes.md",
    ];
    check(
        JSON.stringify(context?.children.map((e) => e.name)) === JSON.stringify(expected),
        "a split file reads after the file it is named after, or in that file's slot when it is gone, and a subpage after its page",
        JSON.stringify(context?.children.map((e) => e.name))
    );
    check(
        context?.children.find((e) => e.name === "domain.order.md")?.kind === "domain",
        "a split file keeps the kind of the file it is named after"
    );

    // -- The spelling that is no longer a layout ------------------------------
    await mkdir(path.join(root, ".design"), { recursive: true });
    await writeFile(path.join(root, ".design", "README.md"), `# Design\n\n${fence("status: draft\nindex: root\n")}\nStray.\n`, "utf8");
    const withStray = await discoverLayout(root);
    check(withStray.stray.includes(".design") && withStray.folders.length === 2, "a root-level dot-folder is reported as stray and not discovered", JSON.stringify(withStray));
    const strayGraph = await buildGraph(root);
    check(
        strayGraph.problems.some((p) => p.severity === "error" && /Only the `\.devbook\/` layout is supported/.test(p.message)),
        "a stray folder is an error naming the move",
        JSON.stringify(strayGraph.problems.map((p) => p.message))
    );
    check(!strayGraph.nodes.some((n) => n.path && n.path.startsWith(".design/")), "the stray folder is not indexed");
} finally {
    await rm(root, { recursive: true, force: true });
}

// -- bounded-context is a domain chapter type -------------------------------

const contextMap = `# Context Map\n\n${fence("index: root\ntype: context-map\n")}\n## Order Management\n\n${fence("type: bounded-context\n")}\nA context.\n`;
const issues = validateDocument(`${DEVBOOK_ROOT}/domain/context-map.md`, contextMap);
check(issues.filter((i) => i.severity === "error").length === 0, "bounded-context validates as a domain chapter type", JSON.stringify(issues));

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
