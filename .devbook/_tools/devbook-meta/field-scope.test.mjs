// Asserts the four sub-rules that say *where* a field may sit, rather than what
// it may say. Each one is invisible to every other check in the pipeline: the
// value parses, the reference resolves, the graph builds — and the block is
// still wrong, because the rule it breaks is about placement.
//
// Run: `node field-scope.test.mjs`
import { validateDocument, slugify } from "./metadata.mjs";

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

// --- .domain field scope ------------------------------------------------

// `depends-on`, `feature-flag`, and `setting` are the delivery order and the
// switches of a capability. A `domain.md` chapter has none of them: it
// describes standing structure, and its relationships belong in `model.md` or
// `related`.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n` +
            `${fence("type: aggregate\ndepends-on: [.domain/ordering/features.md#refunds]\nfeature-flag: .domain/ordering/context.md#orders\nsetting: .domain/ordering/context.md#orders-visible\n")}\n` +
            `Prose.\n`
    );

    check(
        Boolean(find(issues, "error", '`depends-on` on a chapter of type "aggregate"')),
        "`depends-on` on an aggregate is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`feature-flag` on a chapter of type "aggregate"')),
        "`feature-flag` on an aggregate is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`setting` on a chapter of type "aggregate"')),
        "`setting` on an aggregate is an error",
        dump(issues)
    );
}

// The same three fields on the chapters that own them, which must stay silent —
// a scope check that fires on the legal case is worse than none.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/features.md",
        `# Ordering Features\n\n${fence("type: features\n")}\n## Refunds\n\n` +
            `${fence("type: feature\ndepends-on: [.domain/ordering/features.md#orders]\nfeature-flag: .domain/ordering/context.md#refunds\nsetting: .domain/ordering/context.md#refund-notices\n")}\n` +
            `Prose.\n\n### Partial refund\n\n` +
            `${fence("type: sub-feature\nfeature-flag: [.domain/ordering/context.md#refunds, .domain/ordering/context.md#partial-refunds]\n")}\nProse.\n`
    );

    check(
        !find(issues, "error", "scopes the field to"),
        "the three fields on a feature and a sub-feature are silent",
        dump(issues)
    );
    check(
        !find(issues, "error", "is not a `<path>#<slug>` reference"),
        "a reference-shaped `feature-flag` and `setting` are silent",
        dump(issues)
    );
}

// A bare application key is the shape `feature-flag` had before contract 11.
// It is reported by name, so a repository that skipped the migration is told
// which one to run rather than shown a reference that "does not resolve".
{
    const issues = validateDocument(
        ".devbook/domain/ordering/features.md",
        `# Ordering Features\n\n${fence("type: features\n")}\n## Refunds\n\n` +
            `${fence("type: feature\nfeature-flag: refunds\n")}\n` +
            `Prose.\n`
    );

    check(
        Boolean(find(issues, "error", "run the `011-context-md` migration")),
        "a bare key in `feature-flag` is an error naming the migration",
        dump(issues)
    );
}

// `key`, `default`, and `scope` describe the switch, so they sit on the
// `feature-flag` and `setting` chapters in `context.md` and nowhere else; a
// switch without a `key` has nothing for the code to resolve to; `scope` names
// who changes a setting at runtime, which a release-time flag has no answer
// for; and a flag's `default` is `on` or `off`.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/context.md",
        `# Ordering\n\n${fence("index: root\ntype: context\n")}\nBoundary.\n\n## Refunds\n\n` +
            `${fence("type: feature-flag\nkey: refunds\ndefault: off\n")}\nProse.\n\n## Refund notices\n\n` +
            `${fence("type: setting\nkey: notifications.refund\nscope: user\ndefault: on\n")}\nProse.\n\n## Express refunds\n\n` +
            `${fence("type: feature-flag\nscope: user\ndefault: enabled\n")}\nProse.\n\n## Refund limit\n\n` +
            `${fence("type: setting\nkey: refunds.limit\nscope: everyone\n")}\nProse.\n\n## Cashier\n\n` +
            `${fence("type: user\nrole: Cashier\nkey: cashier\n")}\nProse.\n`
    );

    check(
        !find(issues, "error", "## Refunds (") && !find(issues, "error", "## Refund notices ("),
        "a well-formed flag and setting are silent",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", "`feature-flag` chapter without a single `key`")),
        "a flag without `key` is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`scope` on a chapter of type "feature-flag"')),
        "`scope` on a flag is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`default` "enabled" on a feature flag')),
        "a flag `default` outside on/off is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`scope` "everyone"')),
        "a setting `scope` outside user/tenant/system is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`key` on a chapter of type "user"')),
        "`key` on an actor is an error",
        dump(issues)
    );
    check(
        !find(issues, "error", '`role` on a chapter of type "user"'),
        "an actor chapter is legal in `context.md`",
        dump(issues)
    );
}

