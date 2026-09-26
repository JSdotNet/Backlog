// annotations-index.mjs — derives the open-note index from the annotation
// fences in the Markdown.
//
// Markdown stays canonical, annotations included. This is the *derived* half:
// the index a reader comes off so no reader needs the writer and no reader
// parses Markdown twice. It serves the reads that span chapters — the
// cross-repository inbox, the open-note count on a graph node, the review
// queue — and none of them couples to `annotations.mjs`. The approval gate is
// deliberately not among them: it shows one chapter and reads that chapter,
// because a note written on the branch since the last refresh is exactly the
// one this file lacks.
//
// Deleting it costs nothing: it is a deterministic function of the chapters,
// so re-running the generator reproduces it byte for byte.

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { parseAnnotations, resolveAnnotation, folderKindForPath } from "./metadata.mjs";
import { discoverLayout, REPO_SCOPE, SCHEMA_VERSION, generatorPath, DEVBOOK_ROOT } from "./graph.mjs";

/** Recursively collect Markdown files under a folder, as repo-relative posix paths. */
async function collectMarkdown(repoRoot, relFolder) {
    const results = [];
    async function walk(current) {
        let entries;
        try {
            entries = await readdir(path.join(repoRoot, current), { withFileTypes: true });
        } catch {
            return; // folder not adopted by this repository
        }
        for (const entry of entries) {
            const child = `${current}/${entry.name}`;
            // `_meta/` is generated tool input; a note never lives there.
            if (entry.isDirectory()) {
                if (entry.name !== "_meta") await walk(child);
            } else if (entry.isFile() && entry.name.endsWith(".md")) {
                results.push(child);
            }
        }
    }
    await walk(relFolder);
    return results.sort();
}

/**
 * Every annotation thread in the corpus, in document order.
 *
 * A thread is addressed the way devbook addresses everything else — a path
 * plus a heading slug — with its ordinal in the chapter standing in for the id
 * the schema deliberately does not assign.
 */
export async function collectAnnotations(repoRoot, folders = null) {
    folders ??= (await discoverLayout(repoRoot)).folders;
    const files = (
        await Promise.all(folders.map((folder) => collectMarkdown(repoRoot, folder)))
    ).flat();

    const threads = [];
    for (const relPath of files) {
        const markdown = await readFile(path.join(repoRoot, relPath), "utf8");
        for (const note of parseAnnotations(markdown)) {
            const fields = resolveAnnotation(note.fields);
            const slug = note.chapter?.slug ?? null;
            threads.push({
                path: relPath,
                folder: folderKindForPath(relPath),
                address: slug ? `${relPath}#${slug}` : relPath,
                chapter: slug,
                chapterTitle: note.chapter?.text ?? null,
                ordinal: note.ordinal,
                line: note.line,
                target: note.target,
                kind: fields.kind,
                status: fields.status,
                author: fields.author ?? null,
                date: fields.date ?? null,
                // Omitted rather than emitted empty, so a consumer never has to
                // tell "no quote" from "an empty quote".
                ...(fields.quote ? { quote: fields.quote } : {}),
                body: fields.body ?? null,
                ...(Array.isArray(fields.replies) && fields.replies.length
                    ? { replies: fields.replies }
                    : {}),
                ...(fields.ext && typeof fields.ext === "object" && !Array.isArray(fields.ext)
                    ? { ext: fields.ext }
                    : {}),
            });
        }
    }
    return threads;
}

/** Open-note count per chapter address — what a graph node badges itself with. */
export function openCountsByAddress(threads) {
    const counts = new Map();
    for (const thread of threads) {
        if (thread.status !== "open") continue;
        counts.set(thread.address, (counts.get(thread.address) ?? 0) + 1);
    }
    return counts;
}

function summarize(threads) {
    return {
        threads: threads.length,
        open: threads.filter((t) => t.status === "open").length,
        resolved: threads.filter((t) => t.status === "resolved").length,
        replies: threads.reduce((total, t) => total + (t.replies?.length ?? 0), 0),
        chapters: new Set(threads.map((t) => t.address)).size,
    };
}

/**
 * Build the serializable annotations document for one scope, following the
 * derived-artifacts convention.
 *
 * A `resolved` thread is still listed: it lives for the rest of the branch so
 * a reviewer sees the exchange in the pull request that raised it, and the
 * sweep is what removes it.
 */
export async function buildAnnotationsDocument(
    repoRoot,
    scope = REPO_SCOPE,
    prebuilt = null,
    folders = null
) {
    folders ??= (await discoverLayout(repoRoot)).folders;
    const all = prebuilt ?? (await collectAnnotations(repoRoot, folders));
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const threads = all.filter((thread) => roots.some((root) => thread.path.startsWith(`${root}/`)));

    return {
        schemaVersion: SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: roots,
        // Deliberately no timestamp: the index is a deterministic function of
        // the Markdown, so re-running it produces a byte-identical file and CI
        // can diff it to detect a stale commit.
        stats: summarize(threads),
        problems: [],
        threads,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function annotationsPathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/annotations.json` : `${scope}/_meta/annotations.json`;
}
