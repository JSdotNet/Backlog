// additional-page.test.mjs — a bounded context may carry a page the convention
// does not name, without the convention having to learn about it.
//
// The listed file types are the files with documented responsibilities, not the
// only files permitted. An unlisted one is accepted on the rule every listed
// one already follows — a file's `type` is its filename — which is also what
// still catches the typo the closed list was buying.
//
// Run: node plugins/devbook/tools/devbook-meta/additional-page.test.mjs

import { validateDocument } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (ok) console.log(`PASS  ${name}`);
    else { failed++; console.log(`FAIL  ${name}${detail ? `\n${detail}` : ""}`); }
};

const file = (type) =>
    "# Financieel\n\n```meta\nstatus: draft\ntype: " + type + "\n```\n\nProse.\n\n## A Section\n\nMore prose.\n";

const errors = (path, type) =>
    validateDocument(path, file(type)).filter((i) => i.severity === "error");
const dump = (list) => JSON.stringify(list, null, 2);

{
    const e = errors(".devbook/domain/financieel/import.md", "import");
    check(e.length === 0, "a page the convention does not name is accepted", dump(e));
}

{
    const e = errors(".devbook/domain/financieel/regulatory-annex.md", "regulatory-annex");
    check(e.length === 0, "a kebab-case additional page is accepted", dump(e));
}

{
    // The reason the closed list existed: a misspelled prescribed type must
    // still fail, and it does, because it matches no filename either.
    const e = errors(".devbook/domain/financieel/context.md", "contex");
    check(e.length === 1 && e[0].message.includes("contex"), "a typo in a prescribed type still errors", dump(e));
}

{
    // A type that is neither prescribed nor this file's name is still wrong.
    const e = errors(".devbook/domain/financieel/import.md", "nonsense");
    check(e.length === 1, "an additional page must be typed for itself, not arbitrarily", dump(e));
}

{
    // The message points at the way out, so an author who meant to add a page
    // does not have to find this rule to discover it exists.
    const e = errors(".devbook/domain/financieel/import.md", "nonsense");
    check(e[0].message.includes(`or "import" to match this file's own name`), "the error names the filename option", dump(e));
}

{
    // A split file is the kind of the file it is named after, so the base is
    // taken before the first dot, not after the last.
    const e = errors(".devbook/domain/financieel/domain.order.md", "domain");
    check(e.length === 0, "a split file still types as its base", dump(e));
}

{
    // The prescribed files are unaffected.
    for (const [name, type] of [["context.md", "context"], ["model.md", "model"], ["dependencies.md", "dependencies"]]) {
        const e = errors(`.devbook/domain/financieel/${name}`, type);
        check(e.length === 0, `${name} still validates as ${type}`, dump(e));
    }
}

{
    // The freedom is `domain/`'s file level only. `ai/` names its file types
    // and its files are numbered, so a filename is not a type there.
    const e = errors(".devbook/ai/03-build.md", "03-build");
    check(
        e.some((i) => i.message.includes(`has type "03-build"`)),
        "an ai/ file type is not derived from its filename",
        dump(e),
    );
}

{
    // Chapter level is unchanged: the vocabulary is closed inside a file.
    const doc = "# Financieel\n\n```meta\ntype: domain\n```\n\n## Order\n\n```meta\ntype: order\n```\n\nProse.\n";
    const e = validateDocument(".devbook/domain/financieel/domain.md", doc).filter((i) => i.severity === "error");
    check(e.length === 1, "a chapter type is still held to the closed set", dump(e));
}

console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
