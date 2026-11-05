// content-hash.test.mjs — the chapter fingerprint `chapterHash` computes, which
// a host records a decision at (`devbook.chapter.fingerprint@1`). Since contract
// 38 no chapter carries the hash; these pin the normalisation a host recomputes.
//
// Run: node plugins/devbook/tools/devbook-meta/content-hash.test.mjs

import { chapterHash } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (ok) console.log(`PASS  ${name}`);
    else { failed++; console.log(`FAIL  ${name}${detail ? `\n${detail}` : ""}`); }
};

const fence = (body) => "```meta\n" + body + "```\n";
const note = (body) => "```annotation\n" + body + "```\n";

// One chapter in a domain file, with whatever meta the case needs.
const chapter = (meta, body = "Prose.\n") =>
    "# Ordering\n\n" + fence("type: domain\n") +
    "\n## Order\n\n" + fence("type: aggregate\n" + meta) + "\n" + body;

const approved = "date: 2026-09-22\n";

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
    // The `meta` block is not content: a field written in it must not change
    // the fingerprint a decision was recorded at.
    const before = chapterHash(chapter(approved), 7);
    const after = chapterHash(chapter(approved + "related: [.devbook/domain/ordering/features.md]\n"), 7);
    check(before === after, "a field written in the meta block does not change the fingerprint");
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
