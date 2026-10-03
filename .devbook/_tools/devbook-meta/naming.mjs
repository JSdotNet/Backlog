// naming.mjs — derives the term register from the graph `buildGraph` already
// built.
//
// The ubiquitous language lives on the model in `domain/`: a modelled concept
// is its own entry, its surface names in its `aliases` field, and a word with
// no chapter to sit on is a `term` chapter under `## Ubiquitous Language`. A
// reader that wants the language as a list — name, meaning, other names, where
// it is worked out — would otherwise parse Markdown; this is that list. The
// graph's nodes carry a label and no meaning, which is why it is a file of its
// own and not a filter over `graph.json`.
//
// `arc42/12-glossary.md` is not read. Its "Also called" line is prose, not an
// `aliases` field, and a generator that parsed it would make a writing habit
// part of the schema — per the checks-and-indexes decision in the repository's
// devbook.
//
// Its `schemaVersion` is the register's own, starting at 1, not the contract
// version the other three documents carry: a consumer outside this repository
// pins it, and a contract bump that leaves this shape alone must not break it.

import { REPO_SCOPE, DEVBOOK_ROOT, generatorPath } from "./graph.mjs";

export const NAMING_SCHEMA_VERSION = 1;

const asList = (value) => (value == null ? [] : Array.isArray(value) ? value : [value]);
const fold = (name) => String(name).trim().toLowerCase();

// A rule is not a word. A `requirement` or `invariant` chapter's `aliases` are
// the codes it is cited by elsewhere — a requirement code from the document it
// came from, shared by every rule one table row was split into — not names of a
// concept, so the register leaves the behaviour chapters out, and their codes
// never collide with a term or with each other. Per the checks-and-indexes
// decision.
const RULE_KINDS = new Set(["requirement", "invariant"]);

/** Whether a graph node is an entry in the register. */
function isTerm(node) {
    if (node.type !== "chapter" || node.folder !== "domain" || RULE_KINDS.has(node.kind)) return false;
    return node.kind === "term" || asList(node.aliases).length > 0;
}

/**
 * Every term in the corpus, sorted by name, with what the register reports
 * about itself: a term with no definition, and a name or alias claimed twice.
 * Both are warnings — the register describes the language, the chapter's own
 * check is what holds it.
 */
export function collectTerms(graph) {
    const ledes = graph.ledes ?? new Map();
    const terms = graph.nodes.filter(isTerm).map((node) => ({
        name: node.label,
        description: ledes.get(node.id) ?? "",
        id: node.id,
        path: node.path,
        anchor: node.slug,
        aliases: asList(node.aliases).map(String),
        related: asList(node.related).map(String),
    }));
    terms.sort(
        (a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }) || a.id.localeCompare(b.id)
    );

    const problems = [];
    for (const term of terms) {
        if (term.description) continue;
        problems.push({
            severity: "warning",
            path: term.path,
            message: `${term.id} is a term with no definition — write what it means as the chapter's first paragraph.`,
        });
    }

    // One owner per spelling, names and aliases alike. A term listing the same
    // alias twice, or its own name as an alias, is the same claim and passes.
    const claimed = new Map();
    for (const term of terms) {
        for (const spelling of new Set([term.name, ...term.aliases].map(fold))) {
            const owner = claimed.get(spelling);
            if (!owner) {
                claimed.set(spelling, term);
                continue;
            }
            problems.push({
                severity: "warning",
                path: term.path,
                message: `${term.id} and ${owner.id} both claim the term "${spelling}" — one concept, one entry: keep it on one chapter and drop it from the other.`,
            });
        }
    }

    return { terms, problems };
}

/**
 * Build the serializable term register for one scope, following the
 * derived-artifacts convention. A folder other than `domain/` writes an empty
 * register, so every `_meta/` folder has the same four files.
 */
export function buildNamingDocument(repoRoot, scope, graph, folders) {
    const { terms, problems } = collectTerms(graph);
    const roots = scope === REPO_SCOPE ? folders : [scope];
    const inScope = (relPath) => roots.some((root) => relPath.startsWith(`${root}/`));
    const scoped = terms.filter((term) => inScope(term.path));

    return {
        schemaVersion: NAMING_SCHEMA_VERSION,
        generatedBy: generatorPath(repoRoot),
        scope,
        sources: roots,
        // Deliberately no timestamp, like the other three: re-running the
        // generator reproduces the file byte for byte.
        stats: {
            terms: scoped.length,
            aliases: scoped.reduce((total, term) => total + term.aliases.length, 0),
        },
        problems: problems.filter((problem) => inScope(problem.path)),
        terms: scoped,
    };
}

/** Repo-relative output path for a scope, per the derived-artifacts convention. */
export function namingPathFor(scope) {
    return scope === REPO_SCOPE ? `${DEVBOOK_ROOT}/_meta/naming.json` : `${scope}/_meta/naming.json`;
}
