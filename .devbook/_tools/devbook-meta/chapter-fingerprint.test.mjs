// The fingerprint against the shared vector, `chapter-fingerprint.vector.json`:
// every case's canonical text and fingerprint, in-process and through the
// `chapter-hash.mjs` CLI over the case written to disk. spec-manager and
// Backlog run the same vector, per `resources/chapter-fingerprint.md`.
//
// Run: `node --test chapter-fingerprint.test.mjs`
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, readFile, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { changeHash, changePathParts, changeText, chapterFingerprint, chapterFingerprintText, parseDocument, slugify } from "./metadata.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const vector = JSON.parse(await readFile(path.join(here, "chapter-fingerprint.vector.json"), "utf8"));
const digest = (text) => `sha256:${createHash("sha256").update(text, "utf8").digest("hex").slice(0, 8)}`;
const named = (list, name) => list.find((other) => other.name === name);

/** The heading line an address names: its slug's chapter, else the file's `#`. */
const lineOf = (markdown, address) => {
    const slug = address.includes("#") ? address.slice(address.indexOf("#") + 1) : null;
    const { chapters } = parseDocument(markdown);
    return (slug ? chapters.find((c) => c.slug === slugify(slug)) : chapters.find((c) => c.level === 1)).line;
};

const deltasOf = (sample) => sample.deltas.map((d) => ({ target: changePathParts(d.path).target, markdown: d.markdown }));

/** Runs `chapter-hash.mjs <address>` in a scratch repository holding `files`. */
async function cli(files, address) {
    const root = await mkdtemp(path.join(tmpdir(), "chapter-fingerprint-"));
    try {
        for (const [rel, text] of Object.entries(files)) {
            await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
            await writeFile(path.join(root, rel), text);
        }
        const run = spawnSync(process.execPath, [path.join(here, "chapter-hash.mjs"), address], { cwd: root, encoding: "utf8" });
        assert.equal(run.status, 0, run.stderr);
        return run.stdout.trim();
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

for (const sample of vector.blocks) {
    test(`vector block: ${sample.name}`, async () => {
        const demoText = (p) => sample.demos?.[p] ?? null;
        const line = lineOf(sample.markdown, sample.address);
        assert.equal(chapterFingerprintText(sample.path, sample.markdown, line, demoText), sample.canonical);
        assert.equal(chapterFingerprint(sample.path, sample.markdown, line, demoText), sample.fingerprint);
        assert.equal(digest(sample.canonical), sample.fingerprint);
        assert.equal(await cli({ [sample.path]: sample.markdown, ...sample.demos }, sample.address), sample.fingerprint);
        if (sample.sameAs) assert.equal(sample.fingerprint, named(vector.blocks, sample.sameAs).fingerprint);
        if (sample.differsFrom) assert.notEqual(sample.fingerprint, named(vector.blocks, sample.differsFrom).fingerprint);
    });
}

for (const sample of vector.changes) {
    test(`vector change: ${sample.name}`, async () => {
        assert.equal(changeText(sample.proposal, deltasOf(sample)), sample.canonical);
        assert.equal(changeHash(sample.proposal, deltasOf(sample)), sample.fingerprint);
        assert.equal(digest(sample.canonical), sample.fingerprint);
        const files = { [`${sample.address}/proposal.md`]: sample.proposal, ...Object.fromEntries(sample.deltas.map((d) => [d.path, d.markdown])) };
        assert.equal(await cli(files, sample.address), sample.fingerprint);
        assert.equal(await cli(files, `${sample.address}/proposal.md`), sample.fingerprint);
        if (sample.sameAs) assert.equal(sample.fingerprint, named(vector.changes, sample.sameAs).fingerprint);
        if (sample.differsFrom) assert.notEqual(sample.fingerprint, named(vector.changes, sample.differsFrom).fingerprint);
    });
}

test("the vector covers both fence kinds dropped, a kept fence, CRLF, nesting, demos, and a change", () => {
    const all = [...vector.blocks, ...vector.changes];
    assert.ok(vector.blocks.some((b) => b.markdown.includes("```annotation") && b.markdown.includes("```meta")));
    assert.ok(vector.blocks.some((b) => b.markdown.includes("\r\n")));
    assert.ok(vector.blocks.some((b) => b.canonical.includes("~~~")));
    assert.ok(vector.blocks.some((b) => Object.keys(b.demos ?? {}).length > 1));
    assert.ok(vector.changes.some((c) => c.deltas.some((d) => d.path.endsWith(".demo.html"))));
    assert.ok(all.some((x) => x.sameAs) && all.some((x) => x.differsFrom));
    for (const x of all) assert.match(x.fingerprint, /^sha256:[0-9a-f]{8}$/);
});