// `role` is the authorization role an actor holds — the fourth beat of a
// `user` chapter made addressable — so it belongs to the three actor kinds in
// `actors.md` and to nothing else: a role on a feature would restate an
// authorization rule where the domain rule says it must never be written.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/actors.md",
        `# Ordering\n\n${fence("type: actors\n")}\n## Consultant\n\n` +
            `${fence("type: user\nrole: [Consultant, TeamLead]\n")}\nProse.\n\n## Bank\n\n` +
            `${fence("type: organisation\n")}\nProse.\n\n## Month Close\n\n` +
            `${fence("type: technical\nrole: System\n")}\nProse.\n`
    );

    check(
        !find(issues, "error", "scopes the field to"),
        "`role` on a user and a technical actor is silent",
        dump(issues)
    );
}
{
    const issues = validateDocument(
        ".devbook/domain/ordering/features.md",
        `# Ordering Features\n\n${fence("type: features\nrole: Consultant\n")}\n## Refunds\n\n` +
            `${fence("type: feature\nrole: Consultant\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", "`role` on the file-level block")),
        "`role` on the file-level block is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`role` on a chapter of type "feature"')),
        "`role` on a feature is an error",
        dump(issues)
    );
}

// A term that is already an aggregate, service, event, or field carries its
// aliases on that chapter rather than earning a duplicate `term` chapter, so
// `aliases` is legal on any chapter — only the file-level block is out.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\naliases: [Orders]\n")}\n## Order\n\n` +
            `${fence("type: aggregate\naliases: [OrderRoot, order_id]\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", "`aliases` on the file-level block")),
        "`aliases` on the file-level block is an error",
        dump(issues)
    );
    check(
        !find(issues, "error", "chapter of type"),
        "`aliases` on an aggregate chapter is silent",
        dump(issues)
    );
}

// How a bounded context ships — its own `service`, or a `module` in a modular
// monolith — belongs to the context, so `deployment` sits on
// `context-map.md`'s `bounded-context` chapter and nowhere else.
{
    const issues = validateDocument(
        ".devbook/domain/context-map.md",
        `# Order Platform\n\n${fence("type: context-map\n")}\n## Ordering\n\n` +
            `${fence("type: bounded-context\ndeployment: service\n")}\nProse.\n\n## Invoicing\n\n` +
            `${fence("type: bounded-context\ndeployment: module\n")}\nProse.\n\n## Shipping\n\n` +
            `${fence("type: bounded-context\n")}\nProse.\n`
    );

    check(
        !issues.some((i) => i.severity === "error"),
        "`deployment` of `service`, `module`, or none on a bounded context is silent",
        dump(issues)
    );
}
{
    const issues = validateDocument(
        ".devbook/domain/context-map.md",
        `# Order Platform\n\n${fence("type: context-map\ndeployment: module\n")}\n## Ordering\n\n` +
            `${fence("type: bounded-context\ndeployment: lambda\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", "`deployment` on the file-level block")),
        "`deployment` on the file-level block is an error",
        dump(issues)
    );
    check(
        Boolean(find(issues, "error", '`deployment` "lambda", expected one of: service, module')),
        "a `deployment` outside the two values is an error",
        dump(issues)
    );
}
{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n` +
            `${fence("type: aggregate\ndeployment: service\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", '`deployment` on a chapter of type "aggregate"')),
        "`deployment` on an aggregate is an error",
        dump(issues)
    );
}

// `context.md` is the context itself, so its file-level block carries the
// same `deployment` as the context's chapter on the map. Only that file type
// gets the exception: the map's own file-level block is still an error above.
{
    const issues = validateDocument(
        ".devbook/domain/ordering/context.md",
        `# Ordering\n\n${fence("type: context\nindex: root\ndeployment: service\n")}\nProse.\n`
    );

    check(
        !issues.some((i) => i.severity === "error"),
        "`deployment` on `context.md`'s file-level block is silent",
        dump(issues)
    );
}
{
    const issues = validateDocument(
        ".devbook/domain/ordering/context.md",
        `# Ordering\n\n${fence("type: context\nindex: root\ndeployment: serverless\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", '`deployment` "serverless", expected one of: service, module')),
        "a `deployment` outside the two values on `context.md` is an error",
        dump(issues)
    );
}
{
    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\ndeployment: module\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", "`deployment` on the file-level block")),
        "`deployment` on another file type's file-level block is still an error",
        dump(issues)
    );
}

// --- .ai `stage` is a chapter's, never a file's ---------------------------

// A file groups chapters and places none of them on the loop: the chapter says
// its own stages, and a file-level `stage` would place them by implication.
// The vocabulary and the placement rules themselves are ai-loop.test.mjs's.
{
    const issues = validateDocument(
        ".devbook/ai/03-build.md",
        `# Build\n\n${fence("status: adopted\ntype: stage\nstage: build\n")}\n## TDD with an agent\n\n` +
            `${fence("status: trial\ntype: practice\nstage: build\n")}\nProse.\n`
    );

    check(
        Boolean(find(issues, "error", "`stage` on the file-level block")),
        "`stage` on a file-level block is an error",
        dump(issues)
    );
    check(
        !find(issues, "error", "## TDD with an agent") && !find(issues, "warning", "## TDD with an agent"),
        "`stage` on the chapter, where it belongs, is silent",
        dump(issues)
    );
}

