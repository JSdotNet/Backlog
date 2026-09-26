// Asserts the review triad — `review`, `reviewer`, `review-at` — against the
// rules record 77 moved into the schema: written together or not at all, one
// of three states, a verdict held to the open notes it claims to stand on, and
// none of it left standing once the chapter is approved.
//
// Run: `node review-state.test.mjs`
import { validateDocument, reviewIssues } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

const fence = (body) => "```meta\n" + body + "```\n";
const note = (body) => "```annotation\n" + body + "```\n";
const find = (issues, severity, needle) =>
    issues.find((i) => i.severity === severity && i.message.includes(needle));
const dump = (issues) => JSON.stringify(issues, null, 2);
const triad = (state) => `review: ${state}\nreviewer: "@reviewer"\nreview-at: 2026-09-17\n`;
const chapter = (meta, body = "Prose.\n") =>
    `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n" + meta)}\n${body}`;

// --- Absent means no review is running -----------------------------------

check(reviewIssues({ status: "draft" }).length === 0, "no review fields is no review, and no issue");

// --- Together or not at all ----------------------------------------------

{
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter("review: requested\n"));
    check(
        Boolean(find(issues, "error", "written together or not at all")),
        "`review` without `reviewer` and `review-at` is an error",
        dump(issues)
    );
}
{
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter(triad("requested")));
    check(
        !issues.some((i) => i.message.includes("review")),
        "the full triad in `requested` over no notes is clean",
        dump(issues)
    );
}

// --- The closed state set and the date form ------------------------------

{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        chapter('review: pending\nreviewer: "@reviewer"\nreview-at: 17-09-2026\n')
    );
    check(Boolean(find(issues, "error", '`review` "pending"')), "an unknown review state is an error", dump(issues));
    check(Boolean(find(issues, "error", "`review-at`")), "a review date outside YYYY-MM-DD is an error", dump(issues));
}

// --- A verdict is held to its findings -----------------------------------

{
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter(triad("changes-requested")));
    check(
        Boolean(find(issues, "error", "`review: changes-requested` with no open annotation")),
        "`changes-requested` over no open note is a verdict without findings",
        dump(issues)
    );
}
{
    const open = note('author: "@reviewer"\ndate: 2026-09-17\nkind: suggestion\nbody: Tighten this.\n');
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter(triad("changes-requested"), `Prose.\n\n${open}`));
    check(
        !issues.some((i) => i.message.includes("review")),
        "`changes-requested` over one open note is consistent",
        dump(issues)
    );
}
{
    const open = note('author: "@reviewer"\ndate: 2026-09-17\nkind: question\nbody: Which test proves it?\n');
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter(triad("cleared"), `Prose.\n\n${open}`));
    check(
        Boolean(find(issues, "error", "`review: cleared` over 1 open annotation")),
        "`cleared` over an open note is an error",
        dump(issues)
    );
}
{
    const resolved = note('author: "@reviewer"\ndate: 2026-09-17\nkind: question\nstatus: resolved\nbody: Answered.\n');
    const issues = validateDocument(".devbook/domain/ordering/domain.md", chapter(triad("cleared"), `Prose.\n\n${resolved}`));
    check(
        !issues.some((i) => i.message.includes("review")),
        "a resolved note does not count against `cleared`",
        dump(issues)
    );
}

// --- Approval clears the road to it ---------------------------------------

{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        chapter('status: approved\napproved-by: "@lead"\napproved-at: 2026-09-17\n' + triad("cleared"))
    );
    check(
        Boolean(find(issues, "error", "while carrying review state")),
        "review state beside `status: approved` is an error",
        dump(issues)
    );
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
