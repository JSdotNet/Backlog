// knowledge-schema.mjs — the DDL of `_meta/knowledge.db`, as one string.
//
// One exported constant rather than a list of statements, and one file rather
// than a literal inside `build-database.mjs`, for a single reason: ADR 0004's
// named risk is that a schema written in Node and read in C# drifts silently,
// the way the Archify hash rule already can. A schema that exists as one exact
// piece of text can be pinned from the reading side — the C# contract test
// asserts against this string and against `SCHEMA_VERSION` below, so a column
// renamed here fails a test rather than a panel.
//
// Read `.arc42/adr/0004-knowledge-index-is-a-generated-local-database.md` before
// changing anything here. Two rules from it govern this file:
//
//   * The Node generator is the only writer. Nothing in C# creates a table,
//     migrates one, or repairs a row it can see is stale — a drifted file is
//     served from its Markdown instead. That is why there is no migration
//     machinery: the database is a build output, and the answer to a schema
//     change is to rebuild it.
//   * A reader that does not recognise `SCHEMA_VERSION` ignores the database
//     entirely and reads Markdown. So bumping the version is a safe, blunt
//     instrument, and it is the *only* instrument. Bump it for any change to
//     the text below that a reader could notice.
//
// No `PRAGMA` lives here. Journal mode and synchronous settings are properties
// of the file the writer produces, not of the shape the reader is pinned to, and
// mixing them in would make the pinned text depend on how it was applied.

/** Bumped for any change to `KNOWLEDGE_SCHEMA` a reader could notice.
 *
 *  2 — `chapter.search_text` was added and `chapter_fts` moved onto it, so the
 *  full-text index holds prose instead of the chapter's raw Markdown. */
export const SCHEMA_VERSION = 2;

/** The file the writer produces, relative to the repository root. */
export const DATABASE_PATH = '_meta/knowledge.db';

/**
 * Every table, virtual table and index of the knowledge database.
 *
 * Notes on the columns that are not self-evident:
 *
 * `node.out_of_scope` — the graph is stored once, unprojected, because "a scope"
 * is `WHERE folder = ?` and that is the whole point of the decision. The flag
 * therefore does not mean what `projectScope` means by it (a boundary node of
 * one particular projection); it marks a node that lies outside the knowledge
 * folders altogether — an `external` reference target, whose `folder` is null.
 *
 * `chapter.source_hash`, `chapter.size`, `chapter.mtime` — file-level facts,
 * repeated on every chapter of that file on purpose. The reader's drift check is
 * one `stat` per file it is about to present, compared against the row it
 * already has in hand; making that a join would buy nothing and cost the check
 * its simplicity. `mtime` is milliseconds since the Unix epoch, `size` is bytes,
 * and both hashes are lowercase hex SHA-256 — `source_hash` over the file's
 * bytes as read, `content_hash` over the chapter's own slice of them.
 *
 * `chapter.text` and `chapter.search_text` — the same slice of the file twice:
 * as authored, and as prose. `text` is verbatim, because it is what
 * `content_hash` is taken over and what anything rendering or quoting a chapter
 * needs. `search_text` is the same lines with every fenced block removed and the
 * `#` markers taken off the headings, and it is the column `chapter_fts`
 * indexes.
 *
 * Splitting them is what keeps a search result readable. `snippet()` returns the
 * indexed text, so whatever is indexed is what a reader is shown as the reason a
 * chapter is in their result list — and a snippet cut out of a chapter's
 * metadata block reads "draft aliases: [KnowledgeNote, Note] related: [...]".
 * The quieter half of the same fault is the matching: `status`, `draft`,
 * `active` and every chapter path occur in the metadata blocks far more often
 * than in prose, so before this column existed, `draft` matched 444 of the 1124
 * chapters in this repository and `status` matched 851. Neither number is an
 * answer to anything a reader meant.
 *
 * *Every* fenced block goes, not only the two that are certainly not chapter
 * content — the ```meta block that carries the chapter's metadata, and the
 * ```annotation block a `.domain` chapter may carry, which holds open questions
 * about the chapter rather than the chapter, and which the knowledge-folder
 * convention says to skip when reading a chapter as content. The rest of the
 * fences in this corpus are mermaid diagram source, and `stateDiagram-v2` or
 * `Created --> Organized` is no more readable in an excerpt than `status: draft`
 * is; a diagram is reachable through the graph and through its Archify artifact,
 * which is where a reader is meant to meet it. "A fenced block is not prose" is
 * also a rule that survives the next fence language somebody adds, which "these
 * two names are not prose" would not. The cost is real and small: a chapter that
 * is nothing but a diagram is findable by its title and its metadata-derived
 * columns rather than by the words inside the fence, and its `text` still holds
 * every one of them for anything that wants to look.
 *
 * `chapter_fts` — external-content FTS5 over `chapter`, so the corpus text is
 * stored once. Its columns are named for the `chapter` columns they mirror,
 * because that is how FTS5 reads an external content table back — `title` and
 * `search_text`, which is why `snippet(chapter_fts, 1, ...)` yields prose. It is
 * populated explicitly after `chapter` is filled; there are no synchronisation
 * triggers, because nothing ever updates a row here. The database is built whole
 * and renamed into place.
 *
 * `chapter_embedding` — keyed by `content_hash` rather than by chapter address,
 * so an unchanged chapter that moved is never re-embedded. Written empty in the
 * change that introduced it; the semantic tier is wired and makes no live call.
 * `vector` is `dimensions` IEEE-754 single-precision values, little-endian,
 * packed with no header — byte for byte what a `Float32Array` serialises to,
 * which is why this writer needs no encoder of its own. That layout is a
 * cross-language agreement like every other column here, so it is written down
 * rather than left to whichever side wrote first: `KnowledgeDatabase.ReadVector`
 * decodes exactly this and `KnowledgeVectorEncodingTests` pins it. `model` is
 * what produced the vector, and a reader configured for a different one ignores
 * the row rather than comparing across coordinate spaces.
 *
 * `archify_artifact.checks_passed` / `check_count` — present in the fourteen
 * `_archify/index.json` files today and read by no C# DTO. ADR 0004 lists them,
 * so they are carried rather than dropped: a column nobody reads is cheaper to
 * keep than a regeneration to add.
 *
 * `edge.id` carries no primary key. `graph.mjs` composes an edge id from its two
 * endpoints and its type and does not itself enforce uniqueness, so a document
 * that lists the same reference twice in one field would abort a build that
 * insisted on it — for a label, not for data anything joins on.
 */