// --- an open question means the chapter is not agreed -------------------

// Reported only against an approval, which is the contradiction: a person
// signed for content that still carries an unanswered question. An open
// question on any other rung is the state the fence exists for.
{
    const approved = "type: aggregate\nstatus: approved\napproved-by: jobsc\napproved-at: 2026-09-01\n";
    const question = "kind: question\nauthor: jobsc\ndate: 2026-09-02\nbody: Does this still hold?\n";

    const issues = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence(approved)}\n${note(question)}\nProse.\n`
    );
    check(
        Boolean(find(issues, "error", "an open question means the chapter is not agreed")),
        "an approval standing over an open question is an error",
        dump(issues)
    );

    const resolved = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence(approved)}\n` +
            `${note("kind: question\nstatus: resolved\n" + question.split("\n").slice(1).join("\n"))}\nProse.\n`
    );
    check(
        resolved.length === 0,
        "a resolved question under the same approval is silent",
        dump(resolved)
    );

    const active = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence("type: aggregate\n")}\n${note(question)}\nProse.\n`
    );
    check(
        active.length === 0,
        "an open question on an unapproved chapter is silent — that is what the fence is for",
        dump(active)
    );

    // Position is the anchor: the note under the sub-chapter is the
    // sub-chapter's, so the approved parent above it stays clean.
    const nested = validateDocument(
        ".devbook/domain/ordering/domain.md",
        `# Ordering\n\n${fence("type: domain\n")}\n## Order\n\n${fence(approved)}\nProse.\n\n` +
            `### Line\n\n${fence("type: entity\n")}\n${note(question)}\nProse.\n`
    );
    check(
        !find(nested, "error", "not agreed"),
        "a question under a sub-chapter does not indict its approved parent",
        dump(nested)
    );
}

// --- slugs outside ASCII ------------------------------------------------

// `\w` is ASCII-only, so the old class dropped the accented letter and the
// address a `related` field carried stopped resolving to the heading GitHub
// actually renders.
check(
    slugify("Café Ordering") === "café-ordering",
    "a non-ASCII letter survives the slug",
    slugify("Café Ordering")
);
check(
    slugify("Organizational & Process Constraints") === "organizational--process-constraints",
    "punctuation still strips without collapsing the run it leaves behind",
    slugify("Organizational & Process Constraints")
);

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
