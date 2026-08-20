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

// Fields a folder requires on every metadata block, beyond `status`.
const FOLDER_REQUIRED_FIELDS = {
    domain: [],
    arc42: [],
    backlog: [],
    tech: ["kind"],
    design: [],
};

// Allowed values for enumerated folder-specific fields.
const TECH_KINDS = [
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
];

// Fields every folder's chapter/file block may carry, plus folder-specific
// extras layered in below. `order` is file-level only (see validateDocument):
// it declares the reading order of a directory's entries.
const COMMON_OPTIONAL_FIELDS = ["related", "issue"];
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
function slugify(text) {
    return text
        .toLowerCase()
        .trim()
        .replace(/[^\w\s-]/g, "")
        .replace(/\s/g, "-");
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

        // Folder-specific required fields apply to chapter blocks, not to the
        // file-level block (which describes the document as a whole).
        if (chapter.level > 1) {
            for (const required of FOLDER_REQUIRED_FIELDS[kind]) {
                if (!(required in chapter.meta) || chapter.meta[required] === null) {
                    issues.push({
                        severity: "error",
                        message: `${label} is missing required \`${required}\` for the ${kind} folder.`,
                    });
                }
            }
        }

        if (kind === "tech" && chapter.meta.kind && !TECH_KINDS.includes(chapter.meta.kind)) {
            issues.push({
                severity: "error",
                message: `${label} has kind "${chapter.meta.kind}", expected one of: ${TECH_KINDS.join(", ")}.`,
            });
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
