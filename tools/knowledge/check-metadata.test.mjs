// Tests for check-metadata.mjs, run with `node --test tools/knowledge`.
//
// Two kinds of case. The first reads this repository's own corpus and asserts
// the gate is green on it, because a gate that is red the day it lands gets
// switched off rather than obeyed. The rest build a throwaway knowledge folder
// in a temp directory and assert the gate is red for exactly one reason at a
// time, which is the only way to tell "catches a bad status" apart from
// "catches everything".

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { checkRepository, formatReport, KNOWLEDGE_FOLDERS } from './check-metadata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..', '..');
const SCRIPT = join(HERE, 'check-metadata.mjs');

/** A fenced ```meta block, built from `key: value` pairs. */
function meta(fields) {
    const body = Object.entries(fields).map(([key, value]) => `${key}: ${value}`).join('\n');
    return ['```meta', body, '```'].join('\n');
}

/** A document with a valid file-level block and one chapter carrying `fields`. */
function chapter(fileFields, chapterFields) {
    return [
        '# Fixture',
        '',
        meta(fileFields),
        '',
        '## A chapter',
        '',
        meta(chapterFields),
        ''
    ].join('\n');
}

/**
 * Run the check over a throwaway repository holding a single document, and
 * return its result. `relPath` decides which folder rules apply, so it has to
 * start with one of the knowledge folders.
 */
