#!/usr/bin/env node
// Archify artifacts for knowledge chapter diagrams.
//
// The app finds an artifact for a diagram by hashing the mermaid fence it is about
// to draw and looking that hash up in the `_archify/index.json` beside the chapter.
// Nothing else links the two: an Archify artifact is a re-authoring rather than a
// conversion, so there is no way to derive one from the other and no way to notice
// drift except by recording what the artifact was authored from. This script owns
// that hash on the generation side; `DiagramSourceHash` in
// `src/Core/Backlog.UI.Components/Diagrams/DiagramArtifacts.cs` owns the identical
// rule on the reading side, and the two normalise the same way or the feature
// silently shows nothing.
//
//   node tools/diagrams/archify-artifacts.mjs scan [--json] [--missing]
//   node tools/diagrams/archify-artifacts.mjs render <spec.json> [more.json ...]
//   node tools/diagrams/archify-artifacts.mjs render --all
//   node tools/diagrams/archify-artifacts.mjs scaffold <chapter.md> <ordinal>
//   node tools/diagrams/archify-artifacts.mjs verify [--json]

import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync, mkdirSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve, relative, basename, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..', '..');
const ARCHIFY = join(REPO, 'tools', 'archify', 'bin', 'archify.mjs');

/** The knowledge folders `KnowledgeFolderSetting.Defaults()` names, which are the
 *  folders the app resolves chapters out of. A diagram anywhere else is not a
 *  knowledge chapter diagram and is left alone. */
const KNOWLEDGE_FOLDERS = ['.domain', '.arc42', '.tech', '.design'];

/** The artifact folder beside a chapter. Underscore-prefixed so it sorts away from
 *  the chapters and reads as machinery rather than as content — the knowledge
 *  providers enumerate `*.md` in the top directory only, so a subfolder is invisible
 *  to them either way. */
const ARTIFACT_DIR = '_archify';

const INDEX_FILE = 'index.json';

/** Which Archify type re-authors which mermaid diagram, and which has none.
 *
 *  `classDiagram` is the one that matters: twelve of this repository's mermaid
 *  blocks are class diagrams and every one of them is a bounded context's aggregate
 *  model. Archify's five types have no way to say "aggregate root", "value object"
 *  or "0..*", so there is nothing to generate and the app must not offer to. It is
 *  listed here with a null type rather than left out, so that "no Archify type fits
 *  this" is a recorded answer instead of a lookup miss. */
const TYPE_MAP = {
    flowchart: 'workflow',
    graph: 'workflow',
    sequencediagram: 'sequence',
    'statediagram-v2': 'lifecycle',
    statediagram: 'lifecycle',
    c4context: 'architecture',
    c4container: 'architecture',
    c4component: 'architecture',
    classdiagram: null,
    erdiagram: null,
    gantt: null,
    pie: null,
    journey: null,
    mindmap: null,
    timeline: null,
    quadrantchart: null,
    requirementdiagram: null,
    gitgraph: null,
    block: null,
    'block-beta': null,
    architecture: null,
    'architecture-beta': null
};

/** A flowchart that describes a state machine is a `lifecycle`, not a `workflow` —
 *  Archify types a diagram by what it means rather than by how its source was
 *  spelled, and the entry-lifecycle example in the storybook is exactly that case.
 *  The mapping above cannot tell them apart, so `TYPE_MAP` gives the default and an
 *  author overrides it by naming the type in the specification filename. Both are
 *  accepted for a `flowchart`; anything else is rejected. */
const FLOWCHART_TYPES = ['workflow', 'architecture', 'lifecycle', 'dataflow'];

const ALL_TYPES = ['architecture', 'workflow', 'sequence', 'dataflow', 'lifecycle'];

