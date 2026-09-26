#!/usr/bin/env node
// build.mjs — devbook's checker; a layered plugin's refresh runs it with --write.
//
//   node .devbook/_tools/devbook-meta/build.mjs           # check every adopted scope, write nothing
//   node .devbook/_tools/devbook-meta/build.mjs --check   # the same, spelled out; CI runs this
//   node .devbook/_tools/devbook-meta/build.mjs --print   # check, and emit the documents as JSON on stdout
//   node .devbook/_tools/devbook-meta/build.mjs --write   # check, and write the derived _meta/ artifacts
//   node .devbook/_tools/devbook-meta/build.mjs --scope tech     # .tech and .devbook/tech spell the same scope
//   node .devbook/_tools/devbook-meta/build.mjs --root ../other-repo
//
// Checking is this tool's own job and the default. Writing is opt-in, because
// the committed artifacts are a layered plugin's product: its refresh script and
// nightly workflow pass --write, and nothing in devbook ever does (record 81).
// With --write it writes three artifacts per scope, per the derived-artifacts
// convention:
//
//   .devbook/_meta/graph.json          the reference graph (repository-wide rollup)
//   .devbook/_meta/index.json          the ordered reading outline
//   .devbook/_meta/annotations.json    the open-note index, from the annotation fences
//   .devbook/tech/_meta/graph.json     the same set, scoped to .tech
//   ...one set per devbook folder the repository actually has
//
// Only folders present under .devbook/ produce a scope, so a repository that
// adopts just .domain and .arc42 never grows _meta folders for the rest. A
// dot-folder at the repository root is reported as an error and not indexed.
//
// Graph construction lives in graph.mjs, which the devbook-graph canvas also
// imports, so the written indexes and the live view are always the same graph.
// `--print` is for a viewer that can spawn Node but not import these modules:
// it writes nothing and emits `{ folders, scopes: { "<scope>": { graph, outline,
// annotations } } }` on stdout, with the usual diagnostics on stderr.

import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import {
    buildGraph,
    buildGraphDocument,
    outputPathFor,
    discoverLayout,
    resolveScope,
    DEVBOOK_FOLDERS,
    REPO_SCOPE,
} from "./graph.mjs";
import { buildOutlineDocument, outlinePathFor } from "./outline.mjs";
import {
    buildAnnotationsDocument,
    annotationsPathFor,
    collectAnnotations,
} from "./annotations-index.mjs";

const args = process.argv.slice(2);
const printMode = args.includes("--print");
const writeMode = args.includes("--write") && !printMode;
const checkOnly = !writeMode;
// Diagnostics go to stderr in print mode so stdout stays one parseable document.
const log = printMode ? console.error : console.log;

function optionValue(name) {
    const index = args.indexOf(name);
    return index !== -1 ? args[index + 1] : null;
}

// Defaults to the working directory, which is the repository root in CI and in
// the documented usage above. `--root` keeps the generator usable from anywhere.
const REPO_ROOT = path.resolve(optionValue("--root") ?? process.cwd());
const requestedScope = optionValue("--scope") ? resolveScope(optionValue("--scope")) ?? optionValue("--scope") : null;

const layout = await discoverLayout(REPO_ROOT);
const availableScopes = layout.folders.length ? [REPO_SCOPE, ...layout.folders] : [];

if (!availableScopes.length) {
    console.error(
        `No devbook folders found under ${REPO_ROOT}. ` +
            `Expected at least one of: ${DEVBOOK_FOLDERS.join(", ")}.` +
            (layout.stray.length
                ? ` Found ${layout.stray.join(", ")} at the repository root: only the .devbook/ layout is supported, so move them under it.`
                : "")
    );
    process.exit(2);
}

if (requestedScope && !availableScopes.includes(requestedScope)) {
    console.error(
        `Unknown or unadopted scope "${requestedScope}". ` +
            `Available scopes: ${availableScopes.join(", ")}.`
    );
    process.exit(2);
}

const scopes = requestedScope ? [requestedScope] : availableScopes;
const folders = availableScopes.filter((scope) => scope !== REPO_SCOPE);

log(`folders: ${folders.join(", ")}`);

// Parse the corpus once and project it per scope.
const graph = await buildGraph(REPO_ROOT, folders);
const annotations = await collectAnnotations(REPO_ROOT, folders);
let errorCount = 0;
const printed = { folders, scopes: {} };

async function emit(outPath, document, summary) {
    if (!checkOnly) {
        const absoluteOut = path.resolve(REPO_ROOT, outPath);
        await mkdir(path.dirname(absoluteOut), { recursive: true });
        await writeFile(absoluteOut, `${JSON.stringify(document, null, 2)}\n`, "utf8");
    }
    log(`${checkOnly ? "checked" : "wrote  "} ${outPath.padEnd(26)} ${summary}`);
    for (const problem of document.problems) {
        log(`  [${problem.severity}] ${problem.message}`);
        if (problem.severity === "error") errorCount++;
    }
}

function countFiles(entries) {
    return entries.reduce(
        (acc, entry) => acc + (entry.type === "file" ? 1 : countFiles(entry.children ?? [])),
        0
    );
}

for (const scope of scopes) {
    const graphDocument = await buildGraphDocument(REPO_ROOT, scope, graph, folders);
    const { stats } = graphDocument;
    await emit(
        outputPathFor(scope),
        graphDocument,
        `${String(stats.nodes).padStart(4)} nodes, ${String(stats.edges).padStart(4)} edges`
    );

    const outlineDocument = await buildOutlineDocument(REPO_ROOT, scope, folders);
    await emit(
        outlinePathFor(scope),
        outlineDocument,
        `${String(countFiles(outlineDocument.entries)).padStart(4)} files ordered`
    );

    const annotationsDocument = await buildAnnotationsDocument(
        REPO_ROOT,
        scope,
        annotations,
        folders
    );
    await emit(
        annotationsPathFor(scope),
        annotationsDocument,
        `${String(annotationsDocument.stats.threads).padStart(4)} threads, ` +
            `${String(annotationsDocument.stats.open).padStart(4)} open`
    );

    printed.scopes[scope] = { graph: graphDocument, outline: outlineDocument, annotations: annotationsDocument };
}

if (printMode) process.stdout.write(`${JSON.stringify(printed, null, 2)}\n`);

if (errorCount) {
    console.error(`\n${errorCount} problem(s) at error severity.`);
    process.exit(1);
}
