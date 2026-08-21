// metadata.mjs — parsing and validation for the chapter/file `meta` YAML
// blocks defined in knowledge-chapter-metadata.instructions.md.
//
// The schema used across .domain/.arc42/.backlog/.tech/.design is intentionally small and
// flat (single-line scalars, null, or bracket lists), so we parse it with a
// tiny hand-written reader instead of pulling in a YAML dependency.

const STATUS_BY_FOLDER = {
    domain: ["draft", "proposed", "active", "deprecated"],
    arc42: ["draft", "proposed", "active", "deprecated"],
    backlog: ["draft", "ready", "in-progress", "done", "blocked"],
    tech: ["candidate", "trial", "adopted", "hold", "retired"],
    design: ["draft", "active", "deprecated"],
};

// Allowed `type` values per folder, split by block level. `type` records *what
// kind of thing* a chapter or file is — the classification that used to be
// written as a heading prefix (`## Aggregate: Order`). Heading text now carries
// the name alone, so anchors are slugs of the bare name.
//
// A folder whose lists are empty defines no kind distinction of its own: in
// `.arc42`, `.backlog`, and `.design` the only such distinction (chapter vs
// section, item vs sub-item) is already carried by heading level, so inventing
// values there would restate the document structure. `type` is omitted in those
// folders and reported when used.
const TYPE_BY_FOLDER = {
    domain: {
        chapter: [
            "aggregate",
            "entity",
            "value-object",
            "enum",
            "shared-value-objects",
            "shared-enums",
            "domain-service",
            "domain-event",
            "feature",
            "sub-feature",
            "term",
        ],
        file: ["context-map", "domain", "features", "model", "flow", "dependencies", "naming"],
    },
    tech: {
        chapter: [
            "language",
            "runtime",
            "framework",
            "library",
            "package",
            "tool",
            "service",
            "platform",
            "protocol",
            "format",
        ],
        file: [],
    },
    arc42: { chapter: [], file: [] },
    backlog: { chapter: [], file: [] },
    design: { chapter: [], file: [] },
};

// `.tech` spelled this concept `kind` before `type` was unified across folders.
// The old name keeps working so an existing repository is not broken by a
// generator sync, but it lints as a warning and is not documented any more.
const LEGACY_TYPE_FIELD_BY_FOLDER = { tech: "kind" };

// Fields every folder's chapter/file block may carry, plus folder-specific
// extras layered in below. `order` is file-level only (see validateDocument):
// it declares the reading order of a directory's entries.
const COMMON_OPTIONAL_FIELDS = ["type", "related", "issue", "effort", "roadmap"];

// `roadmap` entries are lowercase kebab-case tag slugs, not chapter references.
const ROADMAP_TAG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const FILE_ONLY_FIELDS = ["order"];
const FOLDER_EXTRA_FIELDS = {
    domain: ["depends-on", "aliases", "feature-flag"],
    arc42: [],
    backlog: ["depends-on", "implements"],
    tech: ["kind", "version", "depends-on", "alternatives"],
    design: [],
};

/** Determine which knowledge folder a repo-relative path belongs to. */
export function folderKindForPath(relPath) {
    const normalized = relPath.replace(/\\/g, "/");
    if (normalized.startsWith(".domain/")) return "domain";
    if (normalized.startsWith(".arc42/")) return "arc42";
    if (normalized.startsWith(".backlog/")) return "backlog";
    if (normalized.startsWith(".tech/")) return "tech";
    if (normalized.startsWith(".design/")) return "design";
    return null;
}

/**
 * The `type` values a folder allows on a block at this level, or `[]` when the
 * folder defines no kind distinction. `level` is "file" for the level-1 block
 * and "chapter" for every other heading.
 */
export function typeValuesFor(folder, level) {
    return TYPE_BY_FOLDER[folder]?.[level] ?? [];
}

/**
 * The effective `type` of a block, falling back to the folder's legacy field
 * name where one exists. Returns null when the block declares no type.
 */
export function resolveType(folder, meta) {
    if (!meta) return null;
    if (meta.type !== undefined && meta.type !== null && meta.type !== "") return meta.type;
    const legacy = LEGACY_TYPE_FIELD_BY_FOLDER[folder];
    const value = legacy ? meta[legacy] : null;
    return value === undefined || value === "" ? null : value;
}

