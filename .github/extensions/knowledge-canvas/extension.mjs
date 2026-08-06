// Extension: knowledge-canvas
//
// Tailored canvas for this repository's checked-in knowledge folders
// (.domain/, .arc42/, .backlog/, .tech/, .design/). Renders the Markdown with its embedded
// Mermaid diagrams, and parses each chapter/file's `meta` fenced-YAML block
// (per .github/instructions/chapter-metadata.instructions.md) into a
// structured side panel plus a lightweight metadata lint.
//
// Kept intentionally self-contained: rendering is client-side via
// CDN-hosted `marked`/`mermaid` (see render.mjs); metadata parsing/lint is
// hand-written in metadata.mjs to avoid a YAML dependency for this small,
// fixed schema.

import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { joinSession, createCanvas } from "@github/copilot-sdk/extension";
import { renderPage } from "./render.mjs";
import { renderGraphPage } from "./graph-render.mjs";
import { buildGraph, buildGraphDocument, SCOPES, REPO_SCOPE } from "../../tools/knowledge-meta/graph.mjs";
import { buildOutlineDocument } from "../../tools/knowledge-meta/outline.mjs";
import { parseDocument, validateDocument, folderKindForPath } from "./metadata.mjs";

// Repository root: the CLI launches project-scoped extensions with cwd set
// to the git root, which is also where .domain/.arc42/.backlog/.tech/.design live.
const REPO_ROOT = process.cwd();

// One local HTTP server + current document path per open canvas instance.
const instances = new Map();
// Same, for knowledge-graph canvas instances.
const graphInstances = new Map();

function resolveRelPath(relPath) {
    const normalized = relPath.replace(/\\/g, "/").replace(/^\/+/, "");
    const kind = folderKindForPath(normalized);
    if (!kind) {
        throw new Error(
            `"${relPath}" is not under .domain/, .arc42/, .backlog/, .tech/, or .design/ — this canvas only serves those folders.`
        );
    }
    const absolute = path.resolve(REPO_ROOT, normalized);
    if (!absolute.startsWith(REPO_ROOT)) {
        throw new Error(`"${relPath}" escapes the repository root.`);
    }
    return { relative: normalized, absolute };
}

async function buildDocumentPayload(state) {
    if (!state.relPath) return null;
    const raw = await readFile(state.absolutePath, "utf8");
    const { fileTitle, fileMeta, chapters } = parseDocument(raw);
    const issues = validateDocument(state.relPath, raw);
    return { path: state.relPath, raw, fileTitle, fileMeta, chapters, issues };
}

async function startServer(instanceId) {
    const state = { relPath: null, absolutePath: null };

    const server = createServer(async (req, res) => {
        try {
            if (req.url === "/" || req.url?.startsWith("/?")) {
                res.setHeader("Content-Type", "text/html; charset=utf-8");
                res.end(renderPage());
                return;
            }
            if (req.url === "/api/document") {
                const payload = await buildDocumentPayload(state);
                if (!payload) {
                    res.statusCode = 404;
                    res.setHeader("Content-Type", "application/json; charset=utf-8");
                    res.end(JSON.stringify({ error: "no document open" }));
                    return;
                }
                res.setHeader("Content-Type", "application/json; charset=utf-8");
                res.end(JSON.stringify(payload));
                return;
            }
            res.statusCode = 404;
            res.end("Not found");
        } catch (err) {
            res.statusCode = 500;
            res.setHeader("Content-Type", "application/json; charset=utf-8");
            res.end(JSON.stringify({ error: String(err?.message ?? err) }));
        }
    });

    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    return { server, url: `http://127.0.0.1:${port}/`, state };
}

function setDocument(entry, relPath) {
    const { relative, absolute } = resolveRelPath(relPath);
    entry.state.relPath = relative;
    entry.state.absolutePath = absolute;
}

/**
 * Local server for a knowledge-graph canvas instance.
 *
 * The graph is rebuilt from the Markdown on disk rather than read from the
 * committed `_meta/` artifacts, so the view can never show a stale index. The
 * parsed corpus is cached per instance and projected per requested scope, so
 * switching scope in the UI costs no disk I/O.
 */
async function startGraphServer(defaultScope) {
    const entry = { graph: null, scope: defaultScope };

    const server = createServer(async (req, res) => {
        try {
            const url = new URL(req.url ?? "/", "http://127.0.0.1");
            if (url.pathname === "/") {
                res.setHeader("Content-Type", "text/html; charset=utf-8");
                res.end(renderGraphPage({ scopes: SCOPES, scope: entry.scope }));
                return;
            }
            if (url.pathname === "/api/graph") {
                const requested = url.searchParams.get("scope") ?? entry.scope;
                const scope = SCOPES.includes(requested) ? requested : entry.scope;
                if (!entry.graph) entry.graph = await buildGraph(REPO_ROOT);
                const document = await buildGraphDocument(REPO_ROOT, scope, entry.graph);
                res.setHeader("Content-Type", "application/json; charset=utf-8");
                res.end(JSON.stringify(document));
                return;
            }
            if (url.pathname === "/api/outline") {
                const requested = url.searchParams.get("scope") ?? entry.scope;
                const scope = SCOPES.includes(requested) ? requested : entry.scope;
                res.setHeader("Content-Type", "application/json; charset=utf-8");
                res.end(JSON.stringify(await buildOutlineDocument(REPO_ROOT, scope)));
                return;
            }
            res.statusCode = 404;
            res.end("Not found");
        } catch (err) {
            res.statusCode = 500;
            res.setHeader("Content-Type", "application/json; charset=utf-8");
            res.end(JSON.stringify({ error: String(err?.message ?? err) }));
        }
    });

    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    entry.server = server;
    entry.url = `http://127.0.0.1:${port}/`;
    return entry;
}