/** The Archify quality profile a specification is rendered at.
 *
 *  `showcase` for everything, with one recorded exception. Two diagrams in this
 *  repository are provably non-planar — `.domain/context-map.md` #1 and
 *  `.arc42/05-building-block-view.md` #3 each contain a K3,3 — so at least one
 *  edge crossing is forced in every possible drawing of them, and showcase raises
 *  `composition/proper-crossing` as an error. No faithful specification of either
 *  can ever pass showcase, so they are rendered at `standard` instead, which
 *  demotes crossings, corridors, label clearance and readability to warnings and
 *  keeps edge-through-node, endpoint-side-direction and label-overlap as errors.
 *  A standard render therefore still has to come back clean; it is only allowed
 *  to come back with warnings, and those are printed rather than swallowed. */
const SHOWCASE = 'showcase';

const STANDARD = 'standard';

/** A specification filename: `<chapter>.<ordinal>.<type>.json`, or
 *  `<chapter>.<ordinal>.<type>.standard.json` for the quality opt-out. Showcase is
 *  the default and is never written into a name, so there is exactly one spelling
 *  of the ordinary case. */
const SPEC_NAME = /^(.+)\.(\d+)\.([a-z]+)(?:\.([a-z]+))?\.json$/;

// ---------------------------------------------------------------------------
// The hash. Mirrored in C#; change both or neither.
// ---------------------------------------------------------------------------

/** Normalise a mermaid fence body to what is hashed: LF endings, no trailing
 *  whitespace on any line, no blank lines at either end.
 *
 *  Every one of those varies without changing the diagram — a markdown parser hands
 *  the reading side a body that may have lost its final newline, a Windows checkout
 *  hands the generating side CRLF, and an editor trims or does not trim. Hashing the
 *  raw bytes would make an artifact stop matching for reasons nobody edited. */
export function normalizeDiagramSource(source) {
    const lines = String(source ?? '').replace(/\r\n?/g, '\n').split('\n').map(line => line.replace(/[ \t]+$/, ''));
    while (lines.length > 0 && lines[0] === '') lines.shift();
    while (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
    return lines.join('\n');
}

export function diagramSourceHash(source) {
    return createHash('sha256').update(normalizeDiagramSource(source), 'utf8').digest('hex');
}

// ---------------------------------------------------------------------------
// Reading the chapters
// ---------------------------------------------------------------------------

function markdownFiles(root) {
    const found = [];
    const walk = dir => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory()) {
                if (entry.name === ARTIFACT_DIR || entry.name.startsWith('.')) continue;
                walk(join(dir, entry.name));
            } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) {
                found.push(join(dir, entry.name));
            }
        }
    };
    if (existsSync(root) && statSync(root).isDirectory()) walk(root);
    return found.sort();
}

/** The mermaid fences in one file, in document order, numbered from 1.
 *
 *  Ordinal rather than a heading or a caption because it is the only identifier a
 *  fence reliably has: mermaid blocks in this repository sit under headings that get
 *  renamed and carry no titles of their own. It is also why the hash and not the
 *  ordinal is what the app looks an artifact up by — an inserted diagram renumbers
 *  its neighbours, and a renumbered neighbour must not start showing someone else's
 *  picture. The ordinal is for a person to find the fence again. */
function mermaidFences(file) {
    const text = readFileSync(file, 'utf8').replace(/\r\n?/g, '\n');
    const lines = text.split('\n');
    const fences = [];
    let open = null;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const fence = /^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)/.exec(line);
        if (open === null) {
            if (!fence) continue;
            open = { marker: fence[2][0], length: fence[2].length, indent: fence[1].length, info: fence[3].toLowerCase(), start: i, body: [] };
            continue;
        }

        const closes = fence && fence[2][0] === open.marker && fence[2].length >= open.length && fence[3] === '';
        if (closes) {
            if (open.info === 'mermaid' || open.info === 'mmd') {
                const source = open.body.map(l => l.slice(Math.min(open.indent, l.length - l.trimStart().length))).join('\n');
                fences.push({ ordinal: fences.length + 1, line: open.start + 1, source });
            }
            open = null;
            continue;
        }
        open.body.push(line);
    }

    return fences;
}

