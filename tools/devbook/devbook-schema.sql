-- devbook-schema.sql - the DDL of the generated devbook database, as one text.
--
-- schema-version: 3
--
-- One file read by both writers: tools/devbook/devbook-schema.mjs loads it for
-- the Node writer, and Backlog.Infrastructure.Devbook embeds it for the app's own
-- builder (local ADR 0015). Neither side restates it. The column notes are in
-- devbook-schema.mjs. Bump the version line above for any change a reader could
-- notice: a reader that does not recognise it ignores the database and reads
-- Markdown, and DevbookDatabaseSchema.Version is pinned to it by a test.
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
    mtime        INTEGER NOT NULL,
    open_annotations INTEGER NOT NULL DEFAULT 0
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