function resolveScope(value) {
    if (!value) return REPO_SCOPE;
    const normalized = String(value).replace(/\\/g, "/").replace(/\/+$/, "");
    if (SCOPES.includes(normalized)) return normalized;
    throw new Error(`Unknown scope "${value}". Known scopes: ${SCOPES.join(", ")}`);
}

const session = await joinSession({
    canvases: [
        createCanvas({
            id: "knowledge-graph",
            displayName: "Knowledge graph",
            description:
                "Obsidian-style force-directed view of the knowledge graph derived from the `meta` blocks in .arc42/.domain/.backlog/.tech. Open it scoped to one folder (e.g. .tech) or repository-wide, with folder colouring, status shading, filters, and neighbourhood inspection.",
            inputSchema: {
                type: "object",
                properties: {
                    scope: {
                        type: "string",
                        enum: SCOPES,
                        description:
                            'Which graph to show: a knowledge folder such as ".tech", or "." for the repository-wide rollup. Defaults to ".".',
                    },
                },
            },
            actions: [
                {
                    name: "refresh_graph",
                    description:
                        "Re-read the Markdown from disk and rebuild the graph. Reload the canvas afterwards to see the new graph.",
                    handler: async (ctx) => {
                        const entry = graphInstances.get(ctx.instanceId);
                        if (!entry) throw new Error("Canvas instance not open.");
                        entry.graph = await buildGraph(REPO_ROOT);
                        const document = await buildGraphDocument(REPO_ROOT, entry.scope, entry.graph);
                        return { scope: entry.scope, stats: document.stats, problems: document.problems };
                    },
                },
                {
                    name: "set_scope",
                    description:
                        'Switch the canvas to a different graph scope (a knowledge folder such as ".tech", or "." for repository-wide). Reload the canvas afterwards.',
                    inputSchema: {
                        type: "object",
                        properties: {
                            scope: { type: "string", enum: SCOPES, description: "The scope to display." },
                        },
                        required: ["scope"],
                    },
                    handler: async (ctx) => {
                        const entry = graphInstances.get(ctx.instanceId);
                        if (!entry) throw new Error("Canvas instance not open.");
                        entry.scope = resolveScope(ctx.input?.scope);
                        return { ok: true, scope: entry.scope };
                    },
                },
            ],
            open: async (ctx) => {
                const scope = resolveScope(ctx.input?.scope);
                let entry = graphInstances.get(ctx.instanceId);
                if (!entry) {
                    entry = await startGraphServer(scope);
                    graphInstances.set(ctx.instanceId, entry);
                }
                entry.scope = scope;
                entry.graph = await buildGraph(REPO_ROOT);
                return {
                    title: scope === REPO_SCOPE ? "Knowledge graph" : `Knowledge graph: ${scope}`,
                    url: entry.url,
                };
            },
            onClose: async (ctx) => {
                const entry = graphInstances.get(ctx.instanceId);
                if (entry) {
                    graphInstances.delete(ctx.instanceId);
                    await new Promise((resolve) => entry.server.close(() => resolve()));
                }
            },
        }),
        createCanvas({
            id: "knowledge-canvas",
            displayName: "Knowledge canvas",
            description:
                "View .domain/.arc42/.backlog/.tech/.design Markdown with rendered Mermaid diagrams and a structured metadata/lint side panel, per chapter-metadata.instructions.md.",
            inputSchema: {
                type: "object",
                properties: {
                    path: {
                        type: "string",
                        description:
                            "Repo-relative path to a Markdown file under .domain/, .arc42/, .backlog/, .tech/, or .design/ to open immediately.",
                    },
                },
            },
            actions: [
                {
                    name: "set_document",
                    description:
                        "Switch the canvas to display a different Markdown file under .domain/, .arc42/, .backlog/, .tech/, or .design/.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            path: {
                                type: "string",
                                description: "Repo-relative path to the Markdown file to display.",
                            },
                        },
                        required: ["path"],
                    },
                    handler: async (ctx) => {
                        const entry = instances.get(ctx.instanceId);
                        if (!entry) throw new Error("Canvas instance not open.");
                        setDocument(entry, String(ctx.input?.path ?? ""));
                        return { ok: true, path: entry.state.relPath };
                    },
                },
                {
                    name: "validate_metadata",
                    description:
                        "Lint the currently displayed document's chapter/file `meta` blocks against chapter-metadata.instructions.md and return the issue list (also shown in the side panel).",
                    handler: async (ctx) => {
                        const entry = instances.get(ctx.instanceId);
                        if (!entry || !entry.state.relPath) {
                            throw new Error("No document is open on this canvas instance.");
                        }
                        const raw = await readFile(entry.state.absolutePath, "utf8");
                        const issues = validateDocument(entry.state.relPath, raw);
                        return { path: entry.state.relPath, issues };
                    },
                },
            ],
            open: async (ctx) => {
                let entry = instances.get(ctx.instanceId);
                if (!entry) {
                    entry = await startServer(ctx.instanceId);
                    instances.set(ctx.instanceId, entry);
                }
                const requestedPath = ctx.input?.path;
                if (requestedPath) {
                    setDocument(entry, String(requestedPath));
                }
                return {
                    title: entry.state.relPath ? `Knowledge: ${entry.state.relPath}` : "Knowledge canvas",
                    url: entry.url,
                };
            },
            onClose: async (ctx) => {
                const entry = instances.get(ctx.instanceId);
                if (entry) {
                    instances.delete(ctx.instanceId);
                    await new Promise((resolve) => entry.server.close(() => resolve()));
                }
            },
        }),
    ],
});