/** The mermaid diagram keyword a fence opens with, lowercased, with `%%` comments,
 *  `%%{init}%%` directives and blank lines skipped. */
function mermaidKind(source) {
    for (const raw of normalizeDiagramSource(source).split('\n')) {
        const line = raw.trim();
        if (line === '' || line.startsWith('%%')) continue;
        const word = /^[A-Za-z0-9_-]+/.exec(line);
        return word ? word[0].toLowerCase() : null;
    }
    return null;
}

function defaultTypeFor(kind) {
    return kind !== null && Object.hasOwn(TYPE_MAP, kind) ? TYPE_MAP[kind] : null;
}

// ---------------------------------------------------------------------------
// The index beside a chapter
// ---------------------------------------------------------------------------

function indexPath(chapterFile) {
    return join(dirname(chapterFile), ARTIFACT_DIR, INDEX_FILE);
}

function readIndex(file) {
    if (!existsSync(file)) return { entries: {} };
    try {
        const parsed = JSON.parse(readFileSync(file, 'utf8'));
        return { entries: parsed?.entries && typeof parsed.entries === 'object' ? parsed.entries : {} };
    } catch (error) {
        throw new Error(`${relative(REPO, file)} is not readable JSON: ${error.message}`);
    }
}

function writeIndex(file, index) {
    mkdirSync(dirname(file), { recursive: true });
    const entries = Object.fromEntries(Object.entries(index.entries).sort(([a], [b]) => (a < b ? -1 : 1)));
    writeFileSync(file, `${JSON.stringify({ schemaVersion: 1, entries }, null, 2)}\n`, 'utf8');
}

/** The specification filename a chapter's diagram gets: `<chapter>.<ordinal>.<type>.json`,
 *  with `.standard` before the extension when it is one of the diagrams that opts
 *  out of the showcase profile.
 *
 *  Everything the render step needs is in the name, so a specification carries no
 *  metadata block that could disagree with where it sits. That is also why the
 *  quality opt-out is a filename segment rather than a field inside the JSON or a
 *  side-car list: the one rule stays one rule. */
function specName(chapterFile, ordinal, type, quality = SHOWCASE) {
    return `${basename(chapterFile, '.md')}.${ordinal}.${type}${quality === STANDARD ? `.${STANDARD}` : ''}.json`;
}

/** Every specification on disk for one chapter's diagram N, sorted by filename.
 *
 *  Listed rather than computed, which is the whole point. `defaultTypeFor` gives a
 *  flowchart `workflow`, but `FLOWCHART_TYPES` allows `architecture`, `lifecycle`
 *  and `dataflow` as well — so building the expected filename out of the default
 *  made an authored specification at any other type invisible, and reported the
 *  diagram `missing` when it was really sitting there waiting to be rendered.
 *
 *  More than one match is an authoring mistake rather than a choice to make, so
 *  this returns all of them and the callers say so. Sorted, so which one is
 *  "first" is the same answer on every machine. */
function discoverSpecs(artifactDir, chapterFile, ordinal) {
    if (!existsSync(artifactDir)) return [];

    const chapter = basename(chapterFile, '.md');
    const found = [];

    for (const name of readdirSync(artifactDir).sort()) {
        const match = SPEC_NAME.exec(name);
        if (!match) continue;

        const [, namedChapter, namedOrdinal, type, quality] = match;
        if (namedChapter !== chapter || Number(namedOrdinal) !== ordinal) continue;

        // A file that is named like a specification but says something Archify
        // has no word for is not one. `render` still rejects it by name, loudly,
        // if somebody points at it on purpose.
        if (!ALL_TYPES.includes(type)) continue;
        if (quality !== undefined && quality !== STANDARD) continue;

        found.push({ name, type, quality: quality ?? SHOWCASE });
    }

    return found;
}

