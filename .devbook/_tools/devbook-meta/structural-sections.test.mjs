// structural-sections.test.mjs — a structural document's `##` sections are
// sections, not chapters, so a heading there without a `meta` block is what the
// folder rules ask for and draws no warning.
//
// The rules name the files: `context-map.md`, `model.md`, `flow.md`, their
// splits, `dependencies.md`, and any additional page in `domain/`;
// `technology-graph.md` in `tech/`; `adoption-map.md` in `ai/`. Everywhere else
// a heading without a block is still reported.
//
// Run: node plugins/devbook/tools/devbook-meta/structural-sections.test.mjs

import { isStructuralDocument, validateDocument } from "./metadata.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (ok) console.log(`PASS  ${name}`);
    else { failed++; console.log(`FAIL  ${name}${detail ? `\n${detail}` : ""}`); }
};

const doc = (fileMeta) =>
    "# Title\n\n```meta\n" + fileMeta + "```\n\nProse.\n\n## A Section\n\nMore prose.\n";
const missing = (path, fileMeta) =>
    validateDocument(path, doc(fileMeta)).filter((i) => i.message.includes("has no `meta` block"));
const dump = (list) => JSON.stringify(list, null, 2);

for (const [path, fileMeta] of [
    [".devbook/tech/technology-graph.md", "status: adopt\nindex: root\n"],
    [".devbook/ai/adoption-map.md", "status: trial\nindex: root\ntype: adoption-map\n"],
    [".devbook/domain/context-map.md", "index: root\ntype: context-map\n"],
    [".devbook/domain/ordering/model.md", "type: model\n"],
    [".devbook/domain/ordering/model.order.md", "type: model\n"],
    [".devbook/domain/ordering/flow.md", "type: flow\n"],
    [".devbook/domain/ordering/dependencies.md", "type: dependencies\n"],
    [".devbook/domain/ordering/import.md", "type: import\n"],
]) {
    const m = missing(path, fileMeta);
    check(m.length === 0, `${path}: a section without a block is not reported`, dump(m));
}

for (const [path, fileMeta] of [
    [".devbook/tech/backend.md", "status: adopt\n"],
    [".devbook/ai/01-plan.md", "status: trial\ntype: stage\n"],
    [".devbook/domain/ordering/context.md", "index: root\ntype: context\n"],
    [".devbook/domain/ordering/domain.md", "type: domain\n"],
    [".devbook/arc42/05-building-block-view.md", "index: root\n"],
]) {
    const m = missing(path, fileMeta);
    check(m.length === 1, `${path}: a chapter without a block is still reported`, dump(m));
}

// Only the folder root's own file is structural: a same-named file deeper in
// the tree is an ordinary document.
check(!isStructuralDocument(".devbook/tech/sub/technology-graph.md"), "technology-graph.md below the tech root is not structural");
check(!isStructuralDocument(".devbook/ai/sub/adoption-map.md"), "adoption-map.md below the ai root is not structural");

// A section that does carry a block is still validated: a `bounded-context`
// section in context-map.md is a chapter, and a bad type there is an error.
{
    const md = "# Map\n\n```meta\nindex: root\ntype: context-map\n```\n\n## Ordering\n\n```meta\ntype: nonsense\n```\n\nProse.\n";
    const e = validateDocument(".devbook/domain/context-map.md", md).filter((i) => i.severity === "error");
    check(e.length === 1 && e[0].message.includes("nonsense"), "a block on a structural section is still validated", dump(e));
}

if (failed) {
    console.log(`\n${failed} failed`);
    process.exit(1);
}
console.log("\nall passed");
