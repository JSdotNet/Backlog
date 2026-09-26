// devbook-schema.mjs — the DDL of the generated devbook database, as one text,
// and the notes on its columns.
//
// The DDL itself lives in `devbook-schema.sql` beside this file, and this module
// only reads it. Local ADR 0015 made the desktop app the writer that matters, so
// the schema now has two writers — this repository's Node tooling and the C#
// builder in `Backlog.Infrastructure.Devbook` — and one text both of them load:
// the C# assembly embeds the same `.sql` file. ADR 0004's named risk, a schema
// written in one language and read in another drifting silently, is answered by
// there being nothing to restate.
//
// Read `.devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md`
// and `0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md`
// before changing anything here. Two rules govern this file:
//
//   * There is no migration machinery. The database is a build output, rebuilt
//     whole, and the answer to a schema change is to rebuild it. A drifted file
//     is served from its Markdown until the next build.
//   * A reader that does not recognise the schema version ignores the database
//     entirely and reads Markdown. So bumping it is a safe, blunt instrument, and
//     it is the *only* instrument. Bump the `schema-version` line in the `.sql`
//     file for any change a reader could notice; `DevbookDatabaseSchema.Version`
//     is pinned to it by a C# test.
//
// No `PRAGMA` lives in the DDL. Journal mode and synchronous settings are
// properties of the file a writer produces, not of the shape the reader is
// pinned to.

import { readFileSync } from 'node:fs';

/** The schema text, exactly as `devbook-schema.sql` holds it. */
const SCHEMA_TEXT = readFileSync(new URL('./devbook-schema.sql', import.meta.url), 'utf8');

/** Bumped for any change to `DEVBOOK_SCHEMA` a reader could notice, in the
 *  `-- schema-version: N` line of `devbook-schema.sql`.
 *
 *  2 — `chapter.search_text` was added and `chapter_fts` moved onto it, so the
 *  full-text index holds prose instead of the chapter's raw Markdown. */
export const SCHEMA_VERSION = (() => {
    const match = /^-- schema-version: (\d+)\s*$/m.exec(SCHEMA_TEXT);
    if (!match) throw new Error('devbook-schema.sql carries no `-- schema-version: N` line.');
    return Number(match[1]);
})();

/**
 * Every table, virtual table and index of the devbook database.
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
 * about the chapter rather than the chapter, and which the devbook-folder
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
 * rather than left to whichever side wrote first: `DevbookDatabase.ReadVector`
 * decodes exactly this and `DevbookVectorEncodingTests` pins it. `model` is
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
export const DEVBOOK_SCHEMA = SCHEMA_TEXT;