/** Whether an index entry is for this chapter's diagram N.
 *
 *  Both halves matter, and the chapter half was learned the hard way: `_archify/`
 *  sits beside the chapter, and in `.arc42/` every chapter is in the same folder, so
 *  one index holds four chapters' entries. Matching on the ordinal alone made
 *  rendering `07-deployment-view.md` #1 evict `06-runtime-view.md` #1 and then report
 *  it stale — with the other chapter's type. The entries are hash-keyed and coexist
 *  perfectly well; only the cleanup and the staleness test were blind.
 *
 *  `chapter` is read from the entry when it is there and recovered from the
 *  specification's filename when it is not, so an index written before this field
 *  existed still answers correctly rather than silently matching everything. */
function isSameDiagram(entry, chapterFile, ordinal) {
    if (!entry || entry.ordinal !== ordinal) return false;

    const chapter = basename(chapterFile, '.md');
    const fromSpec = typeof entry.spec === 'string'
        ? SPEC_NAME.exec(basename(entry.spec))?.[1] ?? null
        : null;
    const named = entry.chapter ? basename(entry.chapter, '.md') : fromSpec;

    // An entry that names no chapter at all predates both fields. Treating it as
    // this chapter's is the old behaviour, which is right in every folder that
    // holds one chapter and is the best guess available in the ones that do not.
    return named === null || named === chapter;
}

function parseSpecName(specFile) {
    const match = SPEC_NAME.exec(basename(specFile));
    if (!match) {
        throw new Error(`${basename(specFile)} is not named <chapter>.<ordinal>.<type>.json or <chapter>.<ordinal>.<type>.${STANDARD}.json`);
    }
    const [, chapter, ordinal, type, quality] = match;
    if (!ALL_TYPES.includes(type)) {
        throw new Error(`${basename(specFile)} names type '${type}', which Archify does not have. One of: ${ALL_TYPES.join(', ')}`);
    }
    // The only quality that may be written down is the opt-out. `showcase` is what
    // a name that says nothing means, so spelling it out would give the ordinary
    // case two filenames; anything else is a typo standing where a real decision
    // would go, and is rejected rather than read as a type.
    if (quality !== undefined && quality !== STANDARD) {
        throw new Error(`${basename(specFile)} names quality '${quality}', which is not a quality profile. Only '${STANDARD}' may be named; ${SHOWCASE} is the default and is never written into a name`);
    }
    const chapterFile = resolve(dirname(specFile), '..', `${chapter}.md`);
    if (!existsSync(chapterFile)) {
        throw new Error(`${basename(specFile)} expects a chapter at ${relative(REPO, chapterFile)}, which does not exist`);
    }
    return { chapterFile, ordinal: Number(ordinal), type, quality: quality ?? SHOWCASE };
}

// ---------------------------------------------------------------------------
// scan
// ---------------------------------------------------------------------------

/** Every knowledge chapter diagram and what state its artifact is in.
 *
 *  Six states, because the button the app draws has to tell them apart:
 *    rendered    hash matches an index entry whose artifact is on disk
 *    stale       an entry names this chapter and ordinal, but for a different hash
 *    unrendered  a specification exists and no artifact does
 *    missing     no specification, and Archify has a type for this kind
 *    unsupported no Archify type fits — classDiagram and the rest
 *    error       two specifications claim one diagram, so there is no answer to give */
