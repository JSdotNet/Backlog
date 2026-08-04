// Extension: knowledge-canvas
//
// Tailored canvas for this repository's checked-in knowledge folders
// (.domain/, .arc42/, .backlog/). Renders the Markdown with its embedded
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
import { parseDocument, validateDocument, folderKindForPath } from "./metadata.mjs";

// Repository root: the CLI launches project-scoped extensions with cwd set
// to the git root, which is also where .domain/.arc42/.backlog live.
const REPO_ROOT = process.cwd();

// One local HTTP server + current document path per open canvas instance.
const instances = new Map();

function resolveRelPath(relPath) {
    const normalized = relPath.replace(/\\/g, "/").replace(/^\/+/, "");
    const kind = folderKindForPath(normalized);
    if (!kind) {
        throw new Error(
            `"${relPath}" is not under .domain/, .arc42/, or .backlog/ — this canvas only serves those folders.`
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

const session = await joinSession({
    canvases: [
        createCanvas({
            id: "knowledge-canvas",
            displayName: "Knowledge canvas",
            description:
                "View .domain/.arc42/.backlog Markdown with rendered Mermaid diagrams and a structured metadata/lint side panel, per chapter-metadata.instructions.md.",
            inputSchema: {
                type: "object",
                properties: {
                    path: {
                        type: "string",
                        description:
                            "Repo-relative path to a Markdown file under .domain/, .arc42/, or .backlog/ to open immediately.",
                    },
                },
            },
            actions: [
                {
                    name: "set_document",
                    description:
                        "Switch the canvas to display a different Markdown file under .domain/, .arc42/, or .backlog/.",
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