async function checkFixture(relPath, markdown) {
    const root = await mkdtemp(join(tmpdir(), 'knowledge-check-'));
    try {
        const file = join(root, ...relPath.split('/'));
        await mkdir(dirname(file), { recursive: true });
        await writeFile(file, markdown, 'utf8');
        return await checkRepository(root);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

/** The blocking findings as one string, for a failure message or a match. */
function blockingText(result) {
    return result.blocking.map((finding) => `${finding.path}: ${finding.message}`).join('\n');
}

test('the repository corpus passes', async () => {
    const result = await checkRepository(REPO);

    assert.equal(
        result.blocking.length,
        0,
        `The knowledge corpus has blocking metadata findings:\n${blockingText(result)}`
    );
    assert.ok(result.files > 0, 'The check scanned no files at all, so it is passing on nothing.');
    assert.ok(
        result.folders.every((folder) => folder.files > 0),
        'A knowledge folder was discovered but scanned no files, so the walker is skipping something.'
    );
    assert.deepEqual(
        result.folders.map((folder) => folder.folder).sort(),
        [...KNOWLEDGE_FOLDERS].sort(),
        'This repository has adopted all five knowledge folders, so all five have to be scanned. '
        + 'A gate that quietly covers four of them is the defect issue #241 is about.'
    );
});

test('a field the installed generator predates is not a failure', async () => {
    // The gate is pinned to the copy of the generator under
    // `.github/tools/knowledge-meta/`, which is four plugin releases behind.
    // 0.16.0 allows `type`, `date` and `tests` on any block and `index` on a
    // file-level one; chapter authors are told to write that schema, because the
    // instructions and skills that describe it come from the plugin rather than
    // from here. Blocking on them would fail a pull request for correct metadata.
    const result = await checkFixture(
        '.design/sample.md',
        chapter(
            { status: 'active', index: 'root' },
            { status: 'active', type: 'component', date: '2026-09-05', tests: 'unit:xunit:Foo' }
        )
    );

    assert.equal(result.blocking.length, 0, blockingText(result));
    assert.ok(result.suppressed > 0, 'The newer fields should be suppressed, not silently advisory.');

    // The exemption is those field names, not the whole class: a field no schema
    // at any version defines still blocks.
    const unknown = await checkFixture(
        '.design/sample.md',
        chapter({ status: 'active' }, { status: 'active', owner: 'nobody' })
    );
    assert.equal(unknown.blocking.length, 1, blockingText(unknown));
    assert.match(unknown.blocking[0].message, /unrecognized field `owner`/);
});

test('an adopted folder that holds nothing is an error, not a silent pass', async () => {
    // `result.folders` carries every folder present on disk, empty included, so
    // the corpus assertion above can actually fail. Dropping empty entries is
    // what would let a renamed or unreadable folder go quietly ungated.
    const root = await mkdtemp(join(tmpdir(), 'knowledge-check-'));
    try {
        await mkdir(join(root, '.domain', 'sample'), { recursive: true });
        await writeFile(
            join(root, '.domain', 'sample', 'features.md'),
            chapter({ status: 'active', type: 'features' }, { status: 'active', type: 'feature' }),
            'utf8'
        );
        await mkdir(join(root, '.design'), { recursive: true });

        const result = await checkRepository(root);
        assert.deepEqual(result.folders.map((folder) => folder.folder), ['.domain', '.design']);
        assert.equal(result.folders.find((folder) => folder.folder === '.design').files, 0);

        const run = spawnSync(process.execPath, [SCRIPT, '--root', root], { encoding: 'utf8' });
        assert.equal(run.status, 2, `${run.stdout}${run.stderr}`);
        assert.match(`${run.stdout}${run.stderr}`, /exists but holds no Markdown/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a status outside the folder vocabulary blocks, naming the value and the list', async () => {
    const result = await checkFixture(
        '.domain/sample/features.md',
        chapter({ status: 'active', type: 'features' }, { status: 'idea', type: 'feature' })
    );

    assert.equal(result.blocking.length, 1, blockingText(result));
    const [finding] = result.blocking;
    assert.equal(finding.path, '.domain/sample/features.md');
    assert.match(finding.message, /## A chapter/);
    assert.match(finding.message, /[(]line 8[)]/);
    assert.match(finding.message, /"idea"/);
    assert.match(finding.message, /draft, proposed, active, deprecated/);
});

test('each folder is judged against its own ladder', async () => {
    // `adopted` is a real status in `.tech`, and `active` is a real status in
    // `.domain`. Borrowing another folder vocabulary is the mistake that one
    // shared list would wave through.
    const design = await checkFixture(
        '.design/sample.md',
        chapter({ status: 'active' }, { status: 'adopted' })
    );
    assert.equal(design.blocking.length, 1, blockingText(design));
    assert.match(design.blocking[0].message, /"adopted"/);
    assert.match(design.blocking[0].message, /draft, active, deprecated/);

    const backlog = await checkFixture(
        '.backlog/sample.md',
        chapter({ status: 'draft' }, { status: 'active' })
    );
    assert.equal(backlog.blocking.length, 1, blockingText(backlog));
    assert.match(backlog.blocking[0].message, /"active"/);
    assert.match(backlog.blocking[0].message, /draft, ready, in-progress, done, blocked/);
});

test('a bad type value blocks', async () => {
    const result = await checkFixture(
        '.domain/sample/model.md',
        chapter({ status: 'active', type: 'model' }, { status: 'active', type: 'widget' })
    );

    assert.equal(result.blocking.length, 1, blockingText(result));
    assert.match(result.blocking[0].message, /type "widget"/);
});

test('an unrecognized field blocks', async () => {
    // `validateDocument` reports this at warning severity. It is promoted here
    // because a field the schema does not know is the same class of defect as a
    // value the schema does not know, and that class is what this gate is for.
    const result = await checkFixture(
        '.design/sample.md',
        chapter({ status: 'active' }, { status: 'active', owner: 'nobody' })
    );

    assert.equal(result.blocking.length, 1, blockingText(result));
    assert.match(result.blocking[0].message, /unrecognized field `owner`/);
});

test('the stale tech field rename is suppressed, and only it', async () => {
    // Every `.tech` chapter in this repository authors `type:`, which the
    // installed generator predates: it still requires the pre-rename `kind` and
    // does not recognise `type`. Both halves have to go — the missing-`kind`
    // error by the `.tech` rule, the unrecognized `type` by the pending-re-sync
    // field list — or the gate is red on 89 chapters the day it lands.
    const current = await checkFixture(
        '.tech/sample.md',
        chapter({ status: 'adopted' }, { status: 'adopted', type: 'tool' })
    );
    assert.equal(current.blocking.length, 0, blockingText(current));
    assert.equal(
        current.suppressed,
        2,
        'Expected the missing-`kind` and unrecognized-`type` pair, and nothing else.'
    );

    // The suppression is those two messages rather than the folder: a bad
    // status and a genuinely unknown field in `.tech` still block.
    const bad = await checkFixture(
        '.tech/sample.md',
        chapter({ status: 'adopted' }, { status: 'active', type: 'tool', owner: 'nobody' })
    );
    assert.equal(bad.blocking.length, 2, blockingText(bad));
    assert.match(blockingText(bad), /status "active"/);
    assert.match(blockingText(bad), /unrecognized field `owner`/);
});

test('the report names every folder it scanned and what it found', async () => {
    const result = await checkRepository(REPO);
    const report = formatReport(result);

    for (const folder of result.folders) {
        assert.ok(
            report.includes(folder.folder),
            `The report does not name ${folder.folder}:\n${report}`
        );
    }
    assert.match(report, /files/);
    assert.match(report, /blocking/i);
});

test('the command exits 0 on this repository and 1 on a violation', async () => {
    const clean = spawnSync(process.execPath, [SCRIPT, '--root', REPO], { encoding: 'utf8' });
    assert.equal(clean.status, 0, `${clean.stdout}${clean.stderr}`);

    const root = await mkdtemp(join(tmpdir(), 'knowledge-check-'));
    try {
        await mkdir(join(root, '.domain', 'sample'), { recursive: true });
        await writeFile(
            join(root, '.domain', 'sample', 'features.md'),
            chapter({ status: 'active', type: 'features' }, { status: 'idea', type: 'feature' }),
            'utf8'
        );

        const dirty = spawnSync(process.execPath, [SCRIPT, '--root', root], { encoding: 'utf8' });
        assert.equal(dirty.status, 1, `${dirty.stdout}${dirty.stderr}`);
        assert.match(`${dirty.stdout}${dirty.stderr}`, /"idea"/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});

test('a root with no knowledge folders is an error, not a pass', async () => {
    // The one way to make this gate green by accident is to point it somewhere
    // that has nothing to check.
    const root = await mkdtemp(join(tmpdir(), 'knowledge-check-'));
    try {
        const empty = spawnSync(process.execPath, [SCRIPT, '--root', root], { encoding: 'utf8' });
        assert.equal(empty.status, 2, `${empty.stdout}${empty.stderr}`);
        assert.match(`${empty.stdout}${empty.stderr}`, /No knowledge folders found/);
    } finally {
        await rm(root, { recursive: true, force: true });
    }
});