export const KNOWLEDGE_SCHEMA = `
CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE node (
    id           TEXT PRIMARY KEY,
    type         TEXT NOT NULL,
    label        TEXT,
    folder       TEXT,
    path         TEXT,
    slug         TEXT,
    level        INTEGER,
    line         INTEGER,
    status       TEXT,
    out_of_scope INTEGER NOT NULL DEFAULT 0,
    effort       INTEGER,
    kind         TEXT,
    version      TEXT,
    issue        TEXT
);

CREATE TABLE node_attribute (
    node_id TEXT NOT NULL,
    name    TEXT NOT NULL,
    value   TEXT NOT NULL
);

CREATE TABLE edge (
    id     TEXT NOT NULL,
    type   TEXT NOT NULL,
    source TEXT NOT NULL,
    target TEXT NOT NULL
);

CREATE TABLE outline_entry (
    id        INTEGER PRIMARY KEY,
    scope     TEXT NOT NULL,
    parent_id INTEGER,
    ordinal   INTEGER NOT NULL,
    type      TEXT NOT NULL,
    name      TEXT NOT NULL,
    path      TEXT NOT NULL,
    title     TEXT,
    status    TEXT,
    kind      TEXT,
    is_root   INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE chapter (
    id           INTEGER PRIMARY KEY,
    path         TEXT NOT NULL,
    folder       TEXT,
    slug         TEXT NOT NULL,
    level        INTEGER NOT NULL,
    title        TEXT,
    status       TEXT,
    line         INTEGER NOT NULL,
    text         TEXT NOT NULL,
    search_text  TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    source_hash  TEXT NOT NULL,
    size         INTEGER NOT NULL,
    mtime        INTEGER NOT NULL
);

CREATE VIRTUAL TABLE chapter_fts USING fts5(
    title,
    search_text,
    content='chapter',
    content_rowid='id',
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TABLE chapter_embedding (
    content_hash TEXT PRIMARY KEY,
    model        TEXT NOT NULL,
    dimensions   INTEGER NOT NULL,
    vector       BLOB NOT NULL
);

CREATE TABLE archify_artifact (
    chapter_path  TEXT NOT NULL,
    fence_hash    TEXT NOT NULL,
    ordinal       INTEGER NOT NULL,
    type          TEXT,
    quality       TEXT,
    kind          TEXT,
    spec_path     TEXT,
    artifact_path TEXT,
    checks_passed INTEGER,
    check_count   INTEGER
);

CREATE TABLE problem (
    scope    TEXT NOT NULL,
    severity TEXT NOT NULL,
    path     TEXT,
    message  TEXT NOT NULL
);

CREATE INDEX node_folder ON node (folder);
CREATE INDEX node_path ON node (path);
CREATE INDEX node_attribute_node ON node_attribute (node_id, name);
CREATE INDEX edge_source ON edge (source);
CREATE INDEX edge_target ON edge (target);
CREATE INDEX outline_entry_scope ON outline_entry (scope, parent_id, ordinal);
CREATE INDEX chapter_path ON chapter (path);
CREATE INDEX archify_artifact_fence ON archify_artifact (fence_hash);
`;