function parseScalar(raw) {
    const value = raw.trim();
    if (value === "null" || value === "") return null;
    if (value.startsWith("[") && value.endsWith("]")) {
        const inner = value.slice(1, -1).trim();
        if (inner === "") return [];
        return inner
            .split(",")
            .map((entry) => stripQuotes(entry.trim()))
            .filter((entry) => entry.length > 0);
    }
    return stripQuotes(value);
}

function stripQuotes(value) {
    if (
        (value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'"))
    ) {
        return value.slice(1, -1);
    }
    return value;
}

/** Normalize a scalar-or-list metadata value to a list. */
function toList(value) {
    if (value === null || value === undefined) return [];
    return Array.isArray(value) ? value : [value];
}

/** Parse the body of a fenced ```meta block (without the fences) into an object. */
export function parseMetaBody(body) {
    const result = {};
    for (const line of body.split("\n")) {
        if (!line.trim()) continue;
        const idx = line.indexOf(":");
        if (idx === -1) continue;
        const key = line.slice(0, idx).trim();
        const value = line.slice(idx + 1);
        result[key] = parseScalar(value);
    }
    return result;
}

/**
 * Split a markdown document into its headings and, for each heading, the
 * immediately-following `meta` block (if present).
 *
 * Returns `{ fileMeta, fileTitle, chapters }` where `chapters` covers every
 * `#`/`##`/`###` heading found (level 1 is also exposed as `fileMeta` /
 * `fileTitle` for convenience, matching the "file-level block sits under the
 * top-level heading" convention).
 */
export function parseDocument(markdown) {
    const lines = markdown.split(/\r?\n/);
    const chapters = [];
    let fileTitle = null;
    let fileMeta = null;

    for (let i = 0; i < lines.length; i++) {
        const headingMatch = /^(#{1,6})\s+(.*)$/.exec(lines[i]);
        if (!headingMatch) continue;

        const level = headingMatch[1].length;
        const text = headingMatch[2].trim();
        const slug = slugify(text);

        // Look ahead past blank lines for a ```meta fence.
        let j = i + 1;
        while (j < lines.length && lines[j].trim() === "") j++;

        let meta = null;
        let metaRaw = null;
        if (j < lines.length && /^```meta\s*$/.test(lines[j].trim())) {
            const bodyLines = [];
            let k = j + 1;
            while (k < lines.length && lines[k].trim() !== "```") {
                bodyLines.push(lines[k]);
                k++;
            }
            metaRaw = bodyLines.join("\n");
            meta = parseMetaBody(metaRaw);
        }

        const entry = { level, text, slug, line: i + 1, meta, metaRaw };
        chapters.push(entry);

        if (level === 1 && fileTitle === null) {
            fileTitle = text;
            fileMeta = meta;
        }
    }

    return { fileTitle, fileMeta, chapters };
}

// GitHub's anchor algorithm lowercases, strips punctuation, then replaces each
// remaining whitespace character with a hyphen — it does *not* collapse runs.
// "Organizational & Process Constraints" therefore anchors as
// "organizational--process-constraints" (double hyphen where the & was).
export function slugify(text) {
    return text
        .toLowerCase()
        .trim()
        .replace(/[^\w\s-]/g, "")
        .replace(/\s/g, "-");
}

/**
 * Validate one block's `type` against its folder's vocabulary.
 *
 * Messages are sentence fragments beginning with a verb, so each caller can
 * prefix its own subject. Shared by the document lint and by graph
 * construction, so the canvas, the CLI, and CI all report the same thing.
 */
export function typeIssues(folder, blockLevel, meta) {
    const issues = [];
    if (!meta) return issues;

    const allowed = typeValuesFor(folder, blockLevel);
    const declared = resolveType(folder, meta);
    if (allowed.length) {
        if (declared === null) {
            issues.push({
                severity: "error",
                message: `is missing required \`type\`. Expected one of: ${allowed.join(", ")}.`,
            });
        } else if (!allowed.includes(declared)) {
            issues.push({
                severity: "error",
                message: `has type "${declared}", expected one of: ${allowed.join(", ")}.`,
            });
        }
    } else if (declared !== null) {
        issues.push({
            severity: "warning",
            message: `sets \`type\` to "${declared}", but the ${folder} folder defines no \`type\` value set at ${blockLevel} level — heading level already carries that distinction. Omit the field.`,
        });
    }

    const legacy = LEGACY_TYPE_FIELD_BY_FOLDER[folder];
    if (legacy && meta[legacy] != null) {
        issues.push({
            severity: "warning",
            message: `uses \`${legacy}\`, which has been renamed to \`type\`. Rename the field; \`${legacy}\` still works but is no longer documented.`,
        });
    }

    return issues;
}

/**
 * Flag literal escape sequences sitting in Markdown body text.
 *
 * An agent writing a file through a shell can emit the escape itself rather
 * than the newline it stands for — a PowerShell here-string that was single-
 * quoted when it needed interpolation, say. The failure is silent and
 * disproportionate: a `## Heading` glued onto the end of the previous line
 * stops being a heading, so the chapter disappears from the outline, from the
 * graph, and from every check that reasons about headings. Nothing else here
 * can catch it, because by the time those checks run the heading is prose.
 *
 * Warning rather than error: a document legitimately discussing escape
 * sequences would otherwise have no way to say so. Only newline escapes are
 * matched — `\t` was deliberately left out, because it breaks no structure and
 * collides with unformatted Windows paths. A bare `C:\temp\new` written
 * outside backticks is still a known false positive; formatting paths as code
 * avoids it.
 */
export function escapeSequenceIssues(markdown) {
    const issues = [];
    let inFence = false;
    let fenceChar = null;

    markdown.split(/\r?\n/).forEach((line, index) => {
        const fence = line.match(/^\s*(`{3,}|~{3,})/);
        if (fence) {
            if (!inFence) {
                inFence = true;
                fenceChar = fence[1][0];
            } else if (fence[1][0] === fenceChar) {
                inFence = false;
                fenceChar = null;
            }
            return;
        }
        if (inFence) return;

        // The PowerShell escape is checked against the line with double-backtick
        // code spans removed: that is how Markdown quotes a run containing
        // backticks, so a doubly-quoted occurrence is a document *describing*
        // the escape rather than one corrupted by it. Single-backtick spans are
        // left in place, because those backticks are part of the corrupted
        // token itself. The C-style escapes drop single-backtick spans too,
        // since a backticked \n is ordinary documentation.
        const quoted = line.replace(/``.+?``/g, "");
        const found = new Set();
        if (quoted.includes("`r`n")) found.add("`r`n");
        for (const match of quoted.replace(/`[^`]*`/g, "").matchAll(/\\r\\n|\\n/g)) {
            found.add(match[0]);
        }
        if (!found.size) return;

        const glued = /(?:`r`n|\\r\\n|\\n)\s*#{1,6}\s/.test(quoted);
        issues.push({
            severity: "warning",
            line: index + 1,
            message:
                `has a literal ${[...found].map((s) => `"${s}"`).join(" and ")} ` +
                `escape sequence in body text on line ${index + 1}` +
                (glued
                    ? ", with a heading immediately after it — that heading does not start a line, so it is not being parsed as a heading."
                    : ". If a line break was intended, the escape was not interpreted.")
        });
    });

    return issues;
}

/**
 * Heuristic lint of a document's metadata blocks against
 * chapter-metadata.instructions.md. Not a full structural validator (it does
 * not know which headings are "addressable chapters" per folder — see that
 * folder's own instructions file) — it checks the blocks that *are* present
 * plus the file-level block, which covers the common authoring mistakes.
 */
export function validateDocument(relPath, markdown) {
    const kind = folderKindForPath(relPath);
    const issues = [];
    if (!kind) {
        issues.push({
            severity: "info",
            message: `${relPath} is not under .domain/, .arc42/, .backlog/, .tech/, or .design/ — no metadata rules apply.`,
        });
        return issues;
    }

    const { fileTitle, fileMeta, chapters } = parseDocument(markdown);
    for (const issue of escapeSequenceIssues(markdown)) {
        issues.push({ severity: issue.severity, message: `${relPath} ${issue.message}` });
    }
    const allowedStatus = STATUS_BY_FOLDER[kind];
    const optionalFields = new Set([
        ...COMMON_OPTIONAL_FIELDS,
        ...FILE_ONLY_FIELDS,
        ...FOLDER_EXTRA_FIELDS[kind],
    ]);

    if (!fileTitle) {
        issues.push({
            severity: "error",
            message: "No top-level `#` heading found — every file needs one file-level chapter.",
        });
    } else if (!fileMeta) {
        issues.push({
            severity: "error",
            message: `File-level heading "${fileTitle}" is missing its \`meta\` block.`,
        });
    }

    for (const chapter of chapters) {
        const label = `${"#".repeat(chapter.level)} ${chapter.text} (line ${chapter.line})`;
        if (!chapter.meta) {
            // Level-1 heading already reported above as the file-level block.
            if (chapter.level > 1) {
                issues.push({
                    severity: "warning",
                    message: `${label} has no \`meta\` block. Add one if this heading is an addressable chapter for this folder.`,
                });
            }
            continue;
        }

        if (!("status" in chapter.meta) || chapter.meta.status === null) {
            issues.push({ severity: "error", message: `${label} is missing required \`status\`.` });
        } else if (!allowedStatus.includes(chapter.meta.status)) {
            issues.push({
                severity: "error",
                message: `${label} has status "${chapter.meta.status}", expected one of: ${allowedStatus.join(", ")}.`,
            });
        }

        // `type` records what kind of thing this chapter or file is, in the
        // vocabulary its folder defines. Folders that define no vocabulary
        // (`.arc42`, `.backlog`, `.design`) omit the field entirely.
        const blockLevel = chapter.level === 1 ? "file" : "chapter";
        for (const issue of typeIssues(kind, blockLevel, chapter.meta)) {
            issues.push({ severity: issue.severity, message: `${label} ${issue.message}` });
        }

        // A feature flag key names an application feature in the consuming
        // repository, whose constants this tooling cannot see — so the key
        // itself is deliberately never validated. What is checked is that it is
        // a key at all: an entry carrying `#` or `/` is a chapter or file
        // reference pasted into a field that produces no edge.
        if (kind === "domain" && chapter.meta["feature-flag"] != null) {
            for (const key of toList(chapter.meta["feature-flag"])) {
                if (key.includes("#") || key.includes("/")) {
                    issues.push({
                        severity: "error",
                        message: `${label} has \`feature-flag\` entry "${key}" — feature flag keys are application identifiers, not \`<path>#<slug>\` chapter references.`,
                    });
                }
            }
        }

        // `effort` is a story-point estimate, so it is a single non-negative
        // integer. A list, a fraction, a negative number, or a word such as
        // "large" is not an estimate this schema can total or compare.
        if (chapter.meta.effort != null) {
            const raw = chapter.meta.effort;
            const isInteger = typeof raw === "string" && /^\d+$/.test(raw);
            if (!isInteger) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`effort\` "${Array.isArray(raw) ? raw.join(", ") : raw}" — effort is a story-point estimate and must be a single non-negative integer.`,
                });
            }
        }

        // Roadmap entries are tag slugs an application groups work by, not
        // `<path>#<slug>` chapter references, so the tag vocabulary lives in the
        // consuming repository and only the slug shape is checked here.
        if (chapter.meta.roadmap != null) {
            for (const tag of toList(chapter.meta.roadmap)) {
                if (!ROADMAP_TAG_PATTERN.test(tag)) {
                    issues.push({
                        severity: "warning",
                        message: `${label} has \`roadmap\` entry "${tag}" — roadmap tags are lowercase kebab-case slugs, not chapter references or free text.`,
                    });
                }
            }
        }

        for (const [key, value] of Object.entries(chapter.meta)) {
            if (key === "status") continue;
            if (FILE_ONLY_FIELDS.includes(key) && chapter.level > 1) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`${key}\`, which belongs on the file-level block only — it describes the document's directory, not a chapter.`,
                });
                continue;
            }
            if (!optionalFields.has(key)) {
                issues.push({
                    severity: "warning",
                    message: `${label} has unrecognized field \`${key}\` for the ${kind} folder.`,
                });
                continue;
            }
            const isEmptyList = Array.isArray(value) && value.length === 0;
            if (isEmptyList || value === null) {
                issues.push({
                    severity: "warning",
                    message: `${label} sets \`${key}\` to an empty/null value — omit the field instead per the omit-when-empty rule.`,
                });
            }
        }

        if (chapter.level === 1 && "order" in chapter.meta) {
            const entries = chapter.meta.order;
            if (!Array.isArray(entries)) {
                issues.push({
                    severity: "error",
                    message: `${label} has \`order\` that is not a list. Use a list of sibling file or directory names.`,
                });
            } else {
                for (const entry of entries) {
                    if (typeof entry !== "string" || entry.includes("/")) {
                        issues.push({
                            severity: "error",
                            message: `${label} has \`order\` entry "${entry}" — entries must be plain names of siblings in the same directory, not paths.`,
                        });
                    }
                }
                const duplicates = entries.filter((e, i) => entries.indexOf(e) !== i);
                if (duplicates.length) {
                    issues.push({
                        severity: "error",
                        message: `${label} lists ${[...new Set(duplicates)].join(", ")} more than once in \`order\`.`,
                    });
                }
            }
        }
    }

    return issues;
}
