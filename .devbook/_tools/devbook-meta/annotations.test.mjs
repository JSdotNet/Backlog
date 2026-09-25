// Exercises the annotation fence: the block grammar a thread needs, the
// position rule that replaced content-hash anchoring, and the lint that keeps
// the core field set closed while leaving `ext` opaque.
import { parseAnnotationBody, parseAnnotations, annotationIssues, resolveAnnotation } from "./metadata.mjs";

const FENCE = "```";

const minimal = [
    "# Chapter",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "The outline is one indexed range read.",
    "",
    FENCE + "annotation",
    "author: jobsc",
    "date: 2026-09-02",
    "body: Is this still true now the outline rollup is a view?",
    FENCE,
].join("\n");

const full = [
    "# Chapter",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "The outline is one indexed range read.",
    "",
    FENCE + "annotation",
    "kind: question",
    "status: resolved",
    "author: jobsc",
    "date: 2026-09-02",
    "quote: one indexed range read",
    "body: |",
    "  Does this hold after the outline gained the roadmap rollup?",
    "  The rollup looked like it needed a second scan.",
    "replies:",
    "  - author: claude/flow-spec",
    "    date: 2026-09-02",
    "    body: No second scan — the rollup is a view over the same index.",
    "ext:",
    "  your-plugin:",
    "    raised-in: 2026-09-02",
    FENCE,
].join("\n");

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

// --- the grammar ----------------------------------------------------------

const parsed = parseAnnotationBody(full.split(FENCE + "annotation\n")[1].split("\n" + FENCE)[0]);

check(parsed.kind === "question" && parsed.author === "jobsc", "scalars parse", JSON.stringify(parsed.kind));
check(
    parsed.body === "Does this hold after the outline gained the roadmap rollup?\nThe rollup looked like it needed a second scan.",
    "a `|` body keeps its line breaks and loses its indent",
    JSON.stringify(parsed.body)
);
check(
    Array.isArray(parsed.replies) &&
        parsed.replies.length === 1 &&
        parsed.replies[0].author === "claude/flow-spec" &&
        parsed.replies[0].body === "No second scan — the rollup is a view over the same index.",
    "replies parse as an ordered list of mappings",
    JSON.stringify(parsed.replies)
);
check(
    parsed.ext && parsed.ext["your-plugin"] && parsed.ext["your-plugin"]["raised-in"] === "2026-09-02",
    "ext nests two levels deep and stays opaque",
    JSON.stringify(parsed.ext)
);

const defaults = resolveAnnotation({ author: "jobsc", date: "2026-09-02", body: "x" });
check(
    defaults.kind === "comment" && defaults.status === "open",
    "kind and status default to comment/open"
);

// --- position is the anchor ----------------------------------------------

const notes = parseAnnotations(minimal);
check(notes.length === 1 && notes[0].ordinal === 1, "one fence, ordinal 1", `got ${notes.length}`);
check(notes[0].chapter?.slug === "chapter", "the note knows its chapter", notes[0].chapter?.slug);
check(notes[0].target === "block", "a note after prose annotates that block", notes[0].target);

const chapterLevel = [
    "# Chapter",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    FENCE + "annotation",
    "author: jobsc",
    "date: 2026-09-02",
    "body: Whole-chapter question.",
    FENCE,
].join("\n");
check(
    parseAnnotations(chapterLevel)[0].target === "chapter",
    "a note straight after the meta block annotates the chapter"
);

const stacked = [minimal, "", FENCE + "annotation", "author: ann", "date: 2026-09-02", "body: Second thread.", FENCE].join("\n");
const stackedNotes = parseAnnotations(stacked);
check(
    stackedNotes.length === 2 && stackedNotes.every((n) => n.target === "block"),
    "a second fence attaches to the same passage, not to the first note",
    stackedNotes.map((n) => n.target).join(",")
);
check(stackedNotes[1].ordinal === 2, "ordinals count within the chapter", String(stackedNotes[1].ordinal));

const twoChapters = [minimal, "", "## Second", "", FENCE + "meta", FENCE, "", "Prose.", "", FENCE + "annotation", "author: a", "date: 2026-09-02", "body: b", FENCE].join("\n");
check(
    parseAnnotations(twoChapters)[1].ordinal === 1,
    "the ordinal restarts in the next chapter"
);

const insideOtherFence = [
    "# Chapter",
    "",
    FENCE + "meta",
    FENCE,
    "",
    "~~~markdown",
    FENCE + "annotation",
    "author: not-a-real-note",
    "~~~",
].join("\n");
check(
    parseAnnotations(insideOtherFence).length === 0,
    "an annotation shown inside another fence is an example, not a note"
);

// --- the lint -------------------------------------------------------------

const lints = [
    { name: "a minimal note is clean", markdown: minimal, errors: 0, warnings: 0 },
    { name: "a full thread is clean", markdown: full, errors: 0, warnings: 0 },
    {
        name: "a fence before the first heading is an error",
        markdown: [FENCE + "annotation", "author: a", "date: 2026-09-02", "body: b", FENCE].join("\n"),
        errors: 1,
        warnings: 0,
    },
    {
        name: "a missing author is an error",
        markdown: minimal.replace("author: jobsc\n", ""),
        errors: 1,
        warnings: 0,
    },
    {
        name: "an unknown kind is an error",
        markdown: minimal.replace("author: jobsc", "kind: nitpick\nauthor: jobsc"),
        errors: 1,
        warnings: 0,
    },
    {
        name: "an unknown status is an error",
        markdown: minimal.replace("author: jobsc", "status: wontfix\nauthor: jobsc"),
        errors: 1,
        warnings: 0,
    },
    {
        name: "a non-ISO date is an error",
        markdown: minimal.replace("date: 2026-09-02", "date: yesterday"),
        errors: 1,
        warnings: 0,
    },
    {
        name: "a quote matching nothing above is only a warning",
        markdown: minimal.replace("author: jobsc", "quote: a phrase that is not there\nauthor: jobsc"),
        errors: 0,
        warnings: 1,
    },
    {
        name: "a quote found in the block above is clean",
        markdown: minimal.replace("author: jobsc", "quote: one indexed range read\nauthor: jobsc"),
        errors: 0,
        warnings: 0,
    },
    {
        name: "ext that is not a mapping is an error",
        markdown: minimal.replace("author: jobsc", "ext: your-plugin\nauthor: jobsc"),
        errors: 1,
        warnings: 0,
    },
    {
        name: "a reply missing its author is an error",
        markdown: minimal.replace(
            "body: Is this still true now the outline rollup is a view?",
            ["body: q", "replies:", "  - date: 2026-09-02", "    body: an answer with no author"].join("\n")
        ),
        errors: 1,
        warnings: 0,
    },
    {
        name: "an unrecognized core field is a warning",
        markdown: minimal.replace("author: jobsc", "assignee: someone\nauthor: jobsc"),
        errors: 0,
        warnings: 1,
    },
];

for (const c of lints) {
    const issues = annotationIssues(c.markdown);
    const errors = issues.filter((i) => i.severity === "error").length;
    const warnings = issues.filter((i) => i.severity === "warning").length;
    check(
        errors === c.errors && warnings === c.warnings,
        c.name,
        `got ${errors} error(s), ${warnings} warning(s): ${issues.map((i) => i.message).join(" | ")}`
    );
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
