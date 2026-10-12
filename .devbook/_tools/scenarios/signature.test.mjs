// The signature against the shared vector, `scenario-page.vector.json`: every
// case's canonical text and signature, every data set's hash, the reading of a
// data set from disk, and the CLI that prints a page's signature.
//
// Run: `node --test signature.test.mjs`
import { test } from "node:test";
import { spawnSync } from "node:child_process";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseScenarioPage } from "./parse.mjs";
import { canonicalText, hashDataSet, hashFiles, MISSING_DATA, pageSignature, signatureOf } from "./signature.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const vector = JSON.parse(await readFile(path.join(here, "scenario-page.vector.json"), "utf8"));

for (const sample of vector.cases) {
    test(`vector: ${sample.name}`, () => {
        const page = parseScenarioPage(sample.markdown, sample.path);
        const dataHash = (name) => sample.dataHashes[name] ?? MISSING_DATA;
        assert.equal(canonicalText(page, dataHash), sample.canonical);
        assert.equal(signatureOf(page, dataHash), sample.signature);
        assert.match(sample.signature, /^[0-9a-f]{8}$/);
        if (sample.sameAs) assert.equal(sample.signature, vector.cases.find((other) => other.name === sample.sameAs).signature);
    });
}

for (const set of vector.dataSets) {
    test(`vector data set: ${set.name}`, () => {
        assert.equal(hashFiles(set.files), set.hash);
        if (set.sameAs) assert.equal(set.hash, vector.dataSets.find((other) => other.name === set.sameAs).hash);
    });
}

test("the vector covers the setup fields, a data set, portals, and the default profile", () => {
    const canon = vector.cases.map((sample) => sample.canonical).join("\n");
    for (const key of ["start:", "actor:", "data:", "profile: default", "flags:", "settings:", "part:", "step:", "shot:"]) assert.ok(canon.includes(key), key);
});

test("a step, a label, a part title, or a setup field changes the signature; a caption does not", () => {
    const base = vector.cases[0];
    const sign = (markdown) => signatureOf(parseScenarioPage(markdown, base.path), (name) => base.dataHashes[name] ?? MISSING_DATA);
    assert.equal(sign(base.markdown), base.signature);
    assert.equal(sign(base.markdown.replace("The board with three empty columns", "Another caption")), base.signature);
    for (const [from, to] of [
        ["I drag it to", "I move it to"],
        ["shot:item-moved", "shot:item-dragged"],
        ["## An item is moved", "## An item moves"],
        ["profile: tenant-acme", "profile: default"],
        ["-context.md#legacy-filters", "context.md#legacy-filters"],
    ]) {
        assert.notEqual(sign(base.markdown.replace(from, to)), base.signature, `${from} → ${to}`);
    }
});

test("a data set hashes from its folder, the same over CRLF, and reads as missing when absent", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-data-"));
    try {
        const set = vector.dataSets[0];
        const folder = path.join(root, ".devbook/scenarios/data/webshop-without-statuses");
        await mkdir(folder, { recursive: true });
        for (const [rel, content] of Object.entries(set.files)) await writeFile(path.join(folder, rel), content.replace(/\n/g, "\r\n"));
        assert.equal(await hashDataSet(folder), set.hash);
        assert.equal(await hashDataSet(path.join(root, "nope")), MISSING_DATA);

        const page = parseScenarioPage(vector.cases[0].markdown, vector.cases[0].path);
        const expected = signatureOf(page, (name) => (name === "webshop-without-statuses" ? set.hash : MISSING_DATA));
        assert.equal(await pageSignature(page, root, ".devbook/scenarios"), expected);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test("a binary data file is hashed as its bytes", () => {
    const bytes = Buffer.from([0, 13, 10, 255]);
    assert.notEqual(hashFiles({ "db.bak": bytes }), hashFiles({ "db.bak": Buffer.from([0, 10, 255]) }));
});

test("the CLI prints each page's signature and exits 1 on a path that is not a scenario page", async () => {
    const root = await mkdtemp(path.join(tmpdir(), "scenarios-cli-"));
    try {
        const sample = vector.cases[0];
        await mkdir(path.join(root, path.dirname(sample.path)), { recursive: true });
        await writeFile(path.join(root, sample.path), sample.markdown);
        await writeFile(path.join(root, ".devbook/domain/notes.md"), "# Notes\n");
        const cli = (...args) => spawnSync(process.execPath, [path.join(here, "signature.mjs"), "--root", root, ...args], { encoding: "utf8" });

        const expected = await pageSignature(parseScenarioPage(sample.markdown, sample.path), root, ".devbook/scenarios");
        const one = cli(sample.path);
        assert.equal(one.status, 0, one.stderr);
        assert.equal(one.stdout.trim(), `${expected}  ${sample.path}`);

        const mixed = cli(sample.path, ".devbook/domain/notes.md");
        assert.equal(mixed.status, 1);
        assert.match(mixed.stdout, new RegExp(expected));
        assert.match(mixed.stderr, /notes\.md/);

        assert.equal(cli().status, 2);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