function scan() {
    const rows = [];

    for (const folder of KNOWLEDGE_FOLDERS) {
        for (const chapterFile of markdownFiles(join(REPO, folder))) {
            const index = readIndex(indexPath(chapterFile));
            const artifactDir = join(dirname(chapterFile), ARTIFACT_DIR);

            for (const fence of mermaidFences(chapterFile)) {
                const kind = mermaidKind(fence.source);
                const hash = diagramSourceHash(fence.source);
                const entry = index.entries[hash];
                const displaced = Object.entries(index.entries).find(
                    ([entryHash, value]) => entryHash !== hash && isSameDiagram(value, chapterFile, fence.ordinal));

                // What the index recorded, then what is actually on disk, then the
                // default for the kind. The default stays last rather than first
                // so that a diagram nobody has authored still reports the type it
                // would be authored as, while one somebody authored at a
                // non-default type is found where they put it.
                const named = entry ?? displaced?.[1] ?? null;
                const specs = discoverSpecs(artifactDir, chapterFile, fence.ordinal);

                const type = named?.type ?? specs[0]?.type ?? defaultTypeFor(kind);
                const quality = named?.quality ?? specs[0]?.quality ?? SHOWCASE;
                const spec = type ? join(artifactDir, specName(chapterFile, fence.ordinal, type, quality)) : null;
                const artifact = spec ? spec.replace(/\.json$/, '.html') : null;

                let state;
                let error = null;
                if (specs.length > 1) {
                    state = 'error';
                    error = `two specifications claim this diagram: ${specs.map(found => found.name).join(' and ')}. Exactly one may exist.`;
                }
                else if (type === null) state = 'unsupported';
                else if (entry && artifact && existsSync(artifact)) state = 'rendered';
                else if (displaced) state = 'stale';
                else if (spec && existsSync(spec)) state = 'unrendered';
                else state = 'missing';

                rows.push({
                    chapter: relative(REPO, chapterFile).split(sep).join('/'),
                    ordinal: fence.ordinal,
                    line: fence.line,
                    kind,
                    type,
                    quality: type ? quality : null,
                    state,
                    error,
                    hash,
                    spec: spec ? relative(REPO, spec).split(sep).join('/') : null,
                    artifact: artifact ? relative(REPO, artifact).split(sep).join('/') : null
                });
            }
        }
    }

    return rows;
}

// ---------------------------------------------------------------------------
// render
// ---------------------------------------------------------------------------

/** Renders one specification and records what it was authored from.
 *
 *  `deliver` validates before it writes and exits non-zero if it cannot, so this
 *  adds no checking of its own — it forwards the receipt. What it does add is the
 *  index entry, which is the only thing that will ever connect the artifact back to
 *  the fence. */
function render(specFile) {
    const { chapterFile, ordinal, type, quality } = parseSpecName(specFile);
    const fences = mermaidFences(chapterFile);
    const fence = fences.find(f => f.ordinal === ordinal);
    if (!fence) {
        throw new Error(`${relative(REPO, chapterFile)} has ${fences.length} mermaid fences, so there is no diagram ${ordinal}`);
    }

    // One diagram, one specification. Rendering either of two competing files
    // would write an index entry that immediately contradicts the other, so the
    // author is told which two rather than being given a coin toss.
    const competing = discoverSpecs(dirname(specFile), chapterFile, ordinal);
    if (competing.length > 1) {
        throw new Error(`${relative(REPO, chapterFile)} diagram ${ordinal} has ${competing.length} specifications: ${competing.map(found => found.name).join(' and ')}. Delete the one that is not authoritative.`);
    }

    const kind = mermaidKind(fence.source);
    const expected = defaultTypeFor(kind);
    const allowed = kind === 'flowchart' || kind === 'graph' ? FLOWCHART_TYPES : expected === null ? [] : [expected];
    if (!allowed.includes(type)) {
        throw new Error(
            allowed.length === 0
                ? `${relative(REPO, chapterFile)} diagram ${ordinal} is a ${kind}, which no Archify type fits`
                : `${relative(REPO, chapterFile)} diagram ${ordinal} is a ${kind}, so its type must be ${allowed.join(' or ')}, not ${type}`);
    }

    const artifactFile = specFile.replace(/\.json$/, '.html');
    const result = spawnSync(process.execPath,
        [ARCHIFY, 'deliver', type, specFile, artifactFile, '--quality', quality, '--json'],
        { cwd: join(REPO, 'tools', 'archify'), encoding: 'utf8' });

    const receipt = tryParse(result.stdout);
    if (result.status !== 0 || receipt?.ok !== true) {
        const detail = receipt ? JSON.stringify(receipt.validation ?? receipt, null, 2) : `${result.stdout}${result.stderr}`;
        throw new Error(`deliver failed for ${relative(REPO, specFile)}:\n${detail}`);
    }

    // `standard` demotes crossings and corridors to warnings; it does not make an
    // error acceptable. Asserting the count here rather than trusting `ok` keeps
    // the opt-out honest — it buys a diagram permission to cross an edge, not
    // permission to be wrong.
    const errors = receipt.validation?.errors ?? 0;
    if (errors > 0) {
        throw new Error(`deliver reported ${errors} composition error(s) for ${relative(REPO, specFile)}:\n${JSON.stringify(receipt.validation, null, 2)}`);
    }

    const file = indexPath(chapterFile);
    const index = readIndex(file);
    // Drop whatever used to hold this chapter's diagram N. Re-rendering after an
    // edited fence is exactly the case that produces a second entry for one
    // diagram, and a leftover would keep the old artifact matching the old text
    // nobody can see any more.
    for (const [hash, entry] of Object.entries(index.entries)) {
        if (isSameDiagram(entry, chapterFile, ordinal)) delete index.entries[hash];
    }
    index.entries[diagramSourceHash(fence.source)] = {
        chapter: basename(chapterFile),
        ordinal,
        type,
        quality,
        kind,
        spec: basename(specFile),
        artifact: basename(artifactFile),
        checksPassed: receipt.validation?.checksPassed ?? null,
        checkCount: receipt.validation?.checkCount ?? null
    };
    writeIndex(file, index);

    return {
        spec: relative(REPO, specFile).split(sep).join('/'),
        artifact: relative(REPO, artifactFile).split(sep).join('/'),
        quality,
        bytes: receipt.artifact?.bytes ?? null,
        warnings: receipt.validation?.warnings ?? 0,
        validation: receipt.validation ?? null
    };
}

