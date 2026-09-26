// content-hash.test.mjs — `approved-hash`, the fingerprint that turns "the rung
// comes off when the content changes" from a rule people remember into a check.
//
// Run: node plugins/devbook/tools/devbook-meta/content-hash.test.mjs

import { validateDocument, chapterHash } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (ok) console.log(`PASS  ${name}`);
    else { failed++; console.log(`FAIL  ${name}${detail ? `\n${detail}` : ""}`); }
};

const fence = (body) => "```meta\n" + body + "```\n";
const note = (body) => "```annotation\n" + body + "```\n";

// One approved chapter in a domain file, with whatever meta the case needs.
const chapter = (meta, body = "Prose.\n") =>
    "# Ordering\n\n" + fence("type: domain\n") +
    "\n## Order\n\n" + fence("type: aggregate\n" + meta) + "\n" + body;

const find = (issues, severity, needle) =>
    issues.find((i) => i.severity === severity && i.message.includes(needle));
const dump = (issues) => JSON.stringify(issues, null, 2);

const PATH = ".devbook/domain/ordering/domain.md";
const approved = "status: approved\napproved-by: Job\napproved-at: 2026-09-22\n";

// The hash of the chapter as `chapter()` builds it — every case below is
// written against this one value, so a change to the normalisation fails
// loudly here instead of silently everywhere.
const HASH = chapterHash(chapter(approved), 7);

{
    const issues = validateDocument(PATH, chapter(approved + `approved-hash: ${HASH}\n`));
    check(!issues.some((i) => i.message.includes("approved-hash")), "a matching fingerprint is silent", dump(issues));
}

{
    const issues = validateDocument(PATH, chapter(approved + "approved-hash: sha256:deadbeef\n"));
    check(Boolean(find(issues, "error", "content that has changed")), "a mismatching fingerprint errors", dump(issues));
}

{
    const issues = validateDocument(PATH, chapter(approved));
    check(!issues.some((i) => i.message.includes("approved-hash")), "no fingerprint is not a finding — the field is optional", dump(issues));
}

{
    const issues = validateDocument(PATH, chapter(approved + "approved-hash: beef\n"));
    check(Boolean(find(issues, "error", "eight lowercase hex")), "a malformed fingerprint errors", dump(issues));
}

{
    const issues = validateDocument(PATH, chapter(approved + `approved-hash: [${HASH}]\n`));
    check(Boolean(find(issues, "error", "empty or list value")), "a list of fingerprints errors — a chapter has one content", dump(issues));
}

{
    // An empty value is caught by the omit-when-empty rule that covers every
    // field, before the approval lint sees it — exactly as `approved-by:` is.
    const issues = validateDocument(PATH, chapter(approved + "approved-hash:\n"));
    check(Boolean(find(issues, "warning", "omit the field instead")), "an empty fingerprint is the omit-when-empty warning", dump(issues));
}

{
    const issues = validateDocument(PATH, chapter(`status: active\napproved-hash: ${HASH}\n`));
    check(Boolean(find(issues, "warning", "without `status: approved`")), "an orphaned fingerprint warns, as an orphaned approver does", dump(issues));
}

// The normalisation itself. Each pair must hash the same, or differently, for
// a stated reason — these are the promises the rule makes to an author.
{
    const base = chapter(approved);
    const reflowed = chapter(approved, "Prose.\n\n\n");
    check(chapterHash(base, 7) === chapterHash(reflowed, 7), "blank lines do not lapse an approval");
}

{
    const base = chapter(approved);
    const annotated = chapter(approved, "Prose.\n\n" + note("kind: question\nby: Job\n"));
    check(chapterHash(base, 7) === chapterHash(annotated, 7), "a note written after the approval does not lapse it");
}

{
    const base = chapter(approved);
    const edited = chapter(approved, "Prose, revised.\n");
    check(chapterHash(base, 7) !== chapterHash(edited, 7), "an edit to the body lapses the approval");
}

{
    const base = chapter(approved);
    const renamed = base.replace("## Order", "## Purchase Order");
    check(chapterHash(base, 7) !== chapterHash(renamed, 7), "renaming the chapter lapses the approval");
}

{
    // The `meta` block holds the hash, so it cannot be part of what is hashed:
    // writing the field must not change the value it records.
    const before = chapterHash(chapter(approved), 7);
    const after = chapterHash(chapter(approved + `approved-hash: ${before}\n`), 7);
    check(before === after, "writing the fingerprint does not change the fingerprint");
}

{
    // A `##` chapter covers its `###` subsections, so editing one lapses both.
    const withSub = chapter(approved, "Prose.\n\n### Lines\n\nDetail.\n");
    const subEdited = chapter(approved, "Prose.\n\n### Lines\n\nDetail, revised.\n");
    check(chapterHash(withSub, 7) !== chapterHash(subEdited, 7), "a subsection edit lapses the parent chapter's approval");
}

{
    // A fence that is neither `meta` nor `annotation` is content.
    const withCode = chapter(approved, "Prose.\n\n```js\nconst a = 1;\n```\n");
    const codeEdited = chapter(approved, "Prose.\n\n```js\nconst a = 2;\n```\n");
    check(chapterHash(withCode, 7) !== chapterHash(codeEdited, 7), "a code or diagram fence is part of what was approved");
}

{
    // The file block is a block like any other: its hash covers the whole file.
    const file = chapter(approved);
    const edited = chapter(approved, "Prose, revised.\n");
    check(chapterHash(file, 1) !== chapterHash(edited, 1), "a file-level fingerprint covers the whole file");
}

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