function tryParse(text) {
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

// ---------------------------------------------------------------------------
// scaffold
// ---------------------------------------------------------------------------

/** Where a specification for a chapter diagram goes, and the fence it must be
 *  authored from — printed rather than guessed at, because authoring is the step no
 *  script can do. It writes no stub: an unauthored stub that validates is worse than
 *  no file, since `scan` would then call the diagram `unrendered` and the app would
 *  offer to render nothing. */
function scaffold(chapterArg, ordinalArg) {
    const chapterFile = resolve(REPO, chapterArg);
    const ordinal = Number(ordinalArg);
    const fence = mermaidFences(chapterFile).find(f => f.ordinal === ordinal);
    if (!fence) throw new Error(`${relative(REPO, chapterFile)} has no mermaid diagram ${ordinal}`);

    const kind = mermaidKind(fence.source);
    const type = defaultTypeFor(kind);
    if (type === null) {
        throw new Error(`${relative(REPO, chapterFile)} diagram ${ordinal} is a ${kind}, which no Archify type fits`);
    }

    const spec = join(dirname(chapterFile), ARTIFACT_DIR, specName(chapterFile, ordinal, type));
    return {
        chapter: relative(REPO, chapterFile).split(sep).join('/'),
        ordinal,
        line: fence.line,
        kind,
        defaultType: type,
        alternativeTypes: kind === 'flowchart' || kind === 'graph' ? FLOWCHART_TYPES.filter(t => t !== type) : [],
        spec: relative(REPO, spec).split(sep).join('/'),
        hash: diagramSourceHash(fence.source),
        source: normalizeDiagramSource(fence.source)
    };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const STATE_ORDER = ['rendered', 'stale', 'unrendered', 'missing', 'unsupported', 'error'];

function printScan(rows) {
    const counts = Object.fromEntries(STATE_ORDER.map(state => [state, rows.filter(r => r.state === state).length]));
    for (const row of rows) {
        const type = `${row.type ?? '-'}${row.quality === STANDARD ? ` (${STANDARD})` : ''}`;
        console.log(`${row.state.padEnd(11)} ${type.padEnd(13)} ${row.kind ?? '?'} ${row.chapter}:${row.line} #${row.ordinal}`);
        if (row.error) console.log(`${' '.repeat(11)} ${row.error}`);
    }
    console.log('');
    console.log(`${rows.length} knowledge chapter diagrams: ${STATE_ORDER.map(s => `${counts[s]} ${s}`).join(', ')}`);
}

function main(argv) {
    const [command, ...rest] = argv;
    const json = rest.includes('--json');
    const args = rest.filter(a => !a.startsWith('--'));

    switch (command) {
        case 'scan': {
            let rows = scan();
            if (rest.includes('--missing')) rows = rows.filter(r => r.state === 'missing' || r.state === 'stale' || r.state === 'unrendered' || r.state === 'error');
            if (json) console.log(JSON.stringify(rows, null, 2));
            else printScan(rows);
            return 0;
        }

        case 'render': {
            const specs = rest.includes('--all')
                ? scan().filter(r => r.state === 'unrendered' || r.state === 'stale').map(r => join(REPO, r.spec))
                : args.map(a => resolve(REPO, a));
            if (specs.length === 0) {
                console.log('Nothing to render.');
                return 0;
            }
            const results = [];
            let failed = 0;
            for (const spec of specs) {
                try {
                    const result = render(spec);
                    results.push({ ok: true, ...result });
                    // The warning count is printed rather than hidden: a `standard`
                    // render is allowed to have warnings, and an author who cannot
                    // see how many has no way to tell an accepted crossing from a
                    // drawing that quietly got worse.
                    if (!json) console.log(`ok   ${result.artifact} (${result.validation?.checksPassed}/${result.validation?.checkCount} checks, ${result.warnings} warnings, quality ${result.quality}, ${result.bytes} bytes)`);
                } catch (error) {
                    failed++;
                    results.push({ ok: false, spec: relative(REPO, spec).split(sep).join('/'), error: error.message });
                    if (!json) console.error(`FAIL ${relative(REPO, spec)}\n${error.message}`);
                }
            }
            if (json) console.log(JSON.stringify(results, null, 2));
            return failed === 0 ? 0 : 1;
        }

        case 'scaffold': {
            if (args.length < 2) throw new Error('scaffold needs a chapter and an ordinal');
            const plan = scaffold(args[0], args[1]);
            if (json) console.log(JSON.stringify(plan, null, 2));
            else {
                console.log(`chapter   ${plan.chapter}:${plan.line} #${plan.ordinal} (${plan.kind})`);
                console.log(`type      ${plan.defaultType}${plan.alternativeTypes.length ? ` (or ${plan.alternativeTypes.join(', ')})` : ''}`);
                console.log(`write     ${plan.spec}`);
                console.log(`then      node tools/diagrams/archify-artifacts.mjs render ${plan.spec}`);
                console.log('');
                console.log(plan.source);
            }
            return 0;
        }

        case 'verify': {
            const rows = scan();
            const broken = rows.filter(r => r.state === 'stale' || r.state === 'unrendered' || r.state === 'error');
            if (json) console.log(JSON.stringify({ ok: broken.length === 0, total: rows.length, broken }, null, 2));
            else if (broken.length === 0) {
                const rendered = rows.filter(r => r.state === 'rendered').length;
                console.log(`ok: ${rendered} of ${rows.length} knowledge chapter diagrams have a current artifact, and none is out of date.`);
            } else {
                for (const row of broken) console.error(`${row.state} ${row.chapter} #${row.ordinal}${row.error ? ` — ${row.error}` : ''}`);
                console.error(`\n${broken.length} artifact(s) out of date or unrendered. Run: node tools/diagrams/archify-artifacts.mjs render --all`);
            }
            return broken.length === 0 ? 0 : 1;
        }

        default:
            console.error('usage: archify-artifacts.mjs <scan|render|scaffold|verify> [options]');
            return 2;
    }
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))) {
    try {
        process.exit(main(process.argv.slice(2)));
    } catch (error) {
        console.error(error.message);
        process.exit(1);
    }
}
